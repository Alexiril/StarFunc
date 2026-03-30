"""Content router — GET /content/* endpoints."""

from fastapi import APIRouter

from app.api.schemas.common import ApiResponse
from app.api.schemas.content import (
    BalanceConfigResponse,
    ContentManifestResponse,
    LevelResponse,
    LevelsResponse,
    SectorResponse,
    SectorsResponse,
)
from app.dependencies import CurrentPlayerDep, RedisDep, SessionDep
from app.services.content_service import ContentService

router = APIRouter(prefix="/content", tags=["content"])


def _get_content_service(redis: RedisDep) -> ContentService:
    return ContentService(redis)


@router.get("/manifest", response_model=ApiResponse[ContentManifestResponse])
async def get_manifest(
    _player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
) -> ApiResponse[ContentManifestResponse]:
    service = _get_content_service(redis)
    result = await service.get_manifest(session)
    return ApiResponse(data=result)


@router.get("/sectors", response_model=ApiResponse[SectorsResponse])
async def get_sectors(
    _player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
) -> ApiResponse[SectorsResponse]:
    service = _get_content_service(redis)
    result = await service.get_sectors(session)
    return ApiResponse(data=result)


@router.get("/sectors/{sector_id}", response_model=ApiResponse[SectorResponse])
async def get_sector(
    sector_id: str,
    _player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
) -> ApiResponse[SectorResponse]:
    service = _get_content_service(redis)
    result = await service.get_sector(sector_id, session)
    return ApiResponse(data=result)


@router.get("/sectors/{sector_id}/levels", response_model=ApiResponse[LevelsResponse])
async def get_sector_levels(
    sector_id: str,
    _player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
) -> ApiResponse[LevelsResponse]:
    service = _get_content_service(redis)
    result = await service.get_levels(sector_id, session)
    return ApiResponse(data=result)


@router.get("/levels/{level_id}", response_model=ApiResponse[LevelResponse])
async def get_level(
    level_id: str,
    _player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
) -> ApiResponse[LevelResponse]:
    service = _get_content_service(redis)
    result = await service.get_level(level_id, session)
    return ApiResponse(data=result)


@router.get("/balance", response_model=ApiResponse[BalanceConfigResponse])
async def get_balance_config(
    _player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
) -> ApiResponse[BalanceConfigResponse]:
    service = _get_content_service(redis)
    result = await service.get_balance_config(session)
    return ApiResponse(data=result)
