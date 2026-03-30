"""Economy Pydantic schemas: BalanceResponse, TransactionRequest, TransactionResponse."""

from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field


class BalanceResponse(BaseModel):
    total_fragments: int = Field(alias="totalFragments")

    model_config = {"populate_by_name": True}


class TransactionRequest(BaseModel):
    type: Literal["earn", "spend"]
    amount: int = Field(gt=0)
    reason: str
    reference_id: str | None = Field(default=None, alias="referenceId")

    model_config = {"populate_by_name": True}


class SkipLevelProgressUpdate(BaseModel):
    level_id: str = Field(alias="levelId")
    stars: int
    unlocked_levels: list[str] = Field(alias="unlockedLevels")
    unlocked_sectors: list[str] = Field(alias="unlockedSectors")

    model_config = {"populate_by_name": True}


class TransactionResponse(BaseModel):
    transaction_id: UUID = Field(alias="transactionId")
    previous_balance: int = Field(alias="previousBalance")
    new_balance: int = Field(alias="newBalance")
    progress_update: SkipLevelProgressUpdate | None = Field(default=None, alias="progressUpdate")

    model_config = {"populate_by_name": True}
