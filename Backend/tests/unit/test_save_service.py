"""Unit tests for SaveService — S2.1."""

from datetime import UTC, datetime
from unittest.mock import AsyncMock, MagicMock, patch
from uuid import uuid4

import pytest

from app.api.schemas.save import SaveRequest
from app.domain.exceptions import ConflictError
from app.infrastructure.persistence.models import PlayerSaveModel
from app.services.save_service import _DEFAULT_SAVE_DATA, SaveService


@pytest.fixture
def save_service() -> SaveService:
    return SaveService()


def _make_session() -> AsyncMock:
    session = AsyncMock()
    session.add = MagicMock()
    session.flush = AsyncMock()
    session.commit = AsyncMock()
    session.execute = AsyncMock()
    return session


def _make_save_model(
    player_id=None,
    version: int = 1,
    save_version: int = 1,
    save_data: dict | None = None,
) -> PlayerSaveModel:
    model = PlayerSaveModel(
        id=1,
        player_id=player_id or uuid4(),
        version=version,
        save_version=save_version,
        save_data=save_data or _DEFAULT_SAVE_DATA.copy(),
    )
    model.created_at = datetime.now(UTC)
    model.updated_at = datetime.now(UTC)
    return model


class TestGetSave:
    @pytest.mark.asyncio
    async def test_returns_existing_save(self, save_service: SaveService) -> None:
        session = _make_session()
        player_id = uuid4()
        save_data = {**_DEFAULT_SAVE_DATA, "totalFragments": 42}
        existing_save = _make_save_model(player_id=player_id, version=3, save_data=save_data)

        with patch("app.services.save_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id.return_value = existing_save
            repo_cls.return_value = repo

            result = await save_service.get_save(player_id, session)

        assert result.version == 3
        assert result.save_data["totalFragments"] == 42
        assert result.updated_at == int(existing_save.updated_at.timestamp())

    @pytest.mark.asyncio
    async def test_returns_default_save_for_new_player(self, save_service: SaveService) -> None:
        session = _make_session()
        player_id = uuid4()

        with patch("app.services.save_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id.return_value = None
            repo_cls.return_value = repo

            result = await save_service.get_save(player_id, session)

        assert result.version == 1
        assert result.save_version == 1
        assert result.updated_at == 0
        assert result.save_data["currentLives"] == 5
        assert result.save_data["totalFragments"] == 0


class TestPutSave:
    @pytest.mark.asyncio
    async def test_successful_update_with_matching_version(self, save_service: SaveService) -> None:
        session = _make_session()
        player_id = uuid4()
        existing_save = _make_save_model(player_id=player_id, version=3)

        request = SaveRequest(
            save_data={"totalFragments": 100, **_DEFAULT_SAVE_DATA},
            expected_version=3,
        )

        with patch("app.services.save_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id_for_update.return_value = existing_save
            repo_cls.return_value = repo

            result = await save_service.put_save(player_id, request, session)

        assert result.version == 4
        repo.update.assert_awaited_once_with(existing_save, request.save_data, 4)
        session.commit.assert_awaited_once()

    @pytest.mark.asyncio
    async def test_conflict_on_version_mismatch(self, save_service: SaveService) -> None:
        session = _make_session()
        player_id = uuid4()
        existing_save = _make_save_model(player_id=player_id, version=5)

        request = SaveRequest(
            save_data={"totalFragments": 100, **_DEFAULT_SAVE_DATA},
            expected_version=3,
        )

        with patch("app.services.save_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id_for_update.return_value = existing_save
            repo_cls.return_value = repo

            with pytest.raises(ConflictError) as exc_info:
                await save_service.put_save(player_id, request, session)

        assert exc_info.value.code == "SAVE_CONFLICT"
        assert exc_info.value.status_code == 409
        assert exc_info.value.details is not None
        assert exc_info.value.details["serverSave"]["version"] == 5

    @pytest.mark.asyncio
    async def test_creates_save_for_new_player(self, save_service: SaveService) -> None:
        session = _make_session()
        player_id = uuid4()

        new_save_data = {**_DEFAULT_SAVE_DATA, "totalFragments": 10}
        request = SaveRequest(save_data=new_save_data, expected_version=1)

        created_save = _make_save_model(player_id=player_id, version=1, save_data=new_save_data)

        with patch("app.services.save_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id_for_update.return_value = None
            repo.create.return_value = created_save
            repo_cls.return_value = repo

            result = await save_service.put_save(player_id, request, session)

        assert result.version == 2
        repo.create.assert_awaited_once_with(player_id, new_save_data)
        session.commit.assert_awaited_once()

    @pytest.mark.asyncio
    async def test_conflict_on_new_player_wrong_expected_version(self, save_service: SaveService) -> None:
        session = _make_session()
        player_id = uuid4()

        request = SaveRequest(
            save_data=_DEFAULT_SAVE_DATA.copy(),
            expected_version=5,
        )

        with patch("app.services.save_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id_for_update.return_value = None
            repo_cls.return_value = repo

            with pytest.raises(ConflictError) as exc_info:
                await save_service.put_save(player_id, request, session)

        assert exc_info.value.code == "SAVE_CONFLICT"
        assert exc_info.value.details is not None
        assert exc_info.value.details["serverSave"]["version"] == 1
