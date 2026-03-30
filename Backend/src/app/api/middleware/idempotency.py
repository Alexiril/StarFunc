"""IdempotencyMiddleware — deduplicate POST/PUT mutations via Idempotency-Key header."""

from __future__ import annotations

import structlog
from fastapi import Request, Response
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint

from app.infrastructure.cache.idempotency_store import get_response, store_response

logger = structlog.stdlib.get_logger()

_IDEMPOTENT_METHODS = {"POST", "PUT"}


class IdempotencyMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        if request.method not in _IDEMPOTENT_METHODS:
            return await call_next(request)

        idem_key = request.headers.get("idempotency-key")
        if not idem_key:
            return await call_next(request)

        player_id = _get_player_id(request)
        if player_id is None:
            # Without a player_id we can't safely scope the key
            return await call_next(request)

        redis = request.app.state.redis
        settings = request.app.state.settings
        endpoint = request.url.path

        cached = await get_response(redis, player_id=player_id, endpoint=endpoint, key=idem_key)
        if cached is not None:
            await logger.ainfo(
                "idempotency_hit",
                player_id=player_id,
                endpoint=endpoint,
                key=idem_key,
            )
            return JSONResponse(
                status_code=cached.get("status_code", 200),
                content=cached.get("body"),
            )

        response = await call_next(request)

        # Only cache successful responses (2xx)
        if 200 <= response.status_code < 300:
            body = b""
            async for chunk in response.body_iterator:  # type: ignore[attr-defined]
                body += chunk if isinstance(chunk, bytes) else chunk.encode()

            import json

            try:
                body_json = json.loads(body)
            except (json.JSONDecodeError, ValueError):
                body_json = body.decode()

            ttl_hours: int = getattr(settings, "idempotency_key_expiration_hours", 24)
            await store_response(
                redis,
                player_id=player_id,
                endpoint=endpoint,
                key=idem_key,
                response={"status_code": response.status_code, "body": body_json},
                ttl_hours=ttl_hours,
            )

            return Response(
                content=body,
                status_code=response.status_code,
                headers=dict(response.headers),
                media_type=response.media_type,
            )

        return response


def _get_player_id(request: Request) -> str | None:
    """Extract player_id from JWT without raising — for key scoping only."""
    auth_header = request.headers.get("authorization")
    if not auth_header or not auth_header.lower().startswith("bearer "):
        return None
    token = auth_header[7:]
    try:
        from jose import jwt  # type: ignore[import-untyped]

        settings = request.app.state.settings
        payload = jwt.decode(
            token,
            settings.jwt_secret,
            algorithms=[settings.jwt_algorithm],
            audience="starfunc-client",
            issuer="starfunc-api",
        )
        sub = payload.get("sub")
        return str(sub) if sub is not None else None
    except Exception:
        return None
