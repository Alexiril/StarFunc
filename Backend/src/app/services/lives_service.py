"""LivesService — server-side lives recalculation and fragment-based restores."""

from __future__ import annotations

import copy
from datetime import UTC, datetime
from typing import Any
from uuid import UUID

import structlog
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.schemas.lives import LivesResponse, RestoreAllResponse, RestoreLifeResponse
from app.config import Settings
from app.domain.exceptions import AppError, InsufficientFundsError, NotFoundError
from app.domain.rules.lives import LivesRules
from app.infrastructure.persistence.save_repo import SaveRepository
from app.infrastructure.persistence.transaction_repo import TransactionRepository

logger = structlog.stdlib.get_logger()


class LivesService:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._rules = LivesRules()

    # ------------------------------------------------------------------
    # GET lives
    # ------------------------------------------------------------------
    async def get_lives(self, player_id: UUID, session: AsyncSession) -> LivesResponse:
        repo = SaveRepository(session)
        save = await repo.find_by_player_id(player_id)
        if save is None:
            raise NotFoundError(message="Player save not found")

        save_data: dict[str, Any] = save.save_data
        server_now = int(datetime.now(UTC).timestamp())

        state = self._rules.recalculate(
            current_lives=int(save_data.get("currentLives", self._settings.max_lives)),
            last_restore_ts=int(save_data.get("lastLifeRestoreTimestamp", 0)),
            server_now=server_now,
            config=self._settings,
        )

        # Persist if lives changed
        if state.current_lives != int(save_data.get("currentLives", self._settings.max_lives)):
            updated = copy.deepcopy(save_data)
            updated["currentLives"] = state.current_lives
            updated["lastLifeRestoreTimestamp"] = state.last_restore_timestamp
            save.save_data = updated
            await session.flush()
            await session.commit()

        return LivesResponse(
            current_lives=state.current_lives,
            max_lives=self._settings.max_lives,
            seconds_until_next=state.seconds_until_next,
            restore_cost=self._settings.restore_cost_fragments,
        )

    # ------------------------------------------------------------------
    # POST restore (one life)
    # ------------------------------------------------------------------
    async def restore_one(self, player_id: UUID, session: AsyncSession) -> RestoreLifeResponse:
        save_repo = SaveRepository(session)
        save = await save_repo.find_by_player_id_for_update(player_id)
        if save is None:
            raise NotFoundError(message="Player save not found")

        save_data: dict[str, Any] = copy.deepcopy(save.save_data)
        server_now = int(datetime.now(UTC).timestamp())

        # 1. Recalculate lives
        state = self._rules.recalculate(
            current_lives=int(save_data.get("currentLives", self._settings.max_lives)),
            last_restore_ts=int(save_data.get("lastLifeRestoreTimestamp", 0)),
            server_now=server_now,
            config=self._settings,
        )

        # 2. Check if already full
        if state.current_lives >= self._settings.max_lives:
            raise AppError(
                code="LIVES_ALREADY_FULL",
                message="Lives are already at maximum",
                status_code=400,
            )

        # 3. Check balance
        current_balance = int(save_data.get("totalFragments", 0))
        cost = self._settings.restore_cost_fragments
        if current_balance < cost:
            raise InsufficientFundsError(
                details={"required": cost, "available": current_balance},
            )

        # 4. Apply changes
        new_lives = state.current_lives + 1
        new_balance = current_balance - cost
        save_data["totalFragments"] = new_balance
        save_data["currentLives"] = new_lives
        save_data["lastLifeRestoreTimestamp"] = state.last_restore_timestamp

        # 5. Persist save_data
        save.save_data = save_data
        await session.flush()

        # 6. Record transaction
        tx_repo = TransactionRepository(session)
        await tx_repo.create(
            player_id=player_id,
            type="spend",
            amount=cost,
            reason="restore_life",
            reference_id=None,
            previous_bal=current_balance,
            new_bal=new_balance,
            idempotency_key=None,
        )

        await session.commit()

        return RestoreLifeResponse(
            current_lives=new_lives,
            max_lives=self._settings.max_lives,
            fragments_spent=cost,
            new_balance=new_balance,
        )

    # ------------------------------------------------------------------
    # POST restore-all
    # ------------------------------------------------------------------
    async def restore_all(self, player_id: UUID, session: AsyncSession) -> RestoreAllResponse:
        save_repo = SaveRepository(session)
        save = await save_repo.find_by_player_id_for_update(player_id)
        if save is None:
            raise NotFoundError(message="Player save not found")

        save_data: dict[str, Any] = copy.deepcopy(save.save_data)
        server_now = int(datetime.now(UTC).timestamp())

        # 1. Recalculate lives
        state = self._rules.recalculate(
            current_lives=int(save_data.get("currentLives", self._settings.max_lives)),
            last_restore_ts=int(save_data.get("lastLifeRestoreTimestamp", 0)),
            server_now=server_now,
            config=self._settings,
        )

        # 2. Check if already full
        if state.current_lives >= self._settings.max_lives:
            raise AppError(
                code="LIVES_ALREADY_FULL",
                message="Lives are already at maximum",
                status_code=400,
            )

        # 3. Calculate total cost
        lives_to_restore = self._settings.max_lives - state.current_lives
        cost = self._settings.restore_cost_fragments * lives_to_restore

        # 4. Check balance
        current_balance = int(save_data.get("totalFragments", 0))
        if current_balance < cost:
            raise InsufficientFundsError(
                details={"required": cost, "available": current_balance},
            )

        # 5. Apply changes
        new_balance = current_balance - cost
        save_data["totalFragments"] = new_balance
        save_data["currentLives"] = self._settings.max_lives
        save_data["lastLifeRestoreTimestamp"] = server_now

        # 6. Persist save_data
        save.save_data = save_data
        await session.flush()

        # 7. Record transaction
        tx_repo = TransactionRepository(session)
        await tx_repo.create(
            player_id=player_id,
            type="spend",
            amount=cost,
            reason="restore_life",
            reference_id=None,
            previous_bal=current_balance,
            new_bal=new_balance,
            idempotency_key=None,
        )

        await session.commit()

        return RestoreAllResponse(
            current_lives=self._settings.max_lives,
            max_lives=self._settings.max_lives,
            fragments_spent=cost,
            new_balance=new_balance,
        )
