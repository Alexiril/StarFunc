"""ExceptionHandlerMiddleware — converts exceptions to ApiErrorResponse JSON."""

from __future__ import annotations

import time
from typing import Any

import structlog
from fastapi import Request, Response
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint

from app.domain.exceptions import AppError

logger = structlog.stdlib.get_logger()


class ExceptionHandlerMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        try:
            return await call_next(request)
        except AppError as exc:
            await logger.awarning(
                "app_error",
                code=exc.code,
                message=exc.message,
                status_code=exc.status_code,
                path=request.url.path,
            )
            return _error_response(exc.status_code, exc.code, exc.message, exc.details)
        except RequestValidationError as exc:
            return _error_response(422, "VALIDATION_ERROR", "Request validation failed", {"errors": exc.errors()})
        except Exception:
            await logger.aexception("unhandled_exception", path=request.url.path, method=request.method)
            return _error_response(500, "INTERNAL_ERROR", "Internal server error")


def _error_response(
    status_code: int,
    code: str,
    message: str,
    details: dict[str, Any] | None = None,
) -> JSONResponse:
    error: dict[str, Any] = {"code": code, "message": message}
    if details is not None:
        error["details"] = details
    return JSONResponse(
        status_code=status_code,
        content={"status": "error", "error": error, "serverTime": int(time.time())},
    )
