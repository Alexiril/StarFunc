"""Unit tests for EconomyRules — S2.2."""

import pytest

from app.config import Settings
from app.domain.rules.economy import EconomyRules


@pytest.fixture
def rules() -> EconomyRules:
    return EconomyRules()


@pytest.fixture
def settings() -> Settings:
    return Settings(  # type: ignore[call-arg]
        database_url="postgresql+asyncpg://test:test@localhost:5432/test",
        jwt_secret="test-secret-key-minimum-256-bits-long-key",
        improvement_bonus_per_star=5,
    )


class TestCalculateLevelReward:
    def test_returns_fragment_reward(self, rules: EconomyRules) -> None:
        level = {"fragmentReward": 10}
        assert rules.calculate_level_reward(level, stars=3) == 10

    def test_zero_stars_returns_zero(self, rules: EconomyRules) -> None:
        level = {"fragmentReward": 10}
        assert rules.calculate_level_reward(level, stars=0) == 0

    def test_missing_fragment_reward_returns_zero(self, rules: EconomyRules) -> None:
        level: dict = {}
        assert rules.calculate_level_reward(level, stars=2) == 0

    def test_one_star_returns_full_reward(self, rules: EconomyRules) -> None:
        level = {"fragmentReward": 25}
        assert rules.calculate_level_reward(level, stars=1) == 25


class TestCalculateImprovementBonus:
    def test_improvement_gives_bonus(self, rules: EconomyRules, settings: Settings) -> None:
        bonus = rules.calculate_improvement_bonus(old_stars=1, new_stars=3, config=settings)
        assert bonus == 10  # (3 - 1) * 5

    def test_no_improvement_gives_zero(self, rules: EconomyRules, settings: Settings) -> None:
        bonus = rules.calculate_improvement_bonus(old_stars=3, new_stars=3, config=settings)
        assert bonus == 0

    def test_worse_result_gives_zero(self, rules: EconomyRules, settings: Settings) -> None:
        bonus = rules.calculate_improvement_bonus(old_stars=3, new_stars=1, config=settings)
        assert bonus == 0

    def test_from_zero_to_two_gives_bonus(self, rules: EconomyRules, settings: Settings) -> None:
        bonus = rules.calculate_improvement_bonus(old_stars=0, new_stars=2, config=settings)
        assert bonus == 10  # (2 - 0) * 5


class TestValidateTransaction:
    def test_earn_always_valid(self, rules: EconomyRules) -> None:
        assert rules.validate_transaction("earn", amount=100, current_balance=0) is True

    def test_spend_with_enough_balance(self, rules: EconomyRules) -> None:
        assert rules.validate_transaction("spend", amount=50, current_balance=100) is True

    def test_spend_exact_balance(self, rules: EconomyRules) -> None:
        assert rules.validate_transaction("spend", amount=100, current_balance=100) is True

    def test_spend_insufficient_balance(self, rules: EconomyRules) -> None:
        assert rules.validate_transaction("spend", amount=150, current_balance=100) is False

    def test_unknown_type_returns_false(self, rules: EconomyRules) -> None:
        assert rules.validate_transaction("refund", amount=10, current_balance=100) is False
