"""Content repository — database access for content_versions table."""

from __future__ import annotations

from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.infrastructure.persistence.models import ContentVersionModel


class ContentRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_active_content(
        self,
        content_type: str,
        content_id: str | None,
    ) -> ContentVersionModel | None:
        stmt = (
            select(ContentVersionModel)
            .where(
                ContentVersionModel.content_type == content_type,
                ContentVersionModel.content_id == content_id,
                ContentVersionModel.is_active.is_(True),
            )
            .order_by(ContentVersionModel.version.desc())
            .limit(1)
        )
        result = await self._session.execute(stmt)
        return result.scalar_one_or_none()

    async def get_all_active_by_type(
        self,
        content_type: str,
    ) -> list[ContentVersionModel]:
        stmt = (
            select(ContentVersionModel)
            .where(
                ContentVersionModel.content_type == content_type,
                ContentVersionModel.is_active.is_(True),
            )
            .order_by(ContentVersionModel.version.desc())
        )
        result = await self._session.execute(stmt)
        return list(result.scalars().all())

    async def get_manifest(self) -> dict[str, Any]:
        """Aggregate versions of all active content types into a manifest."""
        stmt = select(ContentVersionModel).where(ContentVersionModel.is_active.is_(True))
        result = await self._session.execute(stmt)
        rows = result.scalars().all()

        sectors: dict[str, int] = {}
        content_version = 0
        balance_version = 1
        shop_version = 1

        for row in rows:
            if row.version > content_version:
                content_version = row.version
            if row.content_type == "sector" and row.content_id:
                sectors[row.content_id] = row.version
            elif row.content_type == "balance":
                balance_version = row.version
            elif row.content_type == "shop_catalog":
                shop_version = row.version

        return {
            "content_version": content_version,
            "sectors": sectors,
            "balance_version": balance_version,
            "shop_version": shop_version,
        }
