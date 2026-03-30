"""Save router — GET/PUT /save (authenticated)."""

from fastapi import APIRouter, Header

from app.api.schemas.common import ApiResponse
from app.api.schemas.save import SaveResponse, SaveRequest, SaveUpdateResponse
from app.dependencies import CurrentPlayerDep, SessionDep
from app.services.save_service import SaveService

router = APIRouter(prefix="/save", tags=["save"])


def _get_save_service() -> SaveService:
    return SaveService()


@router.get("", response_model=ApiResponse[SaveResponse])
async def get_save(
    player_id: CurrentPlayerDep,
    session: SessionDep,
) -> ApiResponse[SaveResponse]:
    service = _get_save_service()
    result = await service.get_save(player_id, session)
    return ApiResponse(data=result)


@router.put("", response_model=ApiResponse[SaveUpdateResponse])
async def put_save(
    request: SaveRequest,
    player_id: CurrentPlayerDep,
    session: SessionDep,
    idempotency_key: str = Header(alias="Idempotency-Key"),
) -> ApiResponse[SaveUpdateResponse]:
    service = _get_save_service()
    result = await service.put_save(player_id, request, session)
    return ApiResponse(data=result)
