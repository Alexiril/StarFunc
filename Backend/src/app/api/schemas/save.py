"""Save-related Pydantic schemas."""

from typing import Any

from pydantic import BaseModel, Field

from app.api.schemas.common import ErrorDetail


class SaveResponse(BaseModel):
    save_data: dict[str, Any] = Field(alias="saveData")
    version: int
    save_version: int = Field(alias="saveVersion")
    updated_at: int = Field(alias="updatedAt")

    model_config = {"populate_by_name": True}


class SaveRequest(BaseModel):
    save_data: dict[str, Any] = Field(alias="saveData")
    expected_version: int = Field(alias="expectedVersion")

    model_config = {"populate_by_name": True}


class SaveUpdateResponse(BaseModel):
    version: int
    updated_at: int = Field(alias="updatedAt")

    model_config = {"populate_by_name": True}


class SaveConflictResponse(BaseModel):
    server_save: SaveResponse = Field(alias="serverSave")
    error: ErrorDetail

    model_config = {"populate_by_name": True}
