"""Redis connection pool and basic cache helpers."""

from __future__ import annotations

import json
from typing import Any

import structlog
from redis.asyncio import Redis

logger = structlog.stdlib.get_logger()


async def create_redis_pool(redis_url: str) -> Redis:
    """Create and verify a Redis connection pool."""
    redis: Redis = Redis.from_url(redis_url, decode_responses=True)  # pyright: ignore[reportUnknownMemberType]
    await redis.ping()  # type: ignore[misc]
    await logger.ainfo("redis_connected", url=redis_url.split("@")[-1])
    return redis


async def close_redis_pool(redis: Redis) -> None:
    """Gracefully close the Redis connection pool."""
    await redis.aclose()
    await logger.ainfo("redis_disconnected")


async def get_cached(redis: Redis, key: str) -> Any | None:
    """Get a value from Redis, deserializing JSON."""
    raw = await redis.get(key)
    if raw is None:
        return None
    try:
        return json.loads(raw)
    except (json.JSONDecodeError, TypeError):
        return raw


async def set_cached(redis: Redis, key: str, value: Any, ttl: int) -> None:
    """Set a value in Redis as JSON with a TTL in seconds."""
    serialized = json.dumps(value, default=str)
    await redis.set(key, serialized, ex=ttl)


async def delete_cached(redis: Redis, key: str) -> None:
    """Delete a key from Redis."""
    await redis.delete(key)
