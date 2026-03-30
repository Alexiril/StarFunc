"""Domain models: PlayerSaveData, SectorProgress, LevelProgress, etc."""

from dataclasses import dataclass, field

from app.domain.enums import SectorState


@dataclass
class SectorProgress:
    state: SectorState = SectorState.LOCKED
    stars_collected: int = 0
    control_level_passed: bool = False


@dataclass
class LevelProgress:
    is_completed: bool = False
    best_stars: int = 0
    best_time: float = 0.0
    attempts: int = 0


@dataclass
class PlayerSaveData:
    save_version: int = 1
    version: int = 1
    last_modified: int = 0
    current_sector_index: int = 0
    sector_progress: dict[str, SectorProgress] = field(default_factory=dict)
    level_progress: dict[str, LevelProgress] = field(default_factory=dict)
    total_fragments: int = 0
    current_lives: int = 5
    last_life_restore_timestamp: int = 0
    owned_items: list[str] = field(default_factory=list)
    consumables: dict[str, int] = field(default_factory=dict)
    total_levels_completed: int = 0
    total_stars_collected: int = 0
    total_play_time: float = 0.0
