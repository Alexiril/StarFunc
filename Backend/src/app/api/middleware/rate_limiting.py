"""RateLimitingMiddleware — per-IP for /auth/*, per-player for everything else."""

from __future__ import annotations

import time

import structlog
from fastapi import Request, Response
from fastapi.responses import JSONResponse
from jose import JWTError  # type: ignore[import-untyped]
from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint

from app.infrastructure.cache.rate_limiter import check_rate_limit, get_retry_after

logger = structlog.stdlib.get_logger()

# Route prefix → settings key mapping
_ROUTE_LIMIT_MAP: dict[str, str] = {
    "/api/v1/auth": "rate_limit_auth",
    "/api/v1/save": "rate_limit_save",
    "/api/v1/economy": "rate_limit_economy",
    "/api/v1/lives": "rate_limit_lives",
    "/api/v1/check": "rate_limit_check",
    "/api/v1/analytics": "rate_limit_analytics",
    "/api/v1/content": "rate_limit_content",
    "/api/v1/shop": "rate_limit_shop",
}


class RateLimitingMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        settings = request.app.state.settings
        redis = request.app.state.redis

        route_group = _resolve_route_group(request.url.path)
        if route_group is None:
            return await call_next(request)

        limit_attr = _ROUTE_LIMIT_MAP.get(route_group)
        if limit_attr is None:
            return await call_next(request)

        limit: int = getattr(settings, limit_attr, 60)
        is_auth = route_group == "/api/v1/auth"

        if is_auth:
            identifier = _get_client_ip(request)
        else:
            player_id = _extract_player_id(request, settings)
            if player_id is None:
                # No player_id available — let the auth dependency handle rejection
                return await call_next(request)
            identifier = player_id

        endpoint_key = route_group.replace("/", "_").strip("_")
        allowed = await check_rate_limit(redis, identifier=identifier, endpoint=endpoint_key, limit=limit)

        if not allowed:
            retry_after = await get_retry_after(redis, identifier=identifier, endpoint=endpoint_key)
            await logger.awarning(
                "rate_limited",
                identifier=identifier,
                endpoint=endpoint_key,
                path=request.url.path,
            )
            return JSONResponse(
                status_code=429,
                content={
                    "status": "error",
                    "error": {"code": "RATE_LIMITED", "message": "Too many requests"},
                    "serverTime": int(time.time()),
                },
                headers={"Retry-After": str(retry_after)},
            )

        return await call_next(request)


def _resolve_route_group(path: str) -> str | None:
    """Find the longest matching route prefix."""
    for prefix in sorted(_ROUTE_LIMIT_MAP, key=len, reverse=True):
        if path.startswith(prefix):
            return prefix
    return None


def _get_client_ip(request: Request) -> str:
    forwarded = request.headers.get("x-forwarded-for")
    if forwarded:
        return forwarded.split(",")[0].strip()
    return request.client.host if request.client else "unknown"


def _extract_player_id(request: Request, settings: object) -> str | None:
    """Try to extract player_id from the Authorization header without raising."""
    auth_header = request.headers.get("authorization")
    if not auth_header or not auth_header.lower().startswith("bearer "):
        return None
    token = auth_header[7:]
    try:
        from jose import jwt

        payload = jwt.decode(
            token,
            getattr(settings, "jwt_secret", ""),
            algorithms=[getattr(settings, "jwt_algorithm", "HS256")],
            audience="starfunc-client",
            issuer="starfunc-api",
        )
        sub = payload.get("sub")
        return str(sub) if sub is not None else None
    except JWTError:
        return None
