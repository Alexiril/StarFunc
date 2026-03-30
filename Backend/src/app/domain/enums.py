"""Domain enums: TaskType, FunctionType, SectorState, LevelType, etc."""

from enum import StrEnum


class Platform(StrEnum):
    ANDROID = "android"
    IOS = "ios"


class SectorState(StrEnum):
    LOCKED = "Locked"
    AVAILABLE = "Available"
    IN_PROGRESS = "InProgress"
    COMPLETED = "Completed"


class LevelType(StrEnum):
    TUTORIAL = "Tutorial"
    NORMAL = "Normal"
    BONUS = "Bonus"
    CONTROL = "Control"
    FINAL = "Final"


class TaskType(StrEnum):
    CHOOSE_COORDINATE = "ChooseCoordinate"
    CHOOSE_FUNCTION = "ChooseFunction"
    ADJUST_GRAPH = "AdjustGraph"
    BUILD_FUNCTION = "BuildFunction"
    IDENTIFY_ERROR = "IdentifyError"
    RESTORE_CONSTELLATION = "RestoreConstellation"


class FunctionType(StrEnum):
    LINEAR = "Linear"
    QUADRATIC = "Quadratic"
    SINUSOIDAL = "Sinusoidal"
    MIXED = "Mixed"


class TransactionType(StrEnum):
    EARN = "earn"
    SPEND = "spend"
