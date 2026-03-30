from collections.abc import AsyncGenerator
from functools import lru_cache
from typing import Annotated
from uuid import UUID

from fastapi import Depends, HTTPException, Request, status
from fastapi.security import OAuth2PasswordBearer
from jose import JWTError  # type: ignore[import-untyped]
from redis.asyncio import Redis
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from app.config import Settings
from app.infrastructure.auth.jwt_provider import JwtProvider

oauth2_scheme = OAuth2PasswordBearer(tokenUrl="/api/v1/auth/register", auto_error=False)


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings()


async def get_session(request: Request) -> AsyncGenerator[AsyncSession]:
    engine = request.app.state.engine
    session_factory = async_sessionmaker(engine, expire_on_commit=False)
    async with session_factory() as session:
        yield session


async def get_redis(request: Request) -> Redis:
    return request.app.state.redis  # type: ignore[no-any-return]


def get_jwt_provider(settings: "SettingsDep") -> JwtProvider:
    return JwtProvider(settings)


async def get_current_player(
    token: str | None = Depends(oauth2_scheme),
    jwt_provider: JwtProvider = Depends(get_jwt_provider),  # noqa: B008
) -> UUID:
    if token is None:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={"code": "INVALID_TOKEN", "message": "Missing authentication token"},
            headers={"WWW-Authenticate": "Bearer"},
        )
    try:
        payload = jwt_provider.decode(token)
    except JWTError:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={"code": "INVALID_TOKEN", "message": "Invalid or expired token"},
            headers={"WWW-Authenticate": "Bearer"},
        ) from None
    sub = payload.get("sub")
    if sub is None:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={"code": "INVALID_TOKEN", "message": "Token missing subject"},
            headers={"WWW-Authenticate": "Bearer"},
        )
    return UUID(sub)


# Type aliases for Depends injection
SettingsDep = Annotated[Settings, Depends(get_settings)]
SessionDep = Annotated[AsyncSession, Depends(get_session)]
RedisDep = Annotated[Redis, Depends(get_redis)]
JwtProviderDep = Annotated[JwtProvider, Depends(get_jwt_provider)]
CurrentPlayerDep = Annotated[UUID, Depends(get_current_player)]
