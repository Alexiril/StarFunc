"""EconomyService — balance queries and atomic transactions."""

from __future__ import annotations

import copy
from typing import Any
from uuid import UUID

import structlog
from redis.asyncio import Redis
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.schemas.economy import SkipLevelProgressUpdate, TransactionRequest, TransactionResponse
from app.config import Settings
from app.domain.exceptions import InsufficientFundsError, NotFoundError
from app.domain.rules.economy import EconomyRules
from app.infrastructure.persistence.models import ContentVersionModel, PlayerSaveModel
from app.infrastructure.persistence.save_repo import SaveRepository
from app.infrastructure.persistence.transaction_repo import TransactionRepository
from app.infrastructure.redis import delete_cached, get_cached, set_cached

logger = structlog.stdlib.get_logger()

_BALANCE_CACHE_KEY = "player:balance:{player_id}"
_BALANCE_CACHE_TTL = 300  # 5 minutes


class EconomyService:
    def __init__(self, redis: Redis, settings: Settings) -> None:
        self._redis = redis
        self._settings = settings
        self._rules = EconomyRules()

    # ------------------------------------------------------------------
    # GET balance
    # ------------------------------------------------------------------
    async def get_balance(self, player_id: UUID, session: AsyncSession) -> int:
        cache_key = _BALANCE_CACHE_KEY.format(player_id=player_id)

        cached = await get_cached(self._redis, cache_key)
        if cached is not None:
            return int(cached)

        repo = SaveRepository(session)
        save = await repo.find_by_player_id(player_id)
        if save is None:
            balance = 0
        else:
            balance = int(save.save_data.get("totalFragments", 0))

        await set_cached(self._redis, cache_key, balance, _BALANCE_CACHE_TTL)
        return balance

    # ------------------------------------------------------------------
    # POST transaction
    # ------------------------------------------------------------------
    async def execute_transaction(
        self,
        player_id: UUID,
        request: TransactionRequest,
        idempotency_key: UUID | None,
        session: AsyncSession,
    ) -> TransactionResponse:
        tx_repo = TransactionRepository(session)

        # 1. Idempotency check
        if idempotency_key is not None:
            existing = await tx_repo.find_by_idempotency_key(idempotency_key)
            if existing is not None:
                return TransactionResponse(
                    transaction_id=existing.id,
                    previous_balance=existing.previous_bal,
                    new_balance=existing.new_bal,
                    progress_update=None,
                )

        # 2. SELECT … FOR UPDATE on player_saves
        save_repo = SaveRepository(session)
        save = await save_repo.find_by_player_id_for_update(player_id)
        if save is None:
            raise NotFoundError(message="Player save not found")

        # 3. Current balance from save_data
        save_data: dict[str, Any] = copy.deepcopy(save.save_data)
        current_balance = int(save_data.get("totalFragments", 0))

        # 4. Validation for spend
        if not self._rules.validate_transaction(request.type, request.amount, current_balance):
            raise InsufficientFundsError(
                details={"required": request.amount, "available": current_balance},
            )

        # 5. Compute new balance
        if request.type == "earn":
            new_balance = current_balance + request.amount
        else:
            new_balance = current_balance - request.amount

        save_data["totalFragments"] = new_balance

        # 6. Skip-level progression update
        progress_update: SkipLevelProgressUpdate | None = None
        if request.reason == "skip_level":
            progress_update = await self._apply_skip_level(save_data, request.reference_id, session)

        # 7. Persist save_data
        save.save_data = save_data
        await session.flush()

        # 8. Record transaction
        tx = await tx_repo.create(
            player_id=player_id,
            type=request.type,
            amount=request.amount,
            reason=request.reason,
            reference_id=request.reference_id,
            previous_bal=current_balance,
            new_bal=new_balance,
            idempotency_key=idempotency_key,
        )

        await session.commit()

        # 9. Invalidate balance cache
        cache_key = _BALANCE_CACHE_KEY.format(player_id=player_id)
        await delete_cached(self._redis, cache_key)

        return TransactionResponse(
            transaction_id=tx.id,
            previous_balance=current_balance,
            new_balance=new_balance,
            progress_update=progress_update,
        )

    # ------------------------------------------------------------------
    # Skip-level helper
    # ------------------------------------------------------------------
    async def _apply_skip_level(
        self,
        save_data: dict[str, Any],
        level_id: str | None,
        session: AsyncSession,
    ) -> SkipLevelProgressUpdate:
        if not level_id:
            raise NotFoundError(message="reference_id (levelId) is required for skip_level")

        # Load LevelDefinition from content_versions
        stmt = select(ContentVersionModel).where(
            ContentVersionModel.content_type == "level",
            ContentVersionModel.content_id == level_id,
            ContentVersionModel.is_active.is_(True),
        )
        result = await session.execute(stmt)
        content = result.scalar_one_or_none()
        if content is None:
            raise NotFoundError(message=f"Level definition not found: {level_id}")

        level_def = content.data
        sector_id: str = level_def.get("sectorId", "")

        # Update level_progress
        level_progress: dict[str, Any] = save_data.setdefault("levelProgress", {})
        level_progress[level_id] = {
            "isCompleted": True,
            "bestStars": 1,
            "bestTime": 0,
            "attempts": 0,
        }

        # Update sector_progress
        sector_progress: dict[str, Any] = save_data.setdefault("sectorProgress", {})
        sector_data: dict[str, Any] = sector_progress.setdefault(
            sector_id, {"state": "InProgress", "starsCollected": 0, "controlLevelPassed": False}
        )
        sector_data["starsCollected"] = sector_data.get("starsCollected", 0) + 1
        if sector_data.get("state") == "Available":
            sector_data["state"] = "InProgress"

        # Update global stats
        save_data["totalLevelsCompleted"] = save_data.get("totalLevelsCompleted", 0) + 1
        save_data["totalStarsCollected"] = save_data.get("totalStarsCollected", 0) + 1

        # Increment save version
        save_data["version"] = save_data.get("version", 1) + 1

        # ProgressionRules stub — real implementation in S3.3
        unlocked_levels: list[str] = []
        unlocked_sectors: list[str] = []

        return SkipLevelProgressUpdate(
            level_id=level_id,
            stars=1,
            unlocked_levels=unlocked_levels,
            unlocked_sectors=unlocked_sectors,
        )
