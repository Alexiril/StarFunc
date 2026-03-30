# План разработки STAR FUNC — Серверные задачи

> Порядок задач выстроен по принципу зависимостей: каждая следующая задача опирается на результаты предыдущих.
> Фазы соответствуют разделу «Оценка задач» из [Server architecture.md](Server%20architecture.md), но детализированы до уровня отдельных файлов и критериев завершения.
> Спецификация API — в [API.md](API.md). Клиентские задачи — в [Tasks.md](Tasks.md).
> Все пути указаны относительно `src/app/`.

---

## Сводка фаз

| Фаза | Название                  | Задач | Что на выходе                                                                  |
| ---- | ------------------------- | ----- | ------------------------------------------------------------------------------ |
| S0   | Инфраструктура            | 4     | Скаффолдинг проекта, БД + миграции, Redis, CI/CD                               |
| S1   | Аутентификация            | 4     | JWT, регистрация, refresh, middleware pipeline, привязка аккаунтов             |
| S2   | Основные сервисы          | 5     | Сохранения, экономика, жизни, контент, seed data                               |
| S3   | Ключевая бизнес-логика    | 5     | Валидация ответов, звёзды, прогрессия, LevelCheckService, мерж сохранений      |
| S4   | Магазин, аналитика, тесты | 5     | Магазин, аналитика, health/метрики, интеграционные тесты, OpenAPI-документация |

---

## ~~Фаза S0 — Инфраструктура~~ (Done)

### ~~Задача S0.1 — Скаффолдинг проекта~~ (Done)

**Суть:** создать структуру серверного проекта, настроить зависимости, Docker-окружение и файлы конфигурации.

**Что сделать:**

- Создать корневую директорию `Backend/` (отдельный репозиторий или подпапка — решить):

  ```txt
  Backend/
  ├── src/app/
  │   ├── __init__.py
  │   ├── main.py
  │   ├── config.py
  │   ├── dependencies.py
  │   ├── domain/
  │   │   ├── __init__.py
  │   │   ├── entities.py
  │   │   ├── enums.py
  │   │   ├── models.py
  │   │   ├── content_models.py
  │   │   ├── shop_models.py
  │   │   ├── check_models.py
  │   │   └── rules/__init__.py
  │   ├── services/__init__.py
  │   ├── api/
  │   │   ├── __init__.py
  │   │   ├── routers/__init__.py
  │   │   ├── middleware/__init__.py
  │   │   └── schemas/__init__.py
  │   └── infrastructure/
  │       ├── __init__.py
  │       ├── persistence/__init__.py
  │       ├── cache/__init__.py
  │       └── auth/__init__.py
  ├── alembic/
  ├── seed/data/
  ├── tests/
  │   ├── conftest.py
  │   ├── unit/
  │   ├── service/
  │   └── integration/
  ├── pyproject.toml
  ├── Dockerfile
  ├── docker-compose.yml
  ├── docker-compose.override.yml
  ├── .env.example
  └── README.md
  ```

- `pyproject.toml` — зависимости (см. Server architecture.md Приложение C):
  - Runtime: `fastapi>=0.110`, `uvicorn[standard]>=0.29`, `gunicorn>=22.0`, `sqlalchemy[asyncio]>=2.0`, `asyncpg>=0.29`, `alembic>=1.13`, `redis[hiredis]>=5.0`, `python-jose[cryptography]>=3.3`, `passlib[bcrypt]>=1.7`, `pydantic>=2.7`, `pydantic-settings>=2.2`, `structlog>=24.1`, `prometheus-fastapi-instrumentator>=7.0`, `prometheus-client>=0.20`, `httpx>=0.27`
  - Dev: `pytest>=8.1`, `pytest-asyncio>=0.23`, `testcontainers[postgres,redis]>=4.4`, `factory-boy>=3.3`, `ruff>=0.4`, `mypy>=1.10`
  - Ruff: `target-version = "py312"`, `line-length = 120`, select `["E", "F", "I", "N", "UP", "B", "SIM", "RUF"]`
  - mypy: `strict = true`, `plugins = ["pydantic.mypy"]`
  - pytest: `asyncio_mode = "auto"`, `testpaths = ["tests"]`

- `config.py` — `Pydantic Settings`:
  - `database_url`, `redis_url`
  - JWT: `jwt_secret`, `jwt_algorithm`, `jwt_access_token_expire_minutes`, `jwt_refresh_token_expire_days`
  - Game Balance: `max_lives`, `restore_interval_seconds`, `restore_cost_fragments`, `skip_level_cost_fragments`, `improvement_bonus_per_star`, `hint_cost_fragments`
  - Infra: `idempotency_key_expiration_hours`, `env`, `log_level`, `workers`
  - Rate Limits: `rate_limit_auth=10`, `rate_limit_save=30`, `rate_limit_economy=60`, `rate_limit_lives=30`, `rate_limit_check=60`, `rate_limit_analytics=10`, `rate_limit_content=30`, `rate_limit_shop=30`

- `.env.example` — шаблон переменных окружения (см. Приложение A)

- `main.py` — FastAPI app factory:
  - `lifespan` async context manager: инициализация DB engine, Redis pool при старте; cleanup при остановке
  - Подключение middleware (заглушки — реализация в S1.3)
  - Подключение роутеров (заглушки — реализация в S1-S4)
  - `prometheus-fastapi-instrumentator` setup

- `dependencies.py` — FastAPI `Depends()`:
  - `get_session()` → `AsyncSession` (yield-зависимость)
  - `get_redis()` → `Redis` client
  - `get_settings()` → `Settings` (кешируется через `lru_cache`)
  - `get_current_player()` — заглушка (реализация в S1.2)

- `Dockerfile` — multi-stage build на `python:3.12-slim`, uv для зависимостей, Gunicorn + Uvicorn workers
- `docker-compose.yml` — services: `api`, `db` (postgres:16-alpine), `redis` (redis:7-alpine), healthchecks
- `docker-compose.override.yml` — dev overrides: volume mounts, debug ports, отключение Nginx

**Зависимости:** нет.

**Критерий завершения:** `docker compose up` поднимает 3 контейнера; `GET http://localhost:8000/docs` возвращает пустой Swagger UI; `ruff check` и `mypy` проходят без ошибок.

---

### ~~Задача S0.2 — Настройка БД и миграции~~ (Done)

**Суть:** создать SQLAlchemy ORM-модели, инициализировать Alembic, написать начальные миграции для всех таблиц.

**Файлы:**

- `infrastructure/database.py` — async engine factory и session factory:
  - `create_engine()` — `create_async_engine(database_url, pool_size=10, max_overflow=20)`
  - `create_session_factory()` — `async_sessionmaker(engine, expire_on_commit=False)`

- `infrastructure/persistence/models.py` — SQLAlchemy ORM-модели:
  - `PlayerModel` — таблица `players`: `id (UUID PK)`, `device_id (UUID UNIQUE)`, `platform (VARCHAR(10), CHECK)`, `client_version (VARCHAR(20))`, `google_play_id (VARCHAR(255) UNIQUE, nullable)`, `apple_gc_id (VARCHAR(255) UNIQUE, nullable)`, `display_name (VARCHAR(100), nullable)`, `created_at`, `updated_at`
  - `PlayerSaveModel` — таблица `player_saves`: `id (BIGSERIAL PK)`, `player_id (UUID FK → players, UNIQUE)`, `version (INT DEFAULT 1)`, `save_version (INT DEFAULT 1)`, `save_data (JSONB)`, `created_at`, `updated_at`
  - `TransactionModel` — таблица `transactions`: `id (UUID PK)`, `player_id (UUID FK)`, `type (VARCHAR(10) CHECK ('earn','spend'))`, `amount (INT CHECK > 0)`, `reason (VARCHAR(50))`, `reference_id (VARCHAR(100), nullable)`, `previous_bal (INT)`, `new_bal (INT)`, `idempotency_key (UUID, nullable)`, `created_at`
  - `RefreshTokenModel` — таблица `refresh_tokens`: `id (UUID PK)`, `player_id (UUID FK)`, `token_hash (VARCHAR(128) UNIQUE)`, `expires_at (TIMESTAMPTZ)`, `is_revoked (BOOLEAN DEFAULT FALSE)`, `created_at`, `replaced_by_id (UUID FK self, nullable)`
  - `ContentVersionModel` — таблица `content_versions`: `id (SERIAL PK)`, `content_type (VARCHAR(50))`, `content_id (VARCHAR(100), nullable)`, `version (INT DEFAULT 1)`, `data (JSONB)`, `is_active (BOOLEAN DEFAULT TRUE)`, `created_at`, `updated_at`
  - `AnalyticsEventModel` — таблица `analytics_events`: `id (BIGSERIAL PK)`, `player_id (UUID)`, `session_id (UUID, nullable)`, `event_name (VARCHAR(50))`, `params (JSONB, nullable)`, `client_ts (BIGINT)`, `server_ts (TIMESTAMPTZ DEFAULT NOW())`, `created_at`

