"""Redis-based sliding-window rate limiter.

Uses ``INCR`` + ``EXPIRE`` on keys of the form ``rate:{identifier}:{endpoint}``.
"""

from __future__ import annotations

from redis.asyncio import Redis

_PREFIX = "rate"


async def check_rate_limit(
    redis: Redis,
    *,
    identifier: str,
    endpoint: str,
    limit: int,
    window: int = 60,
) -> bool:
    """Return ``True`` if the request is within the rate limit.

    Increments the counter for *identifier*/*endpoint* and sets expiry on the
    first hit within the window.  Returns ``False`` when the counter exceeds
    *limit*.
    """
    key = f"{_PREFIX}:{identifier}:{endpoint}"
    current = await redis.incr(key)
    if current == 1:
        await redis.expire(key, window)
    return bool(current <= limit)


async def get_retry_after(redis: Redis, *, identifier: str, endpoint: str) -> int:
    """Return remaining TTL in seconds for the rate-limit window."""
    key = f"{_PREFIX}:{identifier}:{endpoint}"
    ttl = await redis.ttl(key)
    return int(max(ttl, 1))
