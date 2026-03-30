"""Save repository — database access for player_saves table."""

from typing import Any
from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.infrastructure.persistence.models import PlayerSaveModel


class SaveRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def find_by_player_id(self, player_id: UUID) -> PlayerSaveModel | None:
        stmt = select(PlayerSaveModel).where(PlayerSaveModel.player_id == player_id)
        result = await self._session.execute(stmt)
        return result.scalar_one_or_none()

    async def find_by_player_id_for_update(self, player_id: UUID) -> PlayerSaveModel | None:
        stmt = select(PlayerSaveModel).where(PlayerSaveModel.player_id == player_id).with_for_update()
        result = await self._session.execute(stmt)
        return result.scalar_one_or_none()

    async def create(self, player_id: UUID, save_data: dict[str, Any]) -> PlayerSaveModel:
        save = PlayerSaveModel(
            player_id=player_id,
            save_data=save_data,
        )
        self._session.add(save)
        await self._session.flush()
        return save

    async def update(
        self,
        save: PlayerSaveModel,
        save_data: dict[str, Any],
        new_version: int,
    ) -> None:
        save.save_data = save_data
        save.version = new_version
        await self._session.flush()