- Alembic:
  - `alembic init alembic` — инициализация
  - `alembic/env.py` — async конфигурация с SQLAlchemy metadata
  - Начальная миграция: создание всех 6 таблиц + все индексы из Server architecture.md §5.1:
    - `idx_players_device_id`, `idx_player_saves_player`, `idx_saves_fragments`, `idx_saves_lives`
    - `idx_transactions_player`, `idx_transactions_idempotency` (partial, WHERE idempotency_key IS NOT NULL)
    - `idx_refresh_tokens_player`, `idx_refresh_tokens_hash`
    - `idx_content_type` (partial, WHERE is_active = TRUE)
    - `idx_analytics_player`, `idx_analytics_event`

**Зависимости:** S0.1.

**Критерий завершения:** `alembic upgrade head` создаёт все 6 таблиц с корректными индексами и ограничениями; `alembic downgrade base` откатывает; модели маппятся на таблицы без ошибок.

---

### ~~Задача S0.3 — Redis интеграция~~ (Done)

**Суть:** настроить подключение к Redis, реализовать базовые helper-функции для кеширования, rate limiting и idempotency.

**Файлы:**

- `infrastructure/redis.py` — подключение и pool:
  - `create_redis_pool(redis_url)` → `redis.asyncio.Redis`
  - `close_redis_pool(redis)` — graceful shutdown
  - Базовые helpers: `get_cached(key)`, `set_cached(key, value, ttl)`, `delete_cached(key)`

- `infrastructure/cache/idempotency_store.py` — хранение Idempotency-Key:
  - `get_response(key: str) -> dict | None` — `GET idempotency:{key}`
  - `store_response(key: str, response: dict, ttl_hours: int = 24)` — `SET idempotency:{key}` с TTL
  - Ключ привязывается к `{player_id}:{endpoint}:{key}` для безопасности

- `infrastructure/cache/rate_limiter.py` — rate limiting:
  - `check_rate_limit(identifier: str, endpoint: str, limit: int, window: int = 60) -> bool`
  - Реализация через `INCR rate:{identifier}:{endpoint}` + `EXPIRE {window}`
  - Возвращает `True` если лимит не превышен

- `infrastructure/cache/token_store.py` — заглушка для хранения данных о токенах (используется в S1.1)

- `infrastructure/cache/content_cache.py` — кеш контента:
  - `get_content(content_type: str, content_id: str = None) -> dict | None`
  - `set_content(content_type: str, data: dict, content_id: str = None, ttl: int = 600)`
  - `invalidate_content(content_type: str, content_id: str = None)`
  - Ключи: `content:manifest` (5 мин), `content:sector:{id}` (10 мин), `content:levels:{id}` (10 мин), `content:balance` (10 мин), `content:shop` (10 мин)
  - `player:balance:{playerId}` (5 мин) — инвалидируется при транзакциях

**Зависимости:** S0.1.

**Критерий завершения:** Redis-пул создаётся при старте приложения; `set_cached`/`get_cached` работают; rate limiter корректно считает запросы и блокирует при превышении; idempotency store сохраняет и отдаёт ответы.

---

### ~~Задача S0.4 — CI/CD пайплайн~~ (Done)

**Суть:** настроить GitHub Actions для автоматического линтинга, проверки типов, тестирования и сборки Docker-образа.

**Что сделать:**

- `.github/workflows/ci.yml`:
  1. **Setup:** Python 3.12, установка uv
  2. **Install:** `uv sync --frozen`
  3. **Lint:** `ruff check src/ tests/` + `ruff format --check src/ tests/`
  4. **Type check:** `mypy src/`
  5. **Unit tests:** `pytest tests/unit -v`
  6. **Integration tests:** `pytest tests/integration -v` (через testcontainers — Postgres + Redis в Docker)
  7. **Build:** `docker build -t starfunc-server .`
  8. **Push:** push image в container registry (GitHub Container Registry)

- `.github/workflows/deploy.yml`:
  - Триггер: push в `main` после прохождения CI
  - Deploy to staging (автоматически)
  - Deploy to production (manual approval)

- Базовый unit-тест в `tests/unit/test_placeholder.py` — проверка, что pipeline работает

**Зависимости:** S0.1.

**Критерий завершения:** push в репозиторий запускает workflow; lint, mypy, тесты проходят; Docker-образ собирается.

---

## Фаза S1 — Аутентификация и базовые сервисы

### ~~Задача S1.1 — JwtProvider~~ (Done)

**Суть:** реализовать генерацию и валидацию JWT-токенов (access + refresh).

**Файлы:**

- `infrastructure/auth/jwt_provider.py`:
  - `JwtProvider(settings: Settings)` — инжектится через `Depends`
  - `create_access_token(player_id: UUID, platform: str) -> str`:
    - Payload: `sub` (player_id), `platform`, `iat`, `exp` (TTL из settings: 1 час), `iss: "starfunc-api"`, `aud: "starfunc-client"`
    - Алгоритм: HS256 (configurable через settings)
    - Библиотека: `python-jose`
  - `create_refresh_token(player_id: UUID) -> str`:
    - JWT с `sub`, `iat`, `exp` (TTL: 90 дней из settings), `type: "refresh"`
  - `decode(token: str) -> dict`:
    - Валидация `audience`, `issuer`, `exp`
    - Выбрасывает `jose.JWTError` при невалидном токене
  - `hash_token(token: str) -> str`:
    - SHA-256 хеш refresh-токена (оригинал серверу не сохраняется)

- `dependencies.py` — добавить `get_jwt_provider() -> JwtProvider`

**Тесты:**

- `tests/unit/test_jwt_provider.py`:
  - Создание и декодирование access-токена
  - Создание и декодирование refresh-токена
  - Отклонение истёкшего токена
  - Отклонение токена с неверной подписью
  - Проверка claims (iss, aud, sub)
  - Хеширование токена даёт стабильный результат

**Зависимости:** S0.1.

**Критерий завершения:** access-токен создаётся и декодируется; refresh-токен создаётся с TTL 90 дней; невалидные токены отклоняются; все unit-тесты проходят.

---

### ~~Задача S1.2 — AuthService + auth router~~ (Done)

**Суть:** реализовать сервис авторизации и роутер для анонимной регистрации и обновления токенов.

**Файлы:**

- `domain/enums.py` — добавить `Platform` enum: `android`, `ios`

- `api/schemas/common.py` — базовые Pydantic-схемы:
  - `ApiResponse[T]`: `status: str`, `data: T`, `serverTime: int` (Unix timestamp)
  - `ApiErrorResponse`: `status: "error"`, `error: ErrorDetail` (`code: str`, `message: str`, `details: dict | None`)

- `api/schemas/auth.py`:
  - `RegisterRequest`: `device_id: UUID`, `platform: str`, `client_version: str`
  - `RefreshRequest`: `refresh_token: str`
  - `AuthResponse`: `player_id: UUID`, `access_token: str`, `refresh_token: str`, `expires_in: int`, `is_new_player: bool`

- `infrastructure/persistence/player_repo.py` — `PlayerRepository`:
  - `find_by_device_id(device_id: UUID) -> PlayerModel | None`
  - `create(device_id: UUID, platform: str, client_version: str) -> PlayerModel`
  - `find_by_id(player_id: UUID) -> PlayerModel | None`

