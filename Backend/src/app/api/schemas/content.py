"""Content Pydantic schemas — responses for manifest, sectors, levels, balance."""

from typing import Any

from pydantic import BaseModel, Field


class ContentManifestResponse(BaseModel):
    content_version: int = Field(alias="contentVersion")
    sectors: dict[str, int]
    balance_version: int = Field(alias="balanceVersion")
    shop_version: int = Field(alias="shopVersion")

    model_config = {"populate_by_name": True}


class SectorResponse(BaseModel):
    sector: dict[str, Any]


class SectorsResponse(BaseModel):
    sectors: list[dict[str, Any]]


class LevelResponse(BaseModel):
    level: dict[str, Any]


class LevelsResponse(BaseModel):
    levels: list[dict[str, Any]]


class BalanceConfigResponse(BaseModel):
    config: dict[str, Any]
