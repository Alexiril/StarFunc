"""Economy business rules: rewards, bonuses, transaction validation."""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from app.config import Settings


class EconomyRules:
    def calculate_level_reward(self, level: dict[str, Any], stars: int) -> int:
        """Calculate fragment reward for completing a level.

        Uses the level's ``fragmentReward`` field. Returns 0 when the
        player earns zero stars (failed the level).
        """
        if stars <= 0:
            return 0
        return int(level.get("fragmentReward", 0))

    def calculate_improvement_bonus(self, old_stars: int, new_stars: int, config: Settings) -> int:
        """Bonus fragments when the player improves their star rating.

        Formula: ``(new_stars - old_stars) * improvement_bonus_per_star``.
        Returns 0 if the result did not improve.
        """
        if new_stars <= old_stars:
            return 0
        return (new_stars - old_stars) * config.improvement_bonus_per_star

    def validate_transaction(self, tx_type: str, amount: int, current_balance: int) -> bool:
        """Validate whether a transaction is allowed.

        ``earn`` is always valid.  ``spend`` requires sufficient balance.
        """
        if tx_type == "earn":
            return True
        if tx_type == "spend":
            return current_balance >= amount
        return False