- `services/auth_service.py` — `AuthService`:
  - `register(request: RegisterRequest, session: AsyncSession) -> AuthResponse`:
    1. Поиск по `device_id` в БД
    2. Если найден — выдать новые токены для существующего игрока (`is_new_player=False`)
    3. Если нет — создать `Player` + начальный `PlayerSave` с дефолтным `PlayerSaveData` + сгенерировать токены (`is_new_player=True`)
    4. Сохранить refresh-токен (SHA-256 хеш) в `refresh_tokens`
    5. Вернуть `AuthResponse`
  - `refresh(request: RefreshRequest, session: AsyncSession) -> AuthResponse`:
    1. Хешировать полученный refresh-токен
    2. Найти запись в `refresh_tokens` по хешу
    3. Проверить: `is_revoked == False`, `expires_at > now`
    4. Отозвать текущий токен (`is_revoked = True`)
    5. Сгенерировать новую пару токенов
    6. Сохранить новый refresh-токен с `replaced_by_id` → старый
    7. **Детекция кражи:** если токен уже отозван → отозвать **всю цепочку** для этого `player_id` (forced re-auth), вернуть `401`
    8. Вернуть `AuthResponse`

- `api/routers/auth.py`:
  - `POST /register` → `AuthService.register()`, ответ в `ApiResponse[AuthResponse]`
  - `POST /refresh` → `AuthService.refresh()`, ответ в `ApiResponse[AuthResponse]`
  - Обработка ошибок: `401 INVALID_TOKEN`, `401 TOKEN_EXPIRED`

- `dependencies.py` — реализовать `get_current_player()`:
  - Извлечь `Authorization: Bearer <token>` через `OAuth2PasswordBearer`
  - Декодировать через `JwtProvider.decode()`
  - Вернуть `player_id: UUID`
  - При ошибке — `401 Unauthorized`

**Тесты:**

- `tests/unit/test_auth_service.py`:
  - Регистрация нового игрока
  - Повторная регистрация существующего (идемпотентность по `device_id`)
  - Успешный refresh
  - Refresh с отозванным токеном → отзыв цепочки
  - Refresh с истёкшим токеном → ошибка

**Зависимости:** S0.2, S1.1.

**Критерий завершения:** `POST /register` создаёт игрока и возвращает токены; повторный вызов с тем же `device_id` возвращает существующего; `POST /refresh` ротирует токены; детекция кражи работает; все тесты проходят.

---

### ~~Задача S1.3 — Middleware pipeline~~ (Done)

**Суть:** реализовать цепочку middleware для обработки запросов: логирование, обработка исключений, rate limiting, idempotency, server time.

**Файлы:**

- `api/middleware/exception_handler.py` — `ExceptionHandlerMiddleware`:
  - Глобальный `try-except` → конвертация исключений в `ApiErrorResponse`
  - Маппинг: `AppError` → JSON с кодом и деталями, `ValidationError` → `422`, необработанные → `500` с логированием
  - Определить базовые исключения в `domain/exceptions.py`:
    - `AppError(code: str, message: str, status_code: int, details: dict = None)`
    - `NotFoundError(AppError)`, `ConflictError(AppError)`, `ForbiddenError(AppError)`, `InsufficientFundsError(AppError)`, `NoLivesError(AppError)`

- `api/middleware/request_logging.py` — `RequestLoggingMiddleware`:
  - Логирование входящих запросов: метод, путь, player_id (если есть), X-Client-Version
  - Логирование ответов: статус-код, время обработки
  - Формат: structured JSON через `structlog`

- `api/middleware/rate_limiting.py` — `RateLimitingMiddleware`:
  - Проверка лимитов из конфига (Server architecture.md §16.3):
    - `/auth/*` — по IP (10 req/min)
    - Остальные — по `player_id` (из JWT) с лимитом по эндпойнту
  - При превышении — `429 Too Many Requests` с заголовком `Retry-After`
  - Реализация через `cache/rate_limiter.py`

- `api/middleware/idempotency.py` — `IdempotencyMiddleware`:
  - Только для `POST` / `PUT` запросов
  - Извлечение `Idempotency-Key` из заголовка
  - Если ключ найден в Redis → вернуть сохранённый ответ
  - Если нет → выполнить запрос, сохранить ответ в Redis (TTL 24h)
  - Ключ привязан к `player_id + endpoint` для безопасности

- `api/middleware/client_info.py` — `ClientInfoMiddleware`:
  - Извлечение `X-Client-Version` и `X-Platform` из заголовков
  - Сохранение в `request.state` для использования в сервисах

- `api/middleware/server_time.py` — `ServerTimeMiddleware`:
  - Добавление `serverTime: int` (Unix timestamp) в каждый JSON-ответ
  - Реализация через пост-обработку response body

- `main.py` — подключить middleware в правильном порядке:

  ```txt
  Request → Logging → ExceptionHandler → RateLimiting → ClientInfo → Auth (Depends) → Idempotency → Pydantic → ServerTime → Router
  ```

**Зависимости:** S1.2 (нужна аутентификация для player_id в rate limiting).

**Критерий завершения:** ошибки конвертируются в `ApiErrorResponse`; запросы логируются; rate limiting блокирует при превышении лимита; повторный запрос с тем же `Idempotency-Key` возвращает сохранённый ответ; `serverTime` присутствует в каждом ответе.

---

### ~~Задача S1.4 — Привязка сторонних аккаунтов~~ (Done)

**Суть:** реализовать привязку Google Play Games и Apple Game Center аккаунтов к существующему игроку.

**Файлы:**

- `api/schemas/auth.py` — добавить:
  - `LinkAccountRequest`: `provider: str` (`"google_play"` | `"apple_game_center"`), `provider_token: str`
  - `LinkResponse`: `provider: str`, `provider_id: str`, `linked: bool`

- `infrastructure/auth/google_verifier.py` — `GooglePlayVerifier`:
  - `verify(provider_token: str) -> str` (возвращает `google_play_id`)
  - Вызов Google Play Games API для верификации токена
  - При невалидном токене — выбрасывает ошибку

- `infrastructure/auth/apple_verifier.py` — `AppleGameCenterVerifier`:
  - `verify(provider_token: str) -> str` (возвращает `apple_gc_id`)
  - Верификация подписи через Apple Verification API

- `services/auth_service.py` — добавить метод:
  - `link_account(player_id: UUID, request: LinkAccountRequest, session: AsyncSession) -> LinkResponse`:
    1. Верифицировать токен через соответствующий verifier
    2. Проверить уникальность: если `provider_id` уже привязан к другому `player_id` → `409 ACCOUNT_ALREADY_LINKED`
    3. Записать `google_play_id` или `apple_gc_id` в `players`
    4. Вернуть `LinkResponse`

- `api/routers/auth.py` — добавить:
  - `POST /link` (authenticated) → `AuthService.link_account()`

**Зависимости:** S1.2.

**Критерий завершения:** привязка аккаунта записывает `provider_id` в таблицу `players`; повторная привязка того же провайдера обновляет ID; попытка привязать аккаунт, уже занятый другим игроком → `409`.

---

## Фаза S2 — Основные сервисы

### ~~Задача S2.1 — SaveService + save router~~ (Done)

**Суть:** реализовать сервис облачных сохранений с оптимистичной блокировкой и роутер для получения/обновления сохранений.

**Файлы:**

- `domain/models.py` — доменные модели (dataclasses):
  - `SectorProgress`: `state: SectorState`, `stars_collected: int`, `control_passed: bool`
  - `LevelProgress`: `is_completed: bool`, `best_stars: int`, `best_time: float`, `attempts: int`
  - `PlayerSaveData`: `save_version`, `version`, `last_modified`, `current_sector_index`, `sector_progress: dict`, `level_progress: dict`, `total_fragments`, `current_lives`, `last_life_restore_timestamp`, `owned_items: list`, `consumables: dict`, `total_levels_completed`, `total_stars_collected`, `total_play_time`

- `domain/enums.py` — добавить:
  - `SectorState`: `Locked`, `Available`, `InProgress`, `Completed`
  - `LevelType`: `Tutorial`, `Normal`, `Bonus`, `Control`, `Final`
  - `TaskType`: `ChooseCoordinate`, `ChooseFunction`, `AdjustGraph`, `BuildFunction`, `IdentifyError`, `RestoreConstellation`
  - `FunctionType`: `Linear`, `Quadratic`, `Sinusoidal`, `Mixed`
  - `TransactionType`: `earn`, `spend`

