"""Lives router — GET /, POST /restore, POST /restore-all."""

from uuid import UUID

from fastapi import APIRouter, Header

from app.api.schemas.common import ApiResponse
from app.api.schemas.lives import LivesResponse, RestoreAllResponse, RestoreLifeResponse
from app.dependencies import CurrentPlayerDep, SessionDep, SettingsDep
from app.services.lives_service import LivesService

router = APIRouter(prefix="/lives", tags=["lives"])


@router.get("", response_model=ApiResponse[LivesResponse])
async def get_lives(
    player_id: CurrentPlayerDep,
    session: SessionDep,
    settings: SettingsDep,
) -> ApiResponse[LivesResponse]:
    service = LivesService(settings)
    result = await service.get_lives(player_id, session)
    return ApiResponse(data=result)


@router.post("/restore", response_model=ApiResponse[RestoreLifeResponse])
async def restore_one(
    player_id: CurrentPlayerDep,
    session: SessionDep,
    settings: SettingsDep,
    idempotency_key: UUID | None = Header(default=None, alias="Idempotency-Key"),
) -> ApiResponse[RestoreLifeResponse]:
    service = LivesService(settings)
    result = await service.restore_one(player_id, session)
    return ApiResponse(data=result)


@router.post("/restore-all", response_model=ApiResponse[RestoreAllResponse])
async def restore_all(
    player_id: CurrentPlayerDep,
    session: SessionDep,
    settings: SettingsDep,
    idempotency_key: UUID | None = Header(default=None, alias="Idempotency-Key"),
) -> ApiResponse[RestoreAllResponse]:
    service = LivesService(settings)
    result = await service.restore_all(player_id, session)
    return ApiResponse(data=result)
