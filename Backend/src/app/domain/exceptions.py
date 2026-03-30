"""Domain exceptions — base and specific business errors."""

from typing import Any


class AppError(Exception):
    """Base application error that maps to an API error response."""

    def __init__(
        self,
        code: str,
        message: str,
        status_code: int,
        details: dict[str, Any] | None = None,
    ) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.status_code = status_code
        self.details = details


class NotFoundError(AppError):
    def __init__(self, message: str = "Resource not found", details: dict[str, Any] | None = None) -> None:
        super().__init__(code="NOT_FOUND", message=message, status_code=404, details=details)


class ConflictError(AppError):
    def __init__(
        self,
        code: str = "CONFLICT",
        message: str = "Conflict",
        details: dict[str, Any] | None = None,
    ) -> None:
        super().__init__(code=code, message=message, status_code=409, details=details)


class ForbiddenError(AppError):
    def __init__(self, message: str = "Forbidden", details: dict[str, Any] | None = None) -> None:
        super().__init__(code="FORBIDDEN", message=message, status_code=403, details=details)


class InsufficientFundsError(AppError):
    def __init__(self, message: str = "Not enough fragments", details: dict[str, Any] | None = None) -> None:
        super().__init__(code="INSUFFICIENT_FUNDS", message=message, status_code=422, details=details)


class NoLivesError(AppError):
    def __init__(self, message: str = "No lives remaining", details: dict[str, Any] | None = None) -> None:
        super().__init__(code="NO_LIVES", message=message, status_code=422, details=details)