- `api/schemas/save.py`:
  - `SaveResponse`: `save_data: dict` (PlayerSaveData as JSON), `version: int`, `save_version: int`, `updated_at: int`
  - `SaveRequest`: `save_data: dict`, `expected_version: int`
  - `SaveUpdateResponse`: `version: int`, `updated_at: int`
  - `SaveConflictResponse`: `server_save: SaveResponse`, `error: ErrorDetail` (code: `SAVE_CONFLICT`)

- `infrastructure/persistence/save_repo.py` — `SaveRepository`:
  - `find_by_player_id(player_id: UUID, session) -> PlayerSaveModel | None`
  - `find_by_player_id_for_update(player_id: UUID, session) -> PlayerSaveModel | None` (SELECT ... FOR UPDATE)
  - `create(player_id: UUID, save_data: dict, session) -> PlayerSaveModel`
  - `update(save: PlayerSaveModel, save_data: dict, new_version: int, session)`

- `services/save_service.py` — `SaveService`:
  - `get_save(player_id: UUID, session) -> SaveResponse`:
    - Загрузить из БД, вернуть save_data + version
    - Если нет записи — вернуть дефолтный `PlayerSaveData` с `version=1`
  - `put_save(player_id: UUID, request: SaveRequest, session) -> SaveUpdateResponse`:
    1. Загрузить текущую запись из БД
    2. Если `request.expected_version != current_version` → `409 SAVE_CONFLICT` с серверным сохранением
    3. Если совпадает → обновить `save_data`, `version = expected_version + 1`, `updated_at = now`
    4. Вернуть `SaveUpdateResponse`

- `api/routers/save.py`:
  - `GET /` (authenticated) → `SaveService.get_save()`
  - `PUT /` (authenticated, Idempotency-Key) → `SaveService.put_save()`

**Тесты:**

- `tests/unit/test_save_service.py`:
  - Получение сохранения
  - Обновление с правильной `expected_version`
  - Конфликт при неправильной `expected_version`
  - Создание дефолтного сохранения для нового игрока

**Зависимости:** S1.2, S0.2.

**Критерий завершения:** `GET /save` возвращает сохранение; `PUT /save` обновляет при совпадении версии; конфликт при несовпадении → `409` с серверным сохранением; тесты проходят.

---

### ~~Задача S2.2 — EconomyService + economy router~~ (Done)

**Суть:** реализовать сервис экономики (фрагменты) с атомарными транзакциями и защитой от race conditions.

**Файлы:**

- `api/schemas/economy.py`:
  - `BalanceResponse`: `total_fragments: int`
  - `TransactionRequest`: `type: str` (`earn` | `spend`), `amount: int`, `reason: str`, `reference_id: str | None`
  - `TransactionResponse`: `transaction_id: UUID`, `previous_balance: int`, `new_balance: int`, `progress_update: dict | None` (для `skip_level`)
  - `SkipLevelProgressUpdate`: `level_id: str`, `stars: int`, `unlocked_levels: list[str]`, `unlocked_sectors: list[str]`

- `infrastructure/persistence/transaction_repo.py` — `TransactionRepository`:
  - `create(player_id: UUID, type: str, amount: int, reason: str, reference_id: str | None, previous_bal: int, new_bal: int, idempotency_key: UUID | None, session) -> TransactionModel`
  - `find_by_idempotency_key(key: UUID, session) -> TransactionModel | None`

- `domain/rules/economy.py` — `EconomyRules`:
  - `calculate_level_reward(level: LevelDefinition, stars: int) -> int` — фрагменты по конфигу уровня
  - `calculate_improvement_bonus(old_stars: int, new_stars: int, config: Settings) -> int` — `(new_stars - old_stars) * improvement_bonus_per_star`, только если `new_stars > old_stars`
  - `validate_transaction(tx_type: str, amount: int, current_balance: int) -> bool` — для `spend`: `balance >= amount`

- `services/economy_service.py` — `EconomyService`:
  - `get_balance(player_id: UUID, session) -> int`:
    - Проверить Redis-кеш `player:balance:{playerId}`
    - Если нет — загрузить из `player_saves.save_data->>'totalFragments'`
    - Закешировать (TTL 5 мин)
  - `execute_transaction(player_id: UUID, request: TransactionRequest, idempotency_key: UUID | None, session) -> TransactionResponse`:
    1. Проверить idempotency_key → если повтор, вернуть сохранённый ответ
    2. `SELECT ... FOR UPDATE` на `player_saves`
    3. Получить `current_balance` из `save_data`
    4. Для `spend` — `EconomyRules.validate_transaction()` → `422 INSUFFICIENT_FUNDS` при нехватке
    5. Обновить `save_data->'totalFragments'`
    6. **Если `reason == "skip_level"`:**
       - Загрузить `LevelDefinition` по `reference_id`
       - Обновить `level_progress`: `is_completed=True`, `best_stars=1`, `best_time=0`
       - Обновить `sector_progress`: `stars_collected += 1`, state-переходы
       - Обновить `total_levels_completed += 1`, `total_stars_collected += 1`
       - Проверить разблокировку (ProgressionRules — заглушка, реализация в S3.3)
       - Инкрементировать `version`
    7. Записать в `transactions`
    8. Инвалидировать Redis-кеш `player:balance:{playerId}`
    9. Вернуть `TransactionResponse`
    - Вся операция — в одной SQL-транзакции

- `api/routers/economy.py`:
  - `GET /balance` (authenticated) → `EconomyService.get_balance()` → `ApiResponse[BalanceResponse]`
  - `POST /transaction` (authenticated, Idempotency-Key) → `EconomyService.execute_transaction()` → `ApiResponse[TransactionResponse]`

**Тесты:**

- `tests/unit/test_economy_rules.py`:
  - Расчёт награды за уровень
  - Расчёт бонуса за улучшение (new > old → bonus; new <= old → 0)
  - Валидация транзакций (earn всегда ok; spend при нехватке → false)
- `tests/service/test_economy_service.py`:
  - Earn-транзакция увеличивает баланс
  - Spend-транзакция уменьшает баланс
  - Spend при нехватке → ошибка
  - Идемпотентность: повторный запрос с тем же ключом → тот же ответ

**Зависимости:** S2.1.

**Критерий завершения:** `GET /balance` возвращает баланс; `POST /transaction` атомарно меняет баланс; `spend` при нехватке → `422`; `skip_level` обновляет прогрессию; idempotency работает; тесты проходят.

---

### ~~Задача S2.3 — LivesService + lives router~~ (Done)

**Суть:** реализовать сервис жизней с серверным пересчётом по таймеру и восстановлением за фрагменты.

**Файлы:**

- `api/schemas/lives.py`:
  - `LivesResponse`: `current_lives: int`, `max_lives: int`, `seconds_until_next: int`, `restore_cost: int`
  - `RestoreLifeResponse`: `current_lives: int`, `max_lives: int`, `fragments_spent: int`, `new_balance: int`
  - `RestoreAllResponse`: `current_lives: int`, `max_lives: int`, `fragments_spent: int`, `new_balance: int`

- `domain/rules/lives.py` — `LivesRules`:
  - Dataclass `LivesState`: `current_lives: int`, `seconds_until_next: int`, `last_restore_timestamp: int`
  - `recalculate(current_lives: int, last_restore_ts: int, server_now: int, config: Settings) -> LivesState`:

    ```py
    if current_lives >= max_lives: return LivesState(current_lives, 0, last_restore_ts)
    elapsed = server_now - last_restore_ts
    restored = elapsed // restore_interval_seconds
    new_lives = min(current_lives + restored, max_lives)
    new_last_restore_ts = last_restore_ts + restored * restore_interval_seconds
    if new_lives >= max_lives: new_last_restore_ts = server_now
    seconds_until_next = 0 if new_lives >= max_lives else restore_interval_seconds - (elapsed % restore_interval_seconds)
    return LivesState(new_lives, seconds_until_next, new_last_restore_ts)
    ```

