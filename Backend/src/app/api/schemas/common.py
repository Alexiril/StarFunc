"""Common Pydantic schemas: ApiResponse, ApiErrorResponse."""

import time
from typing import Any

from pydantic import BaseModel, Field


class ApiResponse[T](BaseModel):
    status: str = "ok"
    data: T
    server_time: int = Field(alias="serverTime", default_factory=lambda: int(time.time()))

    model_config = {"populate_by_name": True}


class ErrorDetail(BaseModel):
    code: str
    message: str
    details: dict[str, Any] | None = None


class ApiErrorResponse(BaseModel):
    status: str = "error"
    error: ErrorDetail
    server_time: int = Field(alias="serverTime", default_factory=lambda: int(time.time()))

    model_config = {"populate_by_name": True}
