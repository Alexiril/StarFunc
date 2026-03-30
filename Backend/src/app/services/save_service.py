"""SaveService — cloud save with optimistic locking."""

import time
from typing import Any
from uuid import UUID

from sqlalchemy.ext.asyncio import AsyncSession

from app.api.schemas.common import ErrorDetail
from app.api.schemas.save import SaveConflictResponse, SaveRequest, SaveResponse, SaveUpdateResponse
from app.domain.exceptions import ConflictError
from app.infrastructure.persistence.save_repo import SaveRepository

# Default save data for players with no existing save record
_DEFAULT_SAVE_DATA: dict[str, Any] = {
    "saveVersion": 1,
    "version": 1,
    "lastModified": 0,
    "currentSectorIndex": 0,
    "sectorProgress": {},
    "levelProgress": {},
    "totalFragments": 0,
    "currentLives": 5,
    "lastLifeRestoreTimestamp": 0,
    "ownedItems": [],
    "consumables": {},
    "totalLevelsCompleted": 0,
    "totalStarsCollected": 0,
    "totalPlayTime": 0.0,
}


class SaveService:
    async def get_save(self, player_id: UUID, session: AsyncSession) -> SaveResponse:
        repo = SaveRepository(session)
        save = await repo.find_by_player_id(player_id)

        if save is None:
            return SaveResponse(
                save_data=_DEFAULT_SAVE_DATA.copy(),
                version=1,
                save_version=1,
                updated_at=0,
            )

        return SaveResponse(
            save_data=save.save_data,
            version=save.version,
            save_version=save.save_version,
            updated_at=int(save.updated_at.timestamp()),
        )

    async def put_save(
        self,
        player_id: UUID,
        request: SaveRequest,
        session: AsyncSession,
    ) -> SaveUpdateResponse:
        repo = SaveRepository(session)
        save = await repo.find_by_player_id_for_update(player_id)

        now_ts = int(time.time())

        if save is None:
            # First save — expected_version must be 1
            if request.expected_version != 1:
                raise ConflictError(
                    code="SAVE_CONFLICT",
                    message="Save version conflict",
                    details=_build_conflict_details(
                        _DEFAULT_SAVE_DATA.copy(),
                        version=1,
                        save_version=1,
                        updated_at=0,
                    ),
                )
            new_save = await repo.create(player_id, request.save_data)
            new_save.version = 2
            await session.flush()
            await session.commit()
            return SaveUpdateResponse(version=2, updated_at=int(new_save.updated_at.timestamp()))

        current_version = save.version

        if request.expected_version != current_version:
            raise ConflictError(
                code="SAVE_CONFLICT",
                message="Save version conflict",
                details=_build_conflict_details(
                    save.save_data,
                    version=save.version,
                    save_version=save.save_version,
                    updated_at=int(save.updated_at.timestamp()),
                ),
            )

        new_version = current_version + 1
        await repo.update(save, request.save_data, new_version)
        await session.commit()
        return SaveUpdateResponse(version=new_version, updated_at=int(save.updated_at.timestamp()))


def _build_conflict_details(
    save_data: dict[str, Any],
    *,
    version: int,
    save_version: int,
    updated_at: int,
) -> dict[str, Any]:
    return {
        "serverSave": {
            "saveData": save_data,
            "version": version,
            "saveVersion": save_version,
            "updatedAt": updated_at,
        },
    }
