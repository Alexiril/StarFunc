"""Service-level tests for EconomyService — S2.2."""

from datetime import UTC, datetime
from unittest.mock import AsyncMock, MagicMock, patch
from uuid import uuid4

import pytest

from app.api.schemas.economy import TransactionRequest
from app.config import Settings
from app.domain.exceptions import InsufficientFundsError, NotFoundError
from app.infrastructure.persistence.models import PlayerSaveModel, TransactionModel
from app.services.economy_service import EconomyService


@pytest.fixture
def settings() -> Settings:
    return Settings(  # type: ignore[call-arg]
        database_url="postgresql+asyncpg://test:test@localhost:5432/test",
        jwt_secret="test-secret-key-minimum-256-bits-long-key",
        improvement_bonus_per_star=5,
        skip_level_cost_fragments=100,
    )


@pytest.fixture
def redis() -> AsyncMock:
    r = AsyncMock()
    r.get = AsyncMock(return_value=None)
    r.set = AsyncMock()
    r.delete = AsyncMock()
    return r


@pytest.fixture
def economy_service(redis: AsyncMock, settings: Settings) -> EconomyService:
    return EconomyService(redis, settings)


def _make_session() -> AsyncMock:
    session = AsyncMock()
    session.add = MagicMock()
    session.flush = AsyncMock()
    session.commit = AsyncMock()
    session.execute = AsyncMock()
    return session


def _make_save_model(
    player_id=None,
    total_fragments: int = 100,
    version: int = 1,
) -> PlayerSaveModel:
    model = PlayerSaveModel(
        id=1,
        player_id=player_id or uuid4(),
        version=version,
        save_version=1,
        save_data={
            "saveVersion": 1,
            "version": version,
            "totalFragments": total_fragments,
            "currentLives": 5,
            "lastLifeRestoreTimestamp": 0,
            "sectorProgress": {},
            "levelProgress": {},
            "totalLevelsCompleted": 0,
            "totalStarsCollected": 0,
        },
    )
    model.created_at = datetime.now(UTC)
    model.updated_at = datetime.now(UTC)
    return model


def _make_tx_model(
    player_id=None,
    tx_type: str = "earn",
    amount: int = 10,
    previous_bal: int = 100,
    new_bal: int = 110,
) -> TransactionModel:
    model = TransactionModel(
        id=uuid4(),
        player_id=player_id or uuid4(),
        type=tx_type,
        amount=amount,
        reason="test",
        previous_bal=previous_bal,
        new_bal=new_bal,
        idempotency_key=uuid4(),
    )
    return model


