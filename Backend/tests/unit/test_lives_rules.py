"""Unit tests for LivesRules — S2.3."""

import pytest

from app.config import Settings
from app.domain.rules.lives import LivesRules, LivesState


@pytest.fixture
def rules() -> LivesRules:
    return LivesRules()


@pytest.fixture
def settings() -> Settings:
    return Settings(  # type: ignore[call-arg]
        database_url="postgresql+asyncpg://test:test@localhost:5432/test",
        jwt_secret="test-secret-key-minimum-256-bits-long-key",
        max_lives=5,
        restore_interval_seconds=1800,
        restore_cost_fragments=20,
    )


class TestRecalculate:
    def test_zero_lives_plus_3600s_restores_two(self, rules: LivesRules, settings: Settings) -> None:
        """0 lives + 3600 s elapsed → 2 lives restored (interval = 1800 s)."""
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=0,
            last_restore_ts=base_ts,
            server_now=base_ts + 3600,
            config=settings,
        )
        assert state.current_lives == 2
        assert state.last_restore_timestamp == base_ts + 3600

    def test_does_not_exceed_max_lives(self, rules: LivesRules, settings: Settings) -> None:
        """Restoring should never go above max_lives."""
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=3,
            last_restore_ts=base_ts,
            server_now=base_ts + 7200,  # 4 intervals elapsed, but only 2 needed
            config=settings,
        )
        assert state.current_lives == settings.max_lives
        assert state.seconds_until_next == 0

    def test_full_lives_returns_zero_countdown(self, rules: LivesRules, settings: Settings) -> None:
        """When lives are already at max, seconds_until_next must be 0."""
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=settings.max_lives,
            last_restore_ts=base_ts,
            server_now=base_ts + 9999,
            config=settings,
        )
        assert state.current_lives == settings.max_lives
        assert state.seconds_until_next == 0
        # last_restore_ts stays untouched when already full
        assert state.last_restore_timestamp == base_ts

    def test_partial_time_correct_countdown(self, rules: LivesRules, settings: Settings) -> None:
        """After 1 restore + 600 s partial → 1200 s until next life."""
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=2,
            last_restore_ts=base_ts,
            server_now=base_ts + 2400,  # 1 full interval + 600 s
            config=settings,
        )
        assert state.current_lives == 3
        assert state.seconds_until_next == 1200  # 1800 - 600

    def test_no_time_elapsed_no_restore(self, rules: LivesRules, settings: Settings) -> None:
        """Zero elapsed time restores nothing."""
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=2,
            last_restore_ts=base_ts,
            server_now=base_ts,
            config=settings,
        )
        assert state.current_lives == 2
        assert state.seconds_until_next == 1800

    def test_exact_interval_restores_one(self, rules: LivesRules, settings: Settings) -> None:
        """Exactly one interval elapsed → 1 life restored, countdown resets."""
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=0,
            last_restore_ts=base_ts,
            server_now=base_ts + 1800,
            config=settings,
        )
        assert state.current_lives == 1
        assert state.seconds_until_next == 1800
        assert state.last_restore_timestamp == base_ts + 1800

    def test_restores_to_exactly_max(self, rules: LivesRules, settings: Settings) -> None:
        """When restoring reaches exactly max, timestamp and countdown update correctly."""
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=4,
            last_restore_ts=base_ts,
            server_now=base_ts + 1800,
            config=settings,
        )
        assert state.current_lives == 5
        assert state.seconds_until_next == 0
        assert state.last_restore_timestamp == base_ts + 1800

    def test_returns_lives_state_dataclass(self, rules: LivesRules, settings: Settings) -> None:
        base_ts = 1_000_000
        state = rules.recalculate(
            current_lives=3,
            last_restore_ts=base_ts,
            server_now=base_ts,
            config=settings,
        )
        assert isinstance(state, LivesState)
