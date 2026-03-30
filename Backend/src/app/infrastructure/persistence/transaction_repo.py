"""Transaction repository — database access for transactions table."""

from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.infrastructure.persistence.models import TransactionModel


class TransactionRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def create(
        self,
        player_id: UUID,
        type: str,
        amount: int,
        reason: str,
        reference_id: str | None,
        previous_bal: int,
        new_bal: int,
        idempotency_key: UUID | None,
    ) -> TransactionModel:
        tx = TransactionModel(
            player_id=player_id,
            type=type,
            amount=amount,
            reason=reason,
            reference_id=reference_id,
            previous_bal=previous_bal,
            new_bal=new_bal,
            idempotency_key=idempotency_key,
        )
        self._session.add(tx)
        await self._session.flush()
        return tx

    async def find_by_idempotency_key(self, key: UUID) -> TransactionModel | None:
        stmt = select(TransactionModel).where(TransactionModel.idempotency_key == key)
        result = await self._session.execute(stmt)
        return result.scalar_one_or_none()