class TestGetBalance:
    @pytest.mark.asyncio
    async def test_returns_balance_from_save(self, economy_service: EconomyService) -> None:
        session = _make_session()
        player_id = uuid4()
        save = _make_save_model(player_id=player_id, total_fragments=42)

        with patch("app.services.economy_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id.return_value = save
            repo_cls.return_value = repo

            balance = await economy_service.get_balance(player_id, session)

        assert balance == 42

    @pytest.mark.asyncio
    async def test_returns_zero_for_missing_save(self, economy_service: EconomyService) -> None:
        session = _make_session()
        player_id = uuid4()

        with patch("app.services.economy_service.SaveRepository") as repo_cls:
            repo = AsyncMock()
            repo.find_by_player_id.return_value = None
            repo_cls.return_value = repo

            balance = await economy_service.get_balance(player_id, session)

        assert balance == 0

    @pytest.mark.asyncio
    async def test_returns_cached_balance(self, economy_service: EconomyService, redis: AsyncMock) -> None:
        session = _make_session()
        player_id = uuid4()

        with patch("app.services.economy_service.get_cached", new_callable=AsyncMock) as mock_cached:
            mock_cached.return_value = 99

            balance = await economy_service.get_balance(player_id, session)

        assert balance == 99


class TestExecuteTransaction:
    @pytest.mark.asyncio
    async def test_earn_increases_balance(self, economy_service: EconomyService) -> None:
        session = _make_session()
        player_id = uuid4()
        save = _make_save_model(player_id=player_id, total_fragments=100)
        tx = _make_tx_model(player_id=player_id, tx_type="earn", amount=50, previous_bal=100, new_bal=150)

        request = TransactionRequest(type="earn", amount=50, reason="level_completion")

        with (
            patch("app.services.economy_service.SaveRepository") as save_repo_cls,
            patch("app.services.economy_service.TransactionRepository") as tx_repo_cls,
        ):
            save_repo = AsyncMock()
            save_repo.find_by_player_id_for_update.return_value = save
            save_repo_cls.return_value = save_repo

            tx_repo = AsyncMock()
            tx_repo.find_by_idempotency_key.return_value = None
            tx_repo.create.return_value = tx
            tx_repo_cls.return_value = tx_repo

            result = await economy_service.execute_transaction(player_id, request, None, session)

        assert result.previous_balance == 100
        assert result.new_balance == 150

    @pytest.mark.asyncio
    async def test_spend_decreases_balance(self, economy_service: EconomyService) -> None:
        session = _make_session()
        player_id = uuid4()
        save = _make_save_model(player_id=player_id, total_fragments=200)
        tx = _make_tx_model(player_id=player_id, tx_type="spend", amount=50, previous_bal=200, new_bal=150)

        request = TransactionRequest(type="spend", amount=50, reason="shop_purchase")

        with (
            patch("app.services.economy_service.SaveRepository") as save_repo_cls,
            patch("app.services.economy_service.TransactionRepository") as tx_repo_cls,
        ):
            save_repo = AsyncMock()
            save_repo.find_by_player_id_for_update.return_value = save
            save_repo_cls.return_value = save_repo

            tx_repo = AsyncMock()
            tx_repo.find_by_idempotency_key.return_value = None
            tx_repo.create.return_value = tx
            tx_repo_cls.return_value = tx_repo

            result = await economy_service.execute_transaction(player_id, request, None, session)

        assert result.previous_balance == 200
        assert result.new_balance == 150

    @pytest.mark.asyncio
    async def test_spend_insufficient_funds_raises(self, economy_service: EconomyService) -> None:
        session = _make_session()
        player_id = uuid4()
        save = _make_save_model(player_id=player_id, total_fragments=30)

        request = TransactionRequest(type="spend", amount=50, reason="shop_purchase")

        with (
            patch("app.services.economy_service.SaveRepository") as save_repo_cls,
            patch("app.services.economy_service.TransactionRepository") as tx_repo_cls,
        ):
            save_repo = AsyncMock()
            save_repo.find_by_player_id_for_update.return_value = save
            save_repo_cls.return_value = save_repo

            tx_repo = AsyncMock()
            tx_repo.find_by_idempotency_key.return_value = None
            tx_repo_cls.return_value = tx_repo

            with pytest.raises(InsufficientFundsError) as exc_info:
                await economy_service.execute_transaction(player_id, request, None, session)

        assert exc_info.value.details is not None
        assert exc_info.value.details["required"] == 50
        assert exc_info.value.details["available"] == 30

    @pytest.mark.asyncio
    async def test_idempotency_returns_same_response(self, economy_service: EconomyService) -> None:
        session = _make_session()
        player_id = uuid4()
        idem_key = uuid4()

        existing_tx = _make_tx_model(
            player_id=player_id,
            tx_type="earn",
            amount=50,
            previous_bal=100,
            new_bal=150,
        )

        request = TransactionRequest(type="earn", amount=50, reason="level_completion")

        with patch("app.services.economy_service.TransactionRepository") as tx_repo_cls:
            tx_repo = AsyncMock()
            tx_repo.find_by_idempotency_key.return_value = existing_tx
            tx_repo_cls.return_value = tx_repo

            result = await economy_service.execute_transaction(player_id, request, idem_key, session)

        assert result.transaction_id == existing_tx.id
        assert result.previous_balance == 100
        assert result.new_balance == 150
        assert result.progress_update is None

    @pytest.mark.asyncio
    async def test_missing_save_raises_not_found(self, economy_service: EconomyService) -> None:
        session = _make_session()
        player_id = uuid4()

        request = TransactionRequest(type="earn", amount=50, reason="test")

        with (
            patch("app.services.economy_service.SaveRepository") as save_repo_cls,
            patch("app.services.economy_service.TransactionRepository") as tx_repo_cls,
        ):
            save_repo = AsyncMock()
            save_repo.find_by_player_id_for_update.return_value = None
            save_repo_cls.return_value = save_repo

            tx_repo = AsyncMock()
            tx_repo.find_by_idempotency_key.return_value = None
            tx_repo_cls.return_value = tx_repo

            with pytest.raises(NotFoundError):
                await economy_service.execute_transaction(player_id, request, None, session)