- `services/lives_service.py` — `LivesService`:
  - `get_lives(player_id: UUID, session) -> LivesResponse`:
    1. Загрузить `PlayerSaveData` из БД
    2. Вызвать `LivesRules.recalculate()` с `server_now = int(datetime.now(UTC).timestamp())`
    3. Если жизни изменились — обновить `save_data` в БД
    4. Вернуть `LivesResponse`
  - `restore_one(player_id: UUID, session) -> RestoreLifeResponse`:
    1. `SELECT ... FOR UPDATE` на `player_saves`
    2. Пересчитать жизни
    3. Если `current_lives >= max_lives` → `400 LIVES_ALREADY_FULL`
    4. Списать `restore_cost_fragments` через обновление `save_data->'totalFragments'` → `422 INSUFFICIENT_FUNDS` при нехватке
    5. `current_lives += 1`
    6. Записать транзакцию в `transactions` (reason: `restore_life`)
    7. Обновить `save_data`
    8. Вернуть результат
  - `restore_all(player_id: UUID, session) -> RestoreAllResponse`:
    1. Аналогично `restore_one`, но восстановить до `max_lives`
    2. Стоимость: `restore_cost_fragments × (max_lives - current_lives)`

- `api/routers/lives.py`:
  - `GET /` (authenticated) → `LivesService.get_lives()` → `ApiResponse[LivesResponse]`
  - `POST /restore` (authenticated, Idempotency-Key) → `LivesService.restore_one()` → `ApiResponse[RestoreLifeResponse]`
  - `POST /restore-all` (authenticated, Idempotency-Key) → `LivesService.restore_all()` → `ApiResponse[RestoreAllResponse]`

**Тесты:**

- `tests/unit/test_lives_rules.py`:
  - Пересчёт: 0 жизней + 3600 сек → 2 жизни (при интервале 1800)
  - Пересчёт: не превышает `max_lives`
  - Полные жизни → `seconds_until_next = 0`
  - Частичное время → корректный `seconds_until_next`

**Зависимости:** S2.1.

**Критерий завершения:** `GET /lives` пересчитывает жизни по серверному времени; `POST /restore` списывает фрагменты и добавляет жизнь; `POST /restore-all` восстанавливает до максимума; при нехватке фрагментов → `422`; тесты проходят.

---

### ~~Задача S2.4 — ContentService + content router~~ (Done)

**Суть:** реализовать сервис раздачи контента (уровни, секторы, баланс, каталог магазина) с Redis-кешированием.

**Файлы:**

- `domain/content_models.py` — доменные модели контента (dataclasses):
  - `StarDefinition`: `star_id`, `coordinate: tuple[float, float]`, `initial_state`, `is_control_point`, `is_distractor`, `belongs_to_solution`, `reveal_after_action`
  - `StarRatingConfig`: `three_star_max_errors`, `two_star_max_errors`, `one_star_max_errors`, `timer_affects_rating`, `three_star_max_time`
  - `AnswerOption`: `option_id`, `text`, `value`, `is_correct`
  - `GraphVisibilityConfig`: `partial_reveal`, `initial_visible_segments`, `reveal_per_correct_action`
  - `HintDefinition`: `trigger`, `hint_text`, `highlight_position`, `trigger_after_errors`
  - `ReferenceFunctionDef`: `function_type: FunctionType`, `coefficients: list[float]`, `domain_range: tuple[float, float]`
  - `LevelDefinition`: `level_id`, `level_index`, `level_type`, `sector_id`, `task_type`, `plane_min`, `plane_max`, `grid_step`, `stars: list[StarDefinition]`, `reference_functions: list[ReferenceFunctionDef]`, `answer_options: list[AnswerOption]`, `accuracy_threshold`, `star_rating: StarRatingConfig`, `max_attempts`, `max_adjustments`, `use_memory_mode`, `memory_display_duration`, `graph_visibility: GraphVisibilityConfig`, `hints: list[HintDefinition]`, `fragment_reward`
  - `SectorDefinition`: `sector_id`, `display_name`, `sector_index`, `levels: list[str]` (level IDs), `previous_sector`, `required_stars_to_unlock`
  - `BalanceConfig`: `max_lives`, `restore_interval_seconds`, `restore_cost_fragments`, `skip_level_cost_fragments`, `improvement_bonus_per_star`, `hint_cost_fragments`
  - `ContentManifest`: `content_version: int`, `sectors: dict[str, int]` (sector_id → version), `balance_version: int`, `shop_version: int`

- `api/schemas/content.py`:
  - `ContentManifestResponse`: `content_version: int`, `sectors: dict[str, int]`, `balance_version: int`, `shop_version: int`
  - `SectorResponse`: `sector: dict` (SectorDefinition as JSON)
  - `SectorsResponse`: `sectors: list[dict]`
  - `LevelsResponse`: `levels: list[dict]` (LevelDefinition as JSON)
  - `LevelResponse`: `level: dict`
  - `BalanceConfigResponse`: `config: dict` (BalanceConfig as JSON)

- `infrastructure/persistence/content_repo.py` — `ContentRepository`:
  - `get_active_content(content_type: str, content_id: str | None, session) -> ContentVersionModel | None`
  - `get_all_active_by_type(content_type: str, session) -> list[ContentVersionModel]`
  - `get_manifest(session) -> dict` — агрегация версий всех типов контента

- `services/content_service.py` — `ContentService`:
  - Все методы: сначала проверить Redis-кеш → если miss, загрузить из БД → закешировать
  - `get_manifest(session) -> ContentManifestResponse`
  - `get_sectors(session) -> SectorsResponse`
  - `get_sector(sector_id: str, session) -> SectorResponse`
  - `get_levels(sector_id: str, session) -> LevelsResponse`
  - `get_level(level_id: str, session) -> LevelResponse` — `404 LEVEL_NOT_FOUND` если не найден
  - `get_balance_config(session) -> BalanceConfigResponse`

- `api/routers/content.py`:
  - `GET /manifest` (authenticated) → `ContentService.get_manifest()`
  - `GET /sectors` (authenticated) → `ContentService.get_sectors()`
  - `GET /sectors/{sector_id}` (authenticated) → `ContentService.get_sector()`
  - `GET /sectors/{sector_id}/levels` (authenticated) → `ContentService.get_levels()`
  - `GET /levels/{level_id}` (authenticated) → `ContentService.get_level()`
  - `GET /balance` (authenticated) → `ContentService.get_balance_config()`

**Зависимости:** S0.2, S0.3.

**Критерий завершения:** все `/content/*` эндпоинты работают; данные кешируются в Redis; повторный запрос идёт из кеша; `404` при несуществующем контенте.

---

### ~~Задача S2.5 — Seed data для контента~~ (Done)

**Суть:** создать скрипт и JSON-данные для первичного заполнения таблицы `content_versions` данными 5 секторов, 100 уровней, баланса и каталога магазина.

**Файлы:**

- `seed/data/sectors.json` — 5 секторов (`SectorDefinition`):
  - Каждый сектор: `sector_id` (`sector_1`..`sector_5`), `display_name`, `sector_index` (0-4), `levels` (массив из 20 `level_id`), `previous_sector` (null для первого), `required_stars_to_unlock`
  - Структура уровней в каждом секторе (индексы): 0 Tutorial, 1-5 Normal, 6 Bonus, 7-10 Normal, 11 Bonus, 12-17 Normal, 18 Control, 19 Final

- `seed/data/levels/sector_1.json` ... `seed/data/levels/sector_5.json` — по 20 уровней в каждом файле:
  - Минимальная реализация: `level_id`, `level_index`, `level_type`, `sector_id`, `task_type`, координатная плоскость, `stars` (массив `StarDefinition`), `reference_functions`, `answer_options` (для ChooseCoordinate/ChooseFunction), `accuracy_threshold`, `star_rating`, `fragment_reward`
  - Первый сектор — только `ChooseCoordinate` задания (Tutorial)
  - Последующие секторы — разные `TaskType`

- `seed/data/balance.json` — `BalanceConfig`:

  ```json
  {
    "max_lives": 5,
    "restore_interval_seconds": 1800,
    "restore_cost_fragments": 20,
    "skip_level_cost_fragments": 100,
    "improvement_bonus_per_star": 5,
    "hint_cost_fragments": 10
  }
  ```

- `seed/data/shop_catalog.json` — массив `ShopItemDefinition`:
  - Примеры: подсказки (consumable), скины/темы (permanent)
  - Каждый элемент: `item_id`, `display_name`, `description`, `category`, `price`, `is_consumable`, `quantity` (для consumable)

