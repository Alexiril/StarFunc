from collections.abc import AsyncGenerator
from contextlib import asynccontextmanager

import structlog
from fastapi import FastAPI
from prometheus_fastapi_instrumentator import Instrumentator
from sqlalchemy.ext.asyncio import AsyncEngine, create_async_engine

from app.api.middleware import (
    ClientInfoMiddleware,
    ExceptionHandlerMiddleware,
    IdempotencyMiddleware,
    RateLimitingMiddleware,
    RequestLoggingMiddleware,
    ServerTimeMiddleware,
)
from app.api.routers.auth import router as auth_router
from app.api.routers.content import router as content_router
from app.api.routers.economy import router as economy_router
from app.api.routers.lives import router as lives_router
from app.api.routers.save import router as save_router
from app.config import Settings
from app.infrastructure.redis import close_redis_pool, create_redis_pool

logger = structlog.stdlib.get_logger()


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None]:
    settings = Settings()

    # --- Startup ---
    engine: AsyncEngine = create_async_engine(
        settings.database_url,
        echo=(settings.env == "development"),
        pool_size=10,
        max_overflow=20,
    )
    redis = await create_redis_pool(settings.redis_url)

    app.state.engine = engine
    app.state.redis = redis
    app.state.settings = settings

    await logger.ainfo("startup", env=settings.env)

    yield

    # --- Shutdown ---
    await close_redis_pool(redis)
    await engine.dispose()
    await logger.ainfo("shutdown")


def create_app() -> FastAPI:
    app = FastAPI(
        title="STAR FUNC API",
        version="0.1.0",
        docs_url="/docs",
        redoc_url="/redoc",
        lifespan=lifespan,
    )

    # --- Prometheus metrics ---
    Instrumentator().instrument(app).expose(app, endpoint="/metrics")

    # --- Middleware ---
    # Starlette executes middleware in reverse add-order (last added = outermost).
    # Desired request flow:
    #   Request → Logging → ExceptionHandler → RateLimiting → ClientInfo
    #     → [Auth via Depends] → Idempotency → [Pydantic] → ServerTime → Router
    app.add_middleware(RequestLoggingMiddleware)
    app.add_middleware(ExceptionHandlerMiddleware)
    app.add_middleware(RateLimitingMiddleware)
    app.add_middleware(ClientInfoMiddleware)
    app.add_middleware(IdempotencyMiddleware)
    app.add_middleware(ServerTimeMiddleware)

    # --- Routers ---
    app.include_router(auth_router, prefix="/api/v1")
    app.include_router(save_router, prefix="/api/v1")
    app.include_router(economy_router, prefix="/api/v1")
    app.include_router(lives_router, prefix="/api/v1")
    app.include_router(content_router, prefix="/api/v1")

    return app


app = create_app()
