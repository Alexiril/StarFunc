"""Lives business rules: server-side timer recalculation."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from app.config import Settings


@dataclass(frozen=True, slots=True)
class LivesState:
    current_lives: int
    seconds_until_next: int
    last_restore_timestamp: int


class LivesRules:
    def recalculate(
        self,
        current_lives: int,
        last_restore_ts: int,
        server_now: int,
        config: Settings,
    ) -> LivesState:
        """Recalculate lives based on elapsed time since the last restore.

        Restores one life per ``restore_interval_seconds`` until
        ``max_lives`` is reached.  Returns the updated state including
        the countdown to the next life.
        """
        max_lives = config.max_lives
        restore_interval = config.restore_interval_seconds

        if current_lives >= max_lives:
            return LivesState(max_lives, 0, last_restore_ts)

        elapsed = server_now - last_restore_ts
        restored = elapsed // restore_interval
        new_lives = min(current_lives + restored, max_lives)
        new_last_restore_ts = last_restore_ts + restored * restore_interval

        if new_lives >= max_lives:
            new_last_restore_ts = server_now
            seconds_until_next = 0
        else:
            seconds_until_next = restore_interval - (elapsed % restore_interval)

        return LivesState(new_lives, seconds_until_next, new_last_restore_ts)
