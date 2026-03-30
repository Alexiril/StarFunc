"""Unit tests for ContentService — S2.4."""

from unittest.mock import AsyncMock, patch

import pytest

from app.domain.exceptions import NotFoundError
from app.infrastructure.persistence.models import ContentVersionModel
from app.services.content_service import ContentService


@pytest.fixture
def redis() -> AsyncMock:
    return AsyncMock()


@pytest.fixture
def content_service(redis: AsyncMock) -> ContentService:
    return ContentService(redis)


def _make_session() -> AsyncMock:
    session = AsyncMock()
    session.execute = AsyncMock()
    return session


def _make_content_row(
    content_type: str = "sector",
    content_id: str | None = "sector_1",
    version: int = 1,
    data: dict | None = None,
) -> ContentVersionModel:
    row = ContentVersionModel()
    row.id = 1
    row.content_type = content_type
    row.content_id = content_id
    row.version = version
    row.data = data or {}
    row.is_active = True
    return row


# ── get_manifest ─────────────────────────────────────────────


class TestGetManifest:
    @pytest.mark.asyncio
    async def test_returns_manifest_from_db(self, content_service: ContentService) -> None:
        session = _make_session()
        manifest_data = {
            "content_version": 3,
            "sectors": {"sector_1": 2},
            "balance_version": 1,
            "shop_version": 1,
        }

        with (
            patch("app.services.content_service.get_content", return_value=None) as mock_get,
            patch("app.services.content_service.set_content") as mock_set,
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_manifest.return_value = manifest_data
            repo_cls.return_value = repo

            result = await content_service.get_manifest(session)

        assert result.content_version == 3
        assert result.sectors == {"sector_1": 2}
        mock_get.assert_awaited_once()
        mock_set.assert_awaited_once()

    @pytest.mark.asyncio
    async def test_returns_manifest_from_cache(self, content_service: ContentService) -> None:
        session = _make_session()
        cached = {
            "content_version": 5,
            "sectors": {"sector_1": 3},
            "balance_version": 2,
            "shop_version": 1,
        }

        with patch("app.services.content_service.get_content", return_value=cached):
            result = await content_service.get_manifest(session)

        assert result.content_version == 5
        assert result.balance_version == 2


# ── get_sectors ──────────────────────────────────────────────


class TestGetSectors:
    @pytest.mark.asyncio
    async def test_returns_sectors_from_db(self, content_service: ContentService) -> None:
        session = _make_session()
        row1 = _make_content_row(data={"sector_id": "s1", "display_name": "Alpha"})
        row2 = _make_content_row(content_id="sector_2", data={"sector_id": "s2", "display_name": "Beta"})

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.set_content"),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_all_active_by_type.return_value = [row1, row2]
            repo_cls.return_value = repo

            result = await content_service.get_sectors(session)

        assert len(result.sectors) == 2
        assert result.sectors[0]["sector_id"] == "s1"

    @pytest.mark.asyncio
    async def test_returns_sectors_from_cache(self, content_service: ContentService) -> None:
        session = _make_session()
        cached = {"sectors": [{"sector_id": "s1"}]}

        with patch("app.services.content_service.get_content", return_value=cached):
            result = await content_service.get_sectors(session)

        assert len(result.sectors) == 1


# ── get_sector ───────────────────────────────────────────────


class TestGetSector:
    @pytest.mark.asyncio
    async def test_returns_sector_from_db(self, content_service: ContentService) -> None:
        session = _make_session()
        row = _make_content_row(data={"sector_id": "sector_1", "display_name": "Alpha"})

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.set_content"),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_active_content.return_value = row
            repo_cls.return_value = repo

            result = await content_service.get_sector("sector_1", session)

        assert result.sector["sector_id"] == "sector_1"

    @pytest.mark.asyncio
    async def test_not_found_raises(self, content_service: ContentService) -> None:
        session = _make_session()

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_active_content.return_value = None
            repo_cls.return_value = repo

            with pytest.raises(NotFoundError):
                await content_service.get_sector("nonexistent", session)


# ── get_levels ───────────────────────────────────────────────


class TestGetLevels:
    @pytest.mark.asyncio
    async def test_returns_levels_filtered_by_sector(self, content_service: ContentService) -> None:
        session = _make_session()
        row1 = _make_content_row(
            content_type="level",
            content_id="lv1",
            data={"level_id": "lv1", "sector_id": "sector_1"},
        )
        row2 = _make_content_row(
            content_type="level",
            content_id="lv2",
            data={"level_id": "lv2", "sector_id": "sector_2"},
        )

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.set_content"),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_all_active_by_type.return_value = [row1, row2]
            repo_cls.return_value = repo

            result = await content_service.get_levels("sector_1", session)

        assert len(result.levels) == 1
        assert result.levels[0]["level_id"] == "lv1"


# ── get_level ────────────────────────────────────────────────


class TestGetLevel:
    @pytest.mark.asyncio
    async def test_returns_level_from_db(self, content_service: ContentService) -> None:
        session = _make_session()
        row = _make_content_row(
            content_type="level",
            content_id="lv1",
            data={"level_id": "lv1", "sector_id": "sector_1"},
        )

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.set_content"),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_active_content.return_value = row
            repo_cls.return_value = repo

            result = await content_service.get_level("lv1", session)

        assert result.level["level_id"] == "lv1"

    @pytest.mark.asyncio
    async def test_not_found_raises(self, content_service: ContentService) -> None:
        session = _make_session()

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_active_content.return_value = None
            repo_cls.return_value = repo

            with pytest.raises(NotFoundError) as exc_info:
                await content_service.get_level("nonexistent", session)

            assert exc_info.value.details is not None
            assert exc_info.value.details["code"] == "LEVEL_NOT_FOUND"


# ── get_balance_config ───────────────────────────────────────


class TestGetBalanceConfig:
    @pytest.mark.asyncio
    async def test_returns_balance_from_db(self, content_service: ContentService) -> None:
        session = _make_session()
        row = _make_content_row(
            content_type="balance",
            content_id=None,
            data={"max_lives": 5, "restore_interval_seconds": 1800},
        )

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.set_content"),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_active_content.return_value = row
            repo_cls.return_value = repo

            result = await content_service.get_balance_config(session)

        assert result.config["max_lives"] == 5

    @pytest.mark.asyncio
    async def test_not_found_raises(self, content_service: ContentService) -> None:
        session = _make_session()

        with (
            patch("app.services.content_service.get_content", return_value=None),
            patch("app.services.content_service.ContentRepository") as repo_cls,
        ):
            repo = AsyncMock()
            repo.get_active_content.return_value = None
            repo_cls.return_value = repo

            with pytest.raises(NotFoundError):
                await content_service.get_balance_config(session)


# ── cache hit paths ──────────────────────────────────────────


class TestCacheHit:
    @pytest.mark.asyncio
    async def test_get_sector_from_cache(self, content_service: ContentService) -> None:
        session = _make_session()
        cached = {"sector": {"sector_id": "s1"}}

        with patch("app.services.content_service.get_content", return_value=cached):
            result = await content_service.get_sector("s1", session)

        assert result.sector["sector_id"] == "s1"

    @pytest.mark.asyncio
    async def test_get_levels_from_cache(self, content_service: ContentService) -> None:
        session = _make_session()
        cached = {"levels": [{"level_id": "lv1"}]}

        with patch("app.services.content_service.get_content", return_value=cached):
            result = await content_service.get_levels("sector_1", session)

        assert len(result.levels) == 1

    @pytest.mark.asyncio
    async def test_get_level_from_cache(self, content_service: ContentService) -> None:
        session = _make_session()
        cached = {"level": {"level_id": "lv1"}}

        with patch("app.services.content_service.get_content", return_value=cached):
            result = await content_service.get_level("lv1", session)

        assert result.level["level_id"] == "lv1"

    @pytest.mark.asyncio
    async def test_get_balance_from_cache(self, content_service: ContentService) -> None:
        session = _make_session()
        cached = {"config": {"max_lives": 5}}

        with patch("app.services.content_service.get_content", return_value=cached):
            result = await content_service.get_balance_config(session)

        assert result.config["max_lives"] == 5
