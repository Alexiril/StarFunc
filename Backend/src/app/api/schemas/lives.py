"""Lives Pydantic schemas: LivesResponse, RestoreLifeResponse, RestoreAllResponse."""

from pydantic import BaseModel, Field


class LivesResponse(BaseModel):
    current_lives: int = Field(alias="currentLives")
    max_lives: int = Field(alias="maxLives")
    seconds_until_next: int = Field(alias="secondsUntilNext")
    restore_cost: int = Field(alias="restoreCost")

    model_config = {"populate_by_name": True}


class RestoreLifeResponse(BaseModel):
    current_lives: int = Field(alias="currentLives")
    max_lives: int = Field(alias="maxLives")
    fragments_spent: int = Field(alias="fragmentsSpent")
    new_balance: int = Field(alias="newBalance")

    model_config = {"populate_by_name": True}


class RestoreAllResponse(BaseModel):
    current_lives: int = Field(alias="currentLives")
    max_lives: int = Field(alias="maxLives")
    fragments_spent: int = Field(alias="fragmentsSpent")
    new_balance: int = Field(alias="newBalance")

    model_config = {"populate_by_name": True}