- `seed/seed_content.py` — скрипт заполнения:
  - Подключение к БД через `database_url` из `.env`
  - Чтение JSON-файлов из `seed/data/`
  - Insert в `content_versions` для каждого элемента: `content_type` + `content_id` + `version=1` + `data` (JSONB) + `is_active=True`
  - Обработка повторного запуска: skip / upsert
  - Запуск: `python -m seed.seed_content`

**Зависимости:** S2.4.

**Критерий завершения:** после запуска `python -m seed.seed_content` таблица `content_versions` содержит 5 секторов, 100 уровней, баланс и каталог; `GET /content/manifest` возвращает корректный манифест; `GET /content/sectors/sector_1/levels` возвращает 20 уровней.

---

## Фаза S3 — Ключевая бизнес-логика

### Задача S3.1 — ValidationEngine

**Суть:** реализовать серверную валидацию ответов игрока для всех 6 типов заданий.

**Файлы:**

- `domain/check_models.py` — модели для проверки:
  - `StarPlacement`: `star_id: str`, `coordinate: tuple[float, float]`
  - `PlayerAnswer`: union-тип с вложенными вариантами:
    - `ChooseOptionAnswer`: `selected_option_id: str`
    - `FunctionAnswer`: `function_type: FunctionType`, `coefficients: list[float]`
    - `IdentifyStarsAnswer`: `selected_star_ids: list[str]`
    - `PlaceStarsAnswer`: `placements: list[StarPlacement]`
  - `CheckResult`: `is_valid: bool`, `match_percentage: float` (0–1), `errors: list[str]`

- `domain/rules/validation_engine.py` — `ValidationEngine`:
  - `validate(level: LevelDefinition, answer: PlayerAnswer) -> CheckResult`:
    - Диспатч по `level.task_type` → внутренний метод
  - `_validate_choose_option(level, selected_option_id) -> CheckResult`:
    - Найти вариант с `is_correct=True` в `level.answer_options`
    - Сравнить с `selected_option_id`
    - `match_percentage = 1.0 если верно, 0.0 если нет`
  - `_validate_function(level, function_type, coefficients) -> CheckResult`:
    - Получить эталон из `level.reference_functions[0]`
    - Вычислить значения на контрольных точках (stars с `belongs_to_solution=True`) или на равномерной сетке внутри `domain_range`
    - RMSD = `sqrt(sum((y_player - y_reference) ** 2) / n)`
    - `is_valid = rmsd <= level.accuracy_threshold`
    - `match_percentage = max(0, 1 - rmsd / max_rmsd)` (clamp to [0, 1])
  - `_validate_identify_stars(level, selected_star_ids) -> CheckResult`:
    - Эталон: все `star_id` с `is_distractor=True` в `level.stars`
    - Сравнить множества: `is_valid = (selected == expected)`
    - `match_percentage = len(intersection) / len(union)`
  - `_validate_place_stars(level, placements, threshold) -> CheckResult`:
    - Для каждого placement: найти соответствующий star по `star_id`, сравнить координаты с допуском `threshold`
    - `is_valid = all(distance(placed, expected) <= threshold)`
    - `match_percentage = count(correct) / total`

**Тесты:**

- `tests/unit/test_validation_engine.py`:
  - ChooseOption: верный ответ → valid; неверный → invalid
  - Function (Linear): точные коэффициенты → valid; далёкие → invalid; в пределах threshold → valid
  - Function (Quadratic, Sinusoidal): аналогично
  - IdentifyStars: полное совпадение → valid; частичное → invalid с match_percentage
  - PlaceStars: все в пределах threshold → valid; один за пределами → invalid

**Зависимости:** S2.5 (нужны LevelDefinition из seed data).

**Критерий завершения:** все 6 типов заданий валидируются корректно; RMSD-расчёт даёт правильные результаты; match_percentage в диапазоне [0, 1]; все unit-тесты проходят.

---

### Задача S3.2 — StarRatingCalculator

**Суть:** реализовать расчёт звёздного рейтинга (0-3 звезды) на основе ошибок и времени.

**Файлы:**

- `domain/rules/star_rating.py` — `StarRatingCalculator`:
  - `calculate(config: StarRatingConfig, error_count: int, elapsed_time: float) -> int`:
    - `error_count <= config.three_star_max_errors` → 3 звезды
    - `error_count <= config.two_star_max_errors` → 2 звезды
    - `error_count <= config.one_star_max_errors` → 1 звезда
    - Иначе → 0 звёзд (уровень не пройден)
    - Если `config.timer_affects_rating` и `elapsed_time > config.three_star_max_time` → снижение на 1 звезду (минимум 0)

**Тесты:**

- `tests/unit/test_star_rating.py`:
  - 0 ошибок → 3 звезды
  - Ровно `three_star_max_errors` → 3 звезды
  - `three_star_max_errors + 1` → 2 звезды
  - Превышение `one_star_max_errors` → 0 звёзд
  - Влияние таймера: 0 ошибок + превышение времени → 2 звезды
  - `timer_affects_rating=False` + превышение времени → без снижения
  - Снижение не уходит ниже 0

**Зависимости:** нет (чистая бизнес-логика).

**Критерий завершения:** все рейтинговые сценарии покрыты тестами; граничные случаи (ровно на пороге, таймер, 0 ошибок) работают корректно.

---

### Задача S3.3 — ProgressionRules

**Суть:** реализовать правила разблокировки уровней и секторов.

**Файлы:**

- `domain/rules/progression.py` — `ProgressionRules`:
  - `is_level_unlocked(save: PlayerSaveData, level: LevelDefinition, sector: SectorDefinition) -> bool`:
    - Tutorial (index 0) — всегда доступен, если сектор `Available` / `InProgress`
    - Normal/Control/Final — доступен, если предыдущий уровень `is_completed`
    - Bonus (index 6, 11) — опционален: игрок может пропустить и перейти к следующему обязательному
  - `can_unlock_sector(save: PlayerSaveData, sector: SectorDefinition) -> bool`:
    - Пройден контрольный уровень (index 18) предыдущего сектора (`control_passed=True`)
    - Набран порог звёзд (`stars_collected >= required_stars_to_unlock`) предыдущего сектора
    - **Звёзды бонусных уровней (type=Bonus) НЕ учитываются** в пороге
  - `get_unlocked_levels(save: PlayerSaveData, completed_level: LevelDefinition, sector: SectorDefinition) -> list[str]`:
    - Возвращает список вновь разблокированных `level_id` после прохождения
  - `get_unlocked_sectors(save: PlayerSaveData, sectors: list[SectorDefinition]) -> list[str]`:
    - Возвращает список вновь разблокированных `sector_id`

**Тесты:**

- `tests/unit/test_progression_rules.py`:
  - Tutorial всегда доступен в доступном секторе
  - Уровень N+1 доступен после прохождения N
  - Бонусный уровень пропускаем → следующий обязательный доступен
  - Разблокировка сектора: контрольный пройден + достаточно звёзд → ok
  - Разблокировка сектора: мало звёзд → нет
  - Бонусные звёзды не считаются в пороге

**Зависимости:** S2.5 (нужны definition-ы секторов/уровней).

**Критерий завершения:** правила разблокировки реализованы корректно; бонусные уровни не блокируют прогрессию; бонусные звёзды не учитываются в пороге раскрытия сектора; все тесты проходят.

---

### Задача S3.4 — LevelCheckService + check router

**Суть:** реализовать самый сложный сервис — атомарную серверную проверку ответа на уровне, включая валидацию, экономику, жизни и прогрессию.

**Файлы:**

- `api/schemas/check.py`:
  - `CheckLevelRequest`: `level_id: str`, `answer: dict` (PlayerAnswer), `elapsed_time: float`, `errors_before_submit: int`, `attempt: int`
  - `CheckResultSchema`: `is_valid: bool`, `stars: int`, `fragments_earned: int`, `match_percentage: float`, `errors: list[str]`
  - `ProgressUpdate`: `level_id: str`, `is_completed: bool`, `best_stars: int`, `best_time: float`, `unlocked_levels: list[str]`, `unlocked_sectors: list[str]`, `sector_state_changes: dict`
  - `LivesUpdate`: `current_lives: int`, `max_lives: int`, `seconds_until_next: int`, `level_failed: bool`, `fail_reason: str | None`
  - `CheckLevelResponse`: `result: CheckResultSchema`, `progress_update: ProgressUpdate | None`, `lives_update: LivesUpdate`, `new_save_version: int`

