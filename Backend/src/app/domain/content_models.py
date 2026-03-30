"""Content domain models — dataclasses for levels, sectors, balance, etc."""

from __future__ import annotations

from dataclasses import dataclass, field

from app.domain.enums import FunctionType, LevelType, TaskType


@dataclass(frozen=True, slots=True)
class StarDefinition:
    star_id: str
    coordinate: tuple[float, float]
    initial_state: str
    is_control_point: bool = False
    is_distractor: bool = False
    belongs_to_solution: bool = True
    reveal_after_action: int | None = None


@dataclass(frozen=True, slots=True)
class StarRatingConfig:
    three_star_max_errors: int
    two_star_max_errors: int
    one_star_max_errors: int
    timer_affects_rating: bool = False
    three_star_max_time: float | None = None


@dataclass(frozen=True, slots=True)
class AnswerOption:
    option_id: str
    text: str
    value: str
    is_correct: bool


@dataclass(frozen=True, slots=True)
class GraphVisibilityConfig:
    partial_reveal: bool = False
    initial_visible_segments: int = 0
    reveal_per_correct_action: int = 1


@dataclass(frozen=True, slots=True)
class HintDefinition:
    trigger: str
    hint_text: str
    highlight_position: tuple[float, float] | None = None
    trigger_after_errors: int = 0


@dataclass(frozen=True, slots=True)
class ReferenceFunctionDef:
    function_type: FunctionType
    coefficients: list[float] = field(default_factory=list)
    domain_range: tuple[float, float] = (0.0, 10.0)


@dataclass(frozen=True, slots=True)
class LevelDefinition:
    level_id: str
    level_index: int
    level_type: LevelType
    sector_id: str
    task_type: TaskType
    plane_min: tuple[float, float]
    plane_max: tuple[float, float]
    grid_step: float
    stars: list[StarDefinition] = field(default_factory=list)
    reference_functions: list[ReferenceFunctionDef] = field(default_factory=list)
    answer_options: list[AnswerOption] = field(default_factory=list)
    accuracy_threshold: float = 0.0
    star_rating: StarRatingConfig | None = None
    max_attempts: int = 0
    max_adjustments: int = 0
    use_memory_mode: bool = False
    memory_display_duration: float = 0.0
    graph_visibility: GraphVisibilityConfig | None = None
    hints: list[HintDefinition] = field(default_factory=list)
    fragment_reward: int = 0


@dataclass(frozen=True, slots=True)
class SectorDefinition:
    sector_id: str
    display_name: str
    sector_index: int
    levels: list[str] = field(default_factory=list)
    previous_sector: str | None = None
    required_stars_to_unlock: int = 0


@dataclass(frozen=True, slots=True)
class BalanceConfig:
    max_lives: int
    restore_interval_seconds: int
    restore_cost_fragments: int
    skip_level_cost_fragments: int
    improvement_bonus_per_star: int
    hint_cost_fragments: int


@dataclass(frozen=True, slots=True)
class ContentManifest:
    content_version: int
    sectors: dict[str, int] = field(default_factory=dict)
    balance_version: int = 1
    shop_version: int = 1
