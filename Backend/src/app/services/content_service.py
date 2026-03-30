"""ContentService — serves game content with Redis caching."""

from __future__ import annotations

import structlog
from redis.asyncio import Redis
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.schemas.content import (
    BalanceConfigResponse,
    ContentManifestResponse,
    LevelResponse,
    LevelsResponse,
    SectorResponse,
    SectorsResponse,
)
from app.domain.exceptions import NotFoundError
from app.infrastructure.cache.content_cache import get_content, set_content
from app.infrastructure.persistence.content_repo import ContentRepository

logger = structlog.stdlib.get_logger()


class ContentService:
    def __init__(self, redis: Redis) -> None:
        self._redis = redis

    async def get_manifest(self, session: AsyncSession) -> ContentManifestResponse:
        cached = await get_content(self._redis, "manifest")
        if cached is not None:
            return ContentManifestResponse(**cached)

        repo = ContentRepository(session)
        manifest = await repo.get_manifest()

        await set_content(self._redis, "manifest", manifest)
        return ContentManifestResponse(**manifest)

    async def get_sectors(self, session: AsyncSession) -> SectorsResponse:
        cached = await get_content(self._redis, "sectors")
        if cached is not None:
            return SectorsResponse(**cached)

        repo = ContentRepository(session)
        rows = await repo.get_all_active_by_type("sector")
        sectors = [row.data for row in rows]
        payload = {"sectors": sectors}

        await set_content(self._redis, "sectors", payload)
        return SectorsResponse(**payload)

    async def get_sector(self, sector_id: str, session: AsyncSession) -> SectorResponse:
        cached = await get_content(self._redis, "sector", content_id=sector_id)
        if cached is not None:
            return SectorResponse(**cached)

        repo = ContentRepository(session)
        row = await repo.get_active_content("sector", sector_id)
        if row is None:
            raise NotFoundError(
                message="Sector not found",
                details={"sector_id": sector_id},
            )

        payload = {"sector": row.data}
        await set_content(self._redis, "sector", payload, content_id=sector_id)
        return SectorResponse(**payload)

    async def get_levels(self, sector_id: str, session: AsyncSession) -> LevelsResponse:
        cached = await get_content(self._redis, "levels", content_id=sector_id)
        if cached is not None:
            return LevelsResponse(**cached)

        repo = ContentRepository(session)
        rows = await repo.get_all_active_by_type("level")
        levels = [row.data for row in rows if row.data.get("sectorId") == sector_id]
        payload = {"levels": levels}

        await set_content(self._redis, "levels", payload, content_id=sector_id)
        return LevelsResponse(**payload)

    async def get_level(self, level_id: str, session: AsyncSession) -> LevelResponse:
        cache_key_id = f"level:{level_id}"
        cached = await get_content(self._redis, "levels", content_id=cache_key_id)
        if cached is not None:
            return LevelResponse(**cached)

        repo = ContentRepository(session)
        row = await repo.get_active_content("level", level_id)
        if row is None:
            raise NotFoundError(
                message="Level not found",
                details={"level_id": level_id, "code": "LEVEL_NOT_FOUND"},
            )

        payload = {"level": row.data}
        await set_content(self._redis, "levels", payload, content_id=cache_key_id)
        return LevelResponse(**payload)

    async def get_balance_config(self, session: AsyncSession) -> BalanceConfigResponse:
        cached = await get_content(self._redis, "balance")
        if cached is not None:
            return BalanceConfigResponse(**cached)

        repo = ContentRepository(session)
        row = await repo.get_active_content("balance", None)
        if row is None:
            raise NotFoundError(message="Balance config not found")

        payload = {"config": row.data}
        await set_content(self._redis, "balance", payload)
        return BalanceConfigResponse(**payload)