- `services/level_check_service.py` — `LevelCheckService`:
  - `check(player_id: UUID, request: CheckLevelRequest, session) -> CheckLevelResponse`:
    Полный алгоритм (из Server architecture.md §7.5):
    1. Загрузить `LevelDefinition` по `level_id` из `content_versions` → `404 LEVEL_NOT_FOUND`
    2. `SELECT ... FOR UPDATE` на `player_saves`
    3. Пересчитать жизни (`LivesRules.recalculate`)
    4. Если `current_lives == 0` после пересчёта → `422 NO_LIVES` (попытка не засчитывается)
    5. `ValidationEngine.validate(level, answer)`
    6. **Если верно:**
       a. `StarRatingCalculator.calculate(level.star_rating, errors_for_rating, elapsed_time)` где `errors_for_rating = errors_before_submit + (0 if is_valid else 1)`
       b. Рассчитать фрагменты:
       - Первое прохождение → `level.fragment_reward`
       - Улучшение → `EconomyRules.calculate_improvement_bonus(old_stars, new_stars, settings)`
       - Результат равен или хуже → 0 фрагментов
         c. Обновить `level_progress`: `is_completed=True`, `best_stars=max(old, new)`, `best_time=min(old, new) (if > 0)`, `attempts += 1`
         d. Обновить `sector_progress`:
       - Первый пройденный уровень + state==Available → state=InProgress
       - Финальный уровень (index 19, type=Final) → state=Completed
       - `stars_collected` += новые звёзды (delta)
         e. `total_fragments += fragments_earned`
         f. `ProgressionRules` → получить разблокированные уровни/секторы
         g. Обновить `total_levels_completed`, `total_stars_collected`
         h. Инкрементировать `version`
    7. **Если неверно:**
       a. `current_lives -= 1`
       b. Обновить `last_life_restore_timestamp` (если жизни были полные — начать таймер)
       c. Проверить провал:
       - `current_lives == 0` → `level_failed=True`, `fail_reason="no_lives"`
       - `max_attempts > 0 and attempt >= max_attempts` → `level_failed=True`, `fail_reason="max_attempts_reached"`
         d. Инкрементировать `version`
    8. Записать обновлённый `save_data` в БД
    9. Вернуть `CheckLevelResponse` с `result`, `progress_update`, `lives_update`, `new_save_version`

- `api/routers/check.py`:
  - `POST /level` (authenticated, Idempotency-Key) → `LevelCheckService.check()` → `ApiResponse[CheckLevelResponse]`

**Тесты:**

- `tests/service/test_level_check_service.py`:
  - Верный ответ → звёзды + фрагменты + is_completed=True
  - Неверный ответ → жизнь списана, fragments_earned=0
  - Повторное прохождение: улучшение → бонус; ухудшение → 0 фрагментов, bestStars не меняется
  - 0 жизней → 422 NO_LIVES
  - max_attempts превышен → level_failed
  - Разблокировка следующего уровня после прохождения
  - Разблокировка сектора после контрольного уровня
  - Атомарность: при ошибке БД — откат всех изменений

**Зависимости:** S3.1, S3.2, S3.3, S2.2, S2.3.

**Критерий завершения:** `POST /check/level` атомарно: валидирует ответ, рассчитывает звёзды/фрагменты, обновляет жизни/прогрессию; все сценарии (верно/неверно, первое/повторное, провал) работают; тесты проходят.

---

### Задача S3.5 — SaveMerger

**Суть:** реализовать стратегию мержа сохранений при конфликтах (409 SAVE_CONFLICT).

**Файлы:**

- `domain/rules/save_merger.py` — `SaveMerger`:
  - `merge(local: PlayerSaveData, server: PlayerSaveData) -> PlayerSaveData`:
    Стратегия «прогресс всегда вперёд»:

    ```txt
    Для каждого level_progress:
      is_completed    = local OR server
      best_stars      = max(local, server)
      best_time       = если оба > 0: min; если один 0: другой
      attempts        = max(local, server)

    Для каждого sector_progress:
      state           = max по порядку: Locked < Available < InProgress < Completed
      stars_collected = max(local, server)
      control_passed  = local OR server

    total_fragments         = server            (source of truth)
    current_lives           = server            (source of truth)
    last_life_restore_ts    = server
    consumables             = server            (source of truth)
    owned_items             = union(local, server)

    current_sector_index    = max(local, server)
    total_levels_completed  = max(local, server)
    total_stars_collected   = max(local, server)
    total_play_time         = max(local, server)
    version                 = server.version + 1
    last_modified           = now
    ```

**Тесты:**

- `tests/unit/test_save_merger.py`:
  - Мерж двух пустых → дефолт
  - Прогрессия: local=InProgress + server=Completed → Completed
  - Звёзды: local=2 + server=3 → 3
  - Время: local=15.0 + server=20.0 → 15.0; local=0 + server=20.0 → 20.0
  - Фрагменты: всегда серверные
  - Жизни: всегда серверные
  - owned_items: union (дедупликация)
  - Уровень пройден в local, не пройден на server → is_completed=True

**Зависимости:** S2.1.

**Критерий завершения:** мерж корректно разрешает конфликты по принципу «прогресс вперёд, экономика — серверная»; все edge cases покрыты тестами.

---

## Фаза S4 — Магазин, аналитика, полировка

### Задача S4.1 — ShopService + shop router

**Суть:** реализовать сервис магазина — каталог товаров и покупка за фрагменты.

**Файлы:**

- `domain/shop_models.py` — модели магазина:
  - `ShopItemDefinition`: `item_id: str`, `display_name: str`, `description: str`, `category: str`, `price: int`, `is_consumable: bool`, `quantity: int` (для consumable, количество за покупку)

- `api/schemas/shop.py`:
  - `ShopItemSchema`: все поля `ShopItemDefinition`
  - `ShopItemsResponse`: `items: list[ShopItemSchema]`, `shop_version: int`
  - `PurchaseRequest`: `item_id: str`, `cached_price: int` (цена, которую видел клиент)
  - `PurchaseResponse`: `item_id: str`, `fragments_spent: int`, `new_balance: int`, `owned_items: list[str]`, `consumables: dict[str, int]`

- `services/shop_service.py` — `ShopService`:
  - `get_items(session) -> ShopItemsResponse`:
    - Загрузить каталог из `content_versions` (type=`shop_catalog`)
    - Кеш Redis (TTL 10 мин)
  - `purchase(player_id: UUID, request: PurchaseRequest, session) -> PurchaseResponse`:
    1. Найти товар по `item_id` → `404 ITEM_NOT_FOUND`
    2. Если `not is_consumable` и `item_id in owned_items` → `400 ITEM_ALREADY_OWNED`
    3. Серверная цена (не `cached_price`) — цена из каталога
    4. `SELECT ... FOR UPDATE` на `player_saves`
    5. Проверить баланс (`total_fragments >= price`) → `422 INSUFFICIENT_FUNDS`
    6. Обновить `total_fragments -= price`
    7. Если `is_consumable` → `consumables[item_id] += quantity`
    8. Если permanent → `owned_items.append(item_id)`
    9. Записать транзакцию (reason: `shop_purchase`, reference_id: item_id)
    10. Инвалидировать кеш баланса
    11. Инкрементировать save version
    12. Вернуть `PurchaseResponse`

- `api/routers/shop.py`:
  - `GET /items` (authenticated) → `ShopService.get_items()` → `ApiResponse[ShopItemsResponse]`
  - `POST /purchase` (authenticated, Idempotency-Key) → `ShopService.purchase()` → `ApiResponse[PurchaseResponse]`

**Тесты:**

- `tests/service/test_shop_service.py`:
  - Получение каталога
  - Покупка consumable → увеличивает consumables
  - Покупка permanent → добавляет в owned_items
  - Повторная покупка permanent → ошибка
  - Недостаточно фрагментов → ошибка
  - Несуществующий товар → ошибка

**Зависимости:** S2.2.

**Критерий завершения:** `GET /items` возвращает каталог; `POST /purchase` атомарно списывает фрагменты и выдаёт товар; идемпотентность работает; тесты проходят.

---

### Задача S4.2 — AnalyticsService + analytics router

**Суть:** реализовать приём аналитических событий от клиента — батчевый bulk-insert с валидацией.

