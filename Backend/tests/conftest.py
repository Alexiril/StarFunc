import asyncio
from collections.abc import AsyncGenerator

import pytest

from app.config import Settings


@pytest.fixture(scope="session")
def event_loop():
    """Create a session-scoped event loop for async tests."""
    loop = asyncio.new_event_loop()
    yield loop
    loop.close()


@pytest.fixture
async def settings() -> AsyncGenerator[Settings, None]:
    """Provide test settings."""
    from app.config import Settings

    s = Settings(  # type: ignore[call-arg]
        database_url="postgresql+asyncpg://test:test@localhost:5432/test",
        jwt_secret="test-secret-key-minimum-256-bits-long-key",
    )
    yield s
