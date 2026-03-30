from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    # Database
    database_url: str
    redis_url: str = "redis://localhost:6379/0"

    # JWT
    jwt_secret: str
    jwt_algorithm: str = "HS256"
    jwt_access_token_expire_minutes: int = 60
    jwt_refresh_token_expire_days: int = 90

    # Game Balance
    max_lives: int = 5
    restore_interval_seconds: int = 1800
    restore_cost_fragments: int = 20
    skip_level_cost_fragments: int = 100
    improvement_bonus_per_star: int = 5
    hint_cost_fragments: int = 10

    # Idempotency
    idempotency_key_expiration_hours: int = 24

    # Server
    env: str = "development"
    log_level: str = "INFO"
    workers: int = 4

    # Rate Limits (req/min)
    rate_limit_auth: int = 10
    rate_limit_save: int = 30
    rate_limit_economy: int = 60
    rate_limit_lives: int = 30
    rate_limit_check: int = 60
    rate_limit_analytics: int = 10
    rate_limit_content: int = 30
    rate_limit_shop: int = 30

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8", "extra": "ignore"}