**Файлы:**

- `api/schemas/analytics.py`:
  - `AnalyticsEvent`: `event_name: str`, `params: dict | None`, `client_ts: int`, `session_id: UUID | None`
  - `AnalyticsEventsRequest`: `events: list[AnalyticsEvent]` (max 100)
  - `AnalyticsResponse`: `accepted: int`, `rejected: int`

- `infrastructure/persistence/analytics_repo.py` — `AnalyticsRepository`:
  - `bulk_insert(player_id: UUID, events: list[dict], session)` — bulk INSERT в `analytics_events`

- `services/analytics_service.py` — `AnalyticsService`:
  - `ALLOWED_EVENTS` — белый список event_name: `session_start`, `session_end`, `level_start`, `level_complete`, `level_fail`, `level_skip`, `sector_unlock`, `purchase`, `hint_used`, `life_lost`, `life_restored`, `action_undo`, `level_reset`
  - `ingest_events(player_id: UUID, request: AnalyticsEventsRequest, session) -> AnalyticsResponse`:
    1. Валидация: максимум 100 событий, `event_name` в белом списке
    2. Фильтрация невалидных (подсчёт rejected)
    3. Bulk-insert валидных в БД
    4. Вернуть `202 Accepted` с counters

- `api/routers/analytics.py`:
  - `POST /events` (authenticated) → `AnalyticsService.ingest_events()` → `ApiResponse[AnalyticsResponse]` (HTTP 202)

**Зависимости:** S0.2.

**Критерий завершения:** `POST /events` принимает батч событий; невалидные фильтруются; bulk-insert работает; возвращает количество accepted/rejected; rate limiting (10 req/min).

---

### Задача S4.3 — Health checks + метрики

**Суть:** реализовать эндпоинты проверки здоровья и Prometheus-метрики.

**Файлы:**

- `api/routers/health.py`:
  - `GET /` — liveness probe: `{"status": "ok"}`
  - `GET /ready` — readiness probe: проверка PostgreSQL (`SELECT 1`) + Redis (`PING`)
    - Если одна из проверок не прошла → `503 Service Unavailable`

- `main.py` — интеграция `prometheus-fastapi-instrumentator`:
  - Автоматические метрики: `http_requests_total`, `http_request_duration_seconds`
  - Кастомные метрики (через `prometheus_client`):
    - `level_checks_total` (Counter, labels: valid/invalid)
    - `transactions_total` (Counter, labels: earn/spend)
    - `save_conflicts_total` (Counter)
    - `active_players_gauge` (Gauge)

- Настройка `structlog`:
  - JSON-формат для production
  - Цветной вывод для development
  - Контекстные поля: `player_id`, `request_id`, `endpoint`

**Зависимости:** S0.1.

**Критерий завершения:** `GET /health` → 200; `GET /health/ready` → 200 если DB + Redis живы, 503 если нет; `/metrics` возвращает Prometheus-метрики; логи в structured JSON.

---

### Задача S4.4 — Интеграционные тесты

**Суть:** написать полные end-to-end тесты для критических бизнес-потоков с реальной БД и Redis через testcontainers.

**Файлы:**

- `tests/conftest.py` — общие fixtures:
  - `postgres_container` — testcontainers PostgreSQL
  - `redis_container` — testcontainers Redis
  - `db_session` — async session для тестов (с rollback после каждого теста)
  - `client` — `httpx.AsyncClient` для FastAPI test app
  - `auth_headers(player_id)` — helper для создания авторизованного клиента
  - Seed data загрузка для тестового окружения

- `tests/integration/test_auth_endpoints.py`:
  - Register → получить токены → использовать access token для запроса
  - Register повторно → тот же player_id
  - Refresh → новые токены, старый refresh отозван
  - Невалидный токен → 401

- `tests/integration/test_save_endpoints.py`:
  - GET /save → дефолтное сохранение
  - PUT /save → обновление
  - PUT /save с неправильной version → 409 SAVE_CONFLICT
  - Идемпотентность PUT /save

- `tests/integration/test_check_level_endpoints.py`:
  - Полный flow: register → check level (верный) → баланс увеличился → save version увеличился
  - Check level (неверный) → жизнь списана
  - Check level при 0 жизнях → 422
  - Повторное прохождение с улучшением → бонус
  - Разблокировка следующего уровня/сектора

- `tests/integration/test_economy_endpoints.py`:
  - GET /balance
  - POST /transaction (earn) → баланс увеличился
  - POST /transaction (spend) → баланс уменьшился
  - POST /transaction (spend, нехватка) → 422
  - Skip level → прогрессия обновлена
  - Идемпотентность

**Зависимости:** S3.4.

**Критерий завершения:** все интеграционные тесты проходят в CI с testcontainers; покрытие критических путей: auth flow, level check flow, economy flow, save conflict flow.

---

### Задача S4.5 — Документация API

**Суть:** настроить автогенерацию OpenAPI-документации через FastAPI и дополнить описаниями.

**Что сделать:**

- Настроить FastAPI metadata:

  ```python
  app = FastAPI(
      title="STAR FUNC API",
      version="1.0.0",
      description="Backend API for STAR FUNC mobile game",
      docs_url="/docs",           # Swagger UI
      redoc_url="/redoc",         # ReDoc
      openapi_url="/openapi.json"
  )
  ```

- Добавить описания и примеры в Pydantic-схемы:
  - `Field(description="...", example="...")`
  - `model_config = {"json_schema_extra": {"examples": [...]}}`

- Добавить описания роутеров:
  - `summary` и `description` для каждого эндпоинта
  - `responses` с описанием ошибок (401, 409, 422, 429)
  - Tags для группировки

- Добавить OpenAPI Security Scheme:
  - Bearer token authentication
  - Описание Idempotency-Key заголовка

- Проверить соответствие автогенерированного OpenAPI спецификации из [API.md](API.md)

**Зависимости:** S3.4 (все роутеры должны быть реализованы).

**Критерий завершения:** `/docs` отображает полную Swagger UI; все эндпоинты документированы с примерами; можно выполнить запросы из Swagger UI; OpenAPI-спецификация соответствует [API.md](API.md).

---

## Диаграмма зависимостей

```txt
S0.1 ─┬─ S0.2 ─┬─ S1.2 ─┬─ S1.3
      │        │  ▲      │
      ├─ S0.3  │  S1.1   ├─ S1.4
      │        │         │
      └─ S0.4  │         ├─ S2.1 ─┬─ S2.2 ─── S4.1
               │         │        │
               │         │        ├─ S2.3
               │         │        │
               │         │        └─ S3.5
               │         │
               ├─ S2.4 ──┤─ S2.5 ─┬─ S3.1
               │         │        │
               │         │        └─ S3.3
               │         │
               └─ S4.2   S3.2 (независимая)
                         │
                         └─── S3.4 ─┬─ S4.4
                                    └─ S4.5

S0.1 ──── S4.3
```

---

## Связь с клиентскими задачами

| Клиентская задача (Tasks.md)                   | Серверная задача | Описание                                                 |
| ---------------------------------------------- | ---------------- | -------------------------------------------------------- |
| 1.12 (ApiClient, NetworkMonitor, TokenManager) | S1.2             | Клиент интегрируется с `POST /auth/register`, `/refresh` |
| 1.13 (AuthService клиент)                      | S1.2, S1.4       | Клиентский AuthService вызывает серверный                |
| 2.1a (CloudSaveClient, HybridSaveService)      | S2.1             | `GET /save`, `PUT /save`                                 |
| 2.3a (ServerEconomyService)                    | S2.2             | `GET /economy/balance`, `POST /economy/transaction`      |
| 2.4a (ServerLivesService)                      | S2.3             | `GET /lives`, `POST /lives/restore`                      |
| 2.13 (ContentService клиент)                   | S2.4, S2.5       | `/content/*`                                             |
| 4.3a (ServerShopService)                       | S4.1             | `GET /shop/items`, `POST /shop/purchase`                 |
| 4.8a (REST analytics)                          | S4.2             | `POST /analytics/events`                                 |

> **Порядок:** серверные фазы S0–S1 параллелятся с клиентскими фазами 1–2 (клиент работает offline-first). Интеграция начинается с S2, когда клиент получает задачи 1.12–1.13.
