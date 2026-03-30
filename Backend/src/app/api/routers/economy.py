"""Economy router — GET /balance, POST /transaction."""

from uuid import UUID

from fastapi import APIRouter, Header

from app.api.schemas.common import ApiResponse
from app.api.schemas.economy import BalanceResponse, TransactionRequest, TransactionResponse
from app.dependencies import CurrentPlayerDep, RedisDep, SessionDep, SettingsDep
from app.services.economy_service import EconomyService

router = APIRouter(prefix="/economy", tags=["economy"])


def _get_economy_service(redis: RedisDep, settings: SettingsDep) -> EconomyService:
    return EconomyService(redis, settings)


@router.get("/balance", response_model=ApiResponse[BalanceResponse])
async def get_balance(
    player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
    settings: SettingsDep,
) -> ApiResponse[BalanceResponse]:
    service = _get_economy_service(redis, settings)
    balance = await service.get_balance(player_id, session)
    return ApiResponse(data=BalanceResponse(total_fragments=balance))


@router.post("/transaction", response_model=ApiResponse[TransactionResponse])
async def create_transaction(
    request: TransactionRequest,
    player_id: CurrentPlayerDep,
    session: SessionDep,
    redis: RedisDep,
    settings: SettingsDep,
    idempotency_key: UUID | None = Header(default=None, alias="Idempotency-Key"),
) -> ApiResponse[TransactionResponse]:
    service = _get_economy_service(redis, settings)
    result = await service.execute_transaction(player_id, request, idempotency_key, session)
    return ApiResponse(data=result)
