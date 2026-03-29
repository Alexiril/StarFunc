# Серверная архитектура STAR FUNC

> Документ описывает полную архитектуру бэкенда для мобильной игры STAR FUNC.
> Бэкенд обслуживает гибридную (offline-first) клиент-серверную модель: клиент работает автономно, сервер является авторитетным источником истины для экономики, жизней, валидации ответов и облачных сохранений.
> Спецификация API описана в [API.md](API.md). Клиентская архитектура — в [Architecture.md](Architecture.md).

---

## Оглавление

- [1. Обзор и принципы](#1-обзор-и-принципы)
- [2. Технологический стек](#2-технологический-стек)
- [3. Высокоуровневая архитектура](#3-высокоуровневая-архитектура)
- [4. Структура проекта (серверная кодовая база)](#4-структура-проекта-серверная-кодовая-база)
- [5. Слой данных (Database Layer)](#5-слой-данных-database-layer)
  - [5.1 Схема базы данных](#51-схема-базы-данных)
  - [5.2 Redis — кеш и сессии](#52-redis--кеш-и-сессии)
- [6. Доменный слой (Domain Layer)](#6-доменный-слой-domain-layer)
  - [6.1 Модели предметной области](#61-модели-предметной-области)
  - [6.2 Бизнес-правила](#62-бизнес-правила)
- [7. Сервисный слой (Service Layer)](#7-сервисный-слой-service-layer)
  - [7.1 AuthService](#71-authservice)
  - [7.2 SaveService](#72-saveservice)
  - [7.3 EconomyService](#73-economyservice)
  - [7.4 LivesService](#74-livesservice)
  - [7.5 LevelCheckService](#75-levelcheckservice)
  - [7.6 ShopService](#76-shopservice)
  - [7.7 ContentService](#77-contentservice)
  - [7.8 AnalyticsService](#78-analyticsservice)
- [8. API-слой (Router Layer)](#8-api-слой-router-layer)
  - [8.1 Middleware pipeline](#81-middleware-pipeline)
  - [8.2 Роутеры](#82-роутеры)
  - [8.3 Идемпотентность](#83-идемпотентность)
- [9. Аутентификация и авторизация](#9-аутентификация-и-авторизация)
  - [9.1 JWT-токены](#91-jwt-токены)
  - [9.2 Refresh-токены](#92-refresh-токены)
  - [9.3 Привязка сторонних аккаунтов](#93-привязка-сторонних-аккаунтов)
- [10. Валидация ответов (Level Check)](#10-валидация-ответов-level-check)
  - [10.1 Алгоритм серверной проверки](#101-алгоритм-серверной-проверки)
  - [10.2 Правила по типам заданий](#102-правила-по-типам-заданий)
  - [10.3 Звёздный рейтинг](#103-звёздный-рейтинг)
  - [10.4 Провал уровня](#104-провал-уровня)
- [11. Экономика и транзакции](#11-экономика-и-транзакции)
- [12. Система жизней](#12-система-жизней)
- [13. Контент-менеджмент (Remote Config)](#13-контент-менеджмент-remote-config)
- [14. Аналитика](#14-аналитика)
- [15. Синхронизация и конфликты](#15-синхронизация-и-конфликты)
  - [15.1 Очередь синхронизации](#151-очередь-синхронизации)
  - [15.2 Алгоритм мержа сохранений](#152-алгоритм-мержа-сохранений)
- [16. Безопасность](#16-безопасность)
- [17. Инфраструктура и деплой](#17-инфраструктура-и-деплой)
  - [17.1 Окружения](#171-окружения)
  - [17.2 CI/CD](#172-cicd)
  - [17.3 Контейнеризация](#173-контейнеризация)
- [18. Масштабирование](#18-масштабирование)
- [19. Мониторинг и наблюдаемость](#19-мониторинг-и-наблюдаемость)
- [20. Оценка задач](#20-оценка-задач)

---

## 1. Обзор и принципы

### 1.1 Роль сервера

Сервер выполняет следующие функции:

| Функция                     | Описание                                                                    |
| --------------------------- | --------------------------------------------------------------------------- |
| **Аутентификация**          | Анонимная регистрация, JWT-токены, привязка Google Play / Apple Game Center |
| **Облачные сохранения**     | Хранение и синхронизация `PlayerSaveData` с версионированием                |
| **Авторитетная экономика**  | Баланс фрагментов, транзакции, защита от манипуляций                        |
| **Авторитетные жизни**      | Пересчёт жизней по серверному таймеру                                       |
| **Валидация ответов**       | Повторная проверка ответов на уровнях (reconciliation)                      |
| **Контент (Remote Config)** | Раздача определений уровней, секторов, баланса, каталога магазина           |
| **Магазин**                 | Покупка товаров за фрагменты с серверной проверкой                          |
| **Аналитика**               | Приём и хранение игровых событий                                            |

### 1.2 Архитектурные принципы

- **Offline-first совместимость** — сервер обрабатывает запросы от клиентов, которые могли работать автономно длительное время. Все мутирующие эндпоинты поддерживают `Idempotency-Key`.
- **Server is source of truth** — для экономики (фрагменты), жизней (таймер), валидации ответов и статуса покупок. Для прогрессии действует принцип «прогресс всегда вперёд».
- **Stateless API** — серверные процессы не хранят состояние между запросами; состояние в БД и Redis.
- **Monolith-first** — начинаем с монолитного приложения. Разделение на микросервисы — только при реальной необходимости.
- **Слоёная архитектура** — слои: Domain (models, rules) → Services (use cases) → Infrastructure (DB, cache) → API (routers, middleware).

---

## 2. Технологический стек

| Компонент           | Технология                                              | Обоснование                                                                                               |
| ------------------- | ------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| **Язык**            | Python 3.12+                                            | Скорость разработки, богатая экосистема, async/await из коробки                                           |
| **Фреймворк**       | FastAPI 0.110+                                          | Высокая производительность (async на uvicorn), автогенерация OpenAPI, встроенная валидация через Pydantic |
| **ORM**             | SQLAlchemy 2.0+ (async)                                 | Зрелая ORM, async-движок, декларативные модели, поддержка PostgreSQL JSONB                                |
| **Миграции**        | Alembic                                                 | Стандарт для SQLAlchemy, code-first миграции, автогенерация                                               |
| **СУБД**            | PostgreSQL 16+                                          | Надёжность, JSONB для гибких данных, отличная производительность                                          |
| **Кеш / Сессии**    | Redis 7+ (через redis-py/aioredis)                      | In-memory кеш, хранение refresh-токенов, идемпотентность, rate limiting                                   |
| **Аутентификация**  | python-jose + passlib                                   | JWT генерация/валидация, хеширование токенов                                                              |
| **Валидация**       | Pydantic v2                                             | Встроена в FastAPI, автоматическая валидация request/response моделей                                     |
| **Логирование**     | structlog + Grafana Loki                                | Структурированное логирование, JSON-формат, поиск по контексту                                            |
| **Тестирование**    | pytest + pytest-asyncio + httpx + testcontainers-python | Async-тесты, мок-клиент, реальная БД в контейнере                                                         |
| **CI/CD**           | GitHub Actions                                          | Линтинг, тесты, деплой                                                                                    |
| **Контейнеризация** | Docker + Docker Compose                                 | Единообразие окружений, простой деплой                                                                    |
| **Хостинг**         | VPS / Cloud (Hetzner / DigitalOcean / Yandex Cloud)     | Стоимость, доступность в регионе                                                                          |
| **Обратный прокси** | Nginx / Caddy                                           | TLS termination, rate limiting, gzip                                                                      |
| **ASGI-сервер**     | Uvicorn + Gunicorn                                      | Uvicorn как ASGI worker, Gunicorn как process manager                                                     |

---

## 3. Высокоуровневая архитектура

```txt
┌─────────────────────────────────────────────────────────────┐
│                        КЛИЕНТЫ (Unity)                      │
│  Android / iOS — offline-first, локальный кеш, sync queue   │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTPS (TLS 1.2+)
                           ▼
┌──────────────────────────────────────────────────────────────┐
│                    REVERSE PROXY (Nginx/Caddy)               │
│  TLS termination · gzip · rate limiting · access logs        │
└──────────────────────────┬───────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│          FastAPI Application (Uvicorn/Gunicorn)              │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  API Layer (Routers)                                   │  │
│  │  Routers · Middleware · Dependencies · Pydantic DTOs   │  │
│  ├────────────────────────────────────────────────────────┤  │
│  │  SERVICE Layer (Use Cases)                             │  │
│  │  AuthService · SaveService · EconomyService            │  │
│  │  LivesService · LevelCheckService · ShopService        │  │
│  │  ContentService · AnalyticsService                     │  │
│  ├────────────────────────────────────────────────────────┤  │
│  │  DOMAIN Layer (Business Logic)                         │  │
│  │  Dataclasses · Enums · Business Rules                  │  │
│  │  Validation Engine · Star Rating Calculator            │  │
│  │  Merge Strategy · Economy Rules                        │  │
│  ├────────────────────────────────────────────────────────┤  │
│  │  INFRASTRUCTURE Layer                                  │  │
│  │  SQLAlchemy (PostgreSQL) · Redis · JWT Provider        │  │
│  │  External Auth (Google, Apple)                         │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────┬──────────────────────┬────────────────────────┘
               │                      │
               ▼                      ▼
      ┌─────────────────┐    ┌─────────────────┐
      │   PostgreSQL     │    │     Redis        │
      │                  │    │                  │
      │  Players         │    │  Access tokens   │
      │  Saves           │    │  Refresh tokens  │
      │  Transactions    │    │  Idempotency     │
      │  Content         │    │  Rate limits     │
      │  Analytics       │    │  Content cache   │
      └─────────────────┘    └─────────────────┘
```

### Поток данных в типичных сценариях

**Boot Flow (запуск клиента):**

```txt
Client → POST /auth/register     → AuthService → DB (players)
Client → GET /save               → SaveService → DB (saves)
Client → GET /content/manifest   → ContentService → Redis cache / DB
Client → GET /lives              → LivesService → DB + server time recalc
```

**Level Completion Flow:**

```txt
Client → POST /check/level       → LevelCheckService
                                      ├→ ValidationEngine (проверка ответа)
                                      ├→ EconomyService (начисление фрагментов)
                                      ├→ LivesService (списание жизни при ошибке)
                                      ├→ ProgressionLogic (разблокировка)
                                      └→ DB (атомарная запись всех изменений)
Client → PUT /save               → SaveService → DB
Client → POST /analytics/events  → AnalyticsService → DB (async)
```

---

## 4. Структура проекта (серверная кодовая база)

```txt
starfunc-server/
│
├── src/
│   └── app/
│       ├── __init__.py
│       ├── main.py                             # FastAPI app factory, lifespan, middleware
│       ├── config.py                           # Pydantic Settings (env vars)
│       ├── dependencies.py                     # FastAPI Depends — DB session, Redis, current_player
│       │
│       ├── domain/                             # Доменный слой (без зависимостей от фреймворка)
│       │   ├── __init__.py
│       │   ├── entities.py                     # Player, PlayerSave, Transaction, RefreshToken, AnalyticsEvent
│       │   ├── enums.py                        # TaskType, FunctionType, SectorState, LevelType, etc.
│       │   ├── models.py                       # PlayerSaveData, SectorProgress, LevelProgress, etc.
│       │   ├── content_models.py               # LevelDefinition, SectorDefinition, StarDefinition, etc.
│       │   ├── shop_models.py                  # ShopItemDefinition
│       │   ├── check_models.py                 # PlayerAnswer, CheckResult
│       │   │
│       │   └── rules/
│       │       ├── __init__.py
│       │       ├── validation_engine.py        # Ядро валидации ответов (все 6 типов)
│       │       ├── star_rating.py              # Расчёт звёздного рейтинга
│       │       ├── progression.py              # Правила разблокировки секторов/уровней
│       │       ├── economy.py                  # Бизнес-правила экономики
│       │       ├── lives.py                    # Бизнес-правила жизней
│       │       └── save_merger.py              # Стратегия мержа при конфликтах
│       │
│       ├── services/                           # Прикладной слой (use cases)
│       │   ├── __init__.py
│       │   ├── auth_service.py                 # Регистрация, refresh, link
│       │   ├── save_service.py                 # CRUD облачных сохранений + мерж
│       │   ├── economy_service.py              # Транзакции фрагментов
│       │   ├── lives_service.py                # Пересчёт и управление жизнями
│       │   ├── level_check_service.py          # Серверная проверка ответов
│       │   ├── shop_service.py                 # Каталог и покупки
│       │   ├── content_service.py              # Раздача контента уровней/секторов
│       │   └── analytics_service.py            # Приём аналитических событий
│       │
│       ├── api/                                # Presentation слой (роутеры)
│       │   ├── __init__.py
│       │   ├── routers/
│       │   │   ├── __init__.py
│       │   │   ├── auth.py                     # POST /register, /refresh, /link
│       │   │   ├── save.py                     # GET /, PUT /
│       │   │   ├── economy.py                  # GET /balance, POST /transaction
│       │   │   ├── lives.py                    # GET /, POST /restore, /restore-all
│       │   │   ├── shop.py                     # GET /items, POST /purchase
│       │   │   ├── content.py                  # GET /manifest, /sectors, /levels, /balance
│       │   │   ├── check.py                    # POST /level
│       │   │   ├── analytics.py                # POST /events
│       │   │   └── health.py                   # GET /health, GET /health/ready
│       │   │
│       │   ├── middleware/
│       │   │   ├── __init__.py
│       │   │   ├── exception_handler.py        # Глобальная обработка исключений → ApiErrorResponse
│       │   │   ├── request_logging.py          # Логирование запросов/ответов
│       │   │   ├── idempotency.py              # Обработка Idempotency-Key
│       │   │   ├── rate_limiting.py            # Rate limiting per player/IP
│       │   │   ├── client_info.py              # Извлечение X-Client-Version и X-Platform
│       │   │   └── server_time.py              # Добавление serverTime в ответы
│       │   │
│       │   └── schemas/                        # Pydantic модели (request/response DTOs)
│       │       ├── __init__.py
│       │       ├── common.py                   # ApiResponse, ApiErrorResponse
│       │       ├── auth.py                     # RegisterRequest, AuthResponse, etc.
│       │       ├── save.py                     # SaveRequest, SaveResponse
│       │       ├── economy.py                  # TransactionRequest, BalanceResponse
│       │       ├── lives.py                    # LivesResponse, RestoreLifeResponse
│       │       ├── shop.py                     # ShopItemsResponse, PurchaseRequest
│       │       ├── content.py                  # ContentManifestResponse, SectorResponse
│       │       ├── check.py                    # CheckLevelRequest, CheckLevelResponse
│       │       └── analytics.py                # AnalyticsEventsRequest, AnalyticsResponse
│       │
│       └── infrastructure/                     # Инфраструктурный слой
│           ├── __init__.py
│           ├── database.py                     # SQLAlchemy async engine, session factory
│           ├── redis.py                        # Redis connection pool и helper-функции
│           │
│           ├── persistence/
│           │   ├── __init__.py
│           │   ├── models.py                   # SQLAlchemy ORM-модели (Table mapping)
│           │   ├── player_repo.py              # PlayerRepository
│           │   ├── save_repo.py                # SaveRepository
│           │   ├── transaction_repo.py         # TransactionRepository
│           │   ├── content_repo.py             # ContentRepository
│           │   └── analytics_repo.py           # AnalyticsRepository
│           │
│           ├── cache/
│           │   ├── __init__.py
│           │   ├── idempotency_store.py        # Redis: хранение Idempotency-Key (24h TTL)
│           │   ├── token_store.py              # Redis: refresh-токены
│           │   ├── rate_limiter.py             # Redis: rate limiting
│           │   └── content_cache.py            # Redis: кеш контента
│           │
│           └── auth/
│               ├── __init__.py
│               ├── jwt_provider.py             # Генерация и валидация JWT (python-jose)
│               ├── google_verifier.py          # Верификация Google Play Games токенов
│               └── apple_verifier.py           # Верификация Apple Game Center токенов
│
├── alembic/                                    # Alembic миграции
│   ├── alembic.ini
│   ├── env.py
│   └── versions/
│       └── ...
│
├── seed/                                       # Seed data (начальный контент)
│   ├── seed_content.py                         # Скрипт заполнения content_versions
│   └── data/
│       ├── sectors.json
│       ├── levels/
│       │   ├── sector_1.json
│       │   ├── sector_2.json
│       │   ├── sector_3.json
│       │   ├── sector_4.json
│       │   └── sector_5.json
│       ├── balance.json
│       └── shop_catalog.json
│
├── tests/
│   ├── conftest.py                             # Fixtures: DB, Redis, httpx AsyncClient
│   ├── unit/
│   │   ├── test_validation_engine.py
│   │   ├── test_star_rating.py
│   │   ├── test_economy_rules.py
│   │   ├── test_lives_rules.py
│   │   ├── test_progression_rules.py
│   │   └── test_save_merger.py
│   │
│   ├── service/
│   │   ├── test_auth_service.py
│   │   ├── test_level_check_service.py
│   │   ├── test_economy_service.py
│   │   └── test_shop_service.py
│   │
│   └── integration/
│       ├── test_auth_endpoints.py
│       ├── test_save_endpoints.py
│       ├── test_check_level_endpoints.py
│       └── test_economy_endpoints.py
│
├── pyproject.toml                              # Зависимости (uv / poetry), linting (ruff), etc.
├── Dockerfile
├── docker-compose.yml                          # PostgreSQL + Redis + App
├── docker-compose.override.yml                 # Dev overrides
├── .env.example                                # Шаблон переменных окружения
└── README.md
```

---

## 5. Слой данных (Database Layer)

### 5.1 Схема базы данных

#### Таблица `players`

Основная таблица игроков.

```sql
CREATE TABLE players (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id       UUID NOT NULL UNIQUE,
    platform        VARCHAR(10) NOT NULL CHECK (platform IN ('android', 'ios')),
    client_version  VARCHAR(20) NOT NULL,

    -- Привязка сторонних аккаунтов
    google_play_id  VARCHAR(255) UNIQUE,
    apple_gc_id     VARCHAR(255) UNIQUE,
    display_name    VARCHAR(100),

    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_players_device_id ON players(device_id);
```

#### Таблица `player_saves`

Облачные сохранения. Одна актуальная запись на игрока.

```sql
CREATE TABLE player_saves (
    id              BIGSERIAL PRIMARY KEY,
    player_id       UUID NOT NULL REFERENCES players(id),
    version         INT NOT NULL DEFAULT 1,
    save_version    INT NOT NULL DEFAULT 1,

    -- Сохранение хранится целиком как JSONB
    save_data       JSONB NOT NULL,

    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (player_id)
);

CREATE INDEX idx_player_saves_player ON player_saves(player_id);
```

> **Обоснование JSONB**: `PlayerSaveData` содержит вложенные словари (`sectorProgress`, `levelProgress`, `consumables`, `ownedItems`), структура которых может меняться. JSONB позволяет хранить всё сохранение атомарно и менять формат без миграций. Для частых запросов (баланс фрагментов, жизни) создаются индексы по JSONB-путям.

```sql
-- Индексы для частых выборок по JSONB
CREATE INDEX idx_saves_fragments ON player_saves ((save_data->>'totalFragments'));
CREATE INDEX idx_saves_lives ON player_saves ((save_data->>'currentLives'));
```

#### Таблица `transactions`

Лог всех экономических операций (аудит-трейл).

```sql
CREATE TABLE transactions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    player_id       UUID NOT NULL REFERENCES players(id),
    type            VARCHAR(10) NOT NULL CHECK (type IN ('earn', 'spend')),
    amount          INT NOT NULL CHECK (amount > 0),
    reason          VARCHAR(50) NOT NULL,
    reference_id    VARCHAR(100),
    previous_bal    INT NOT NULL,
    new_bal         INT NOT NULL,
    idempotency_key UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_transactions_player ON transactions(player_id, created_at DESC);
CREATE INDEX idx_transactions_idempotency ON transactions(idempotency_key) WHERE idempotency_key IS NOT NULL;
```

#### Таблица `refresh_tokens`

Refresh-токены для ротации при каждом использовании.

```sql
CREATE TABLE refresh_tokens (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    player_id       UUID NOT NULL REFERENCES players(id),
    token_hash      VARCHAR(128) NOT NULL UNIQUE,    -- SHA-256 хэш токена
    expires_at      TIMESTAMPTZ NOT NULL,
    is_revoked      BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    replaced_by_id  UUID REFERENCES refresh_tokens(id)
);

CREATE INDEX idx_refresh_tokens_player ON refresh_tokens(player_id);
CREATE INDEX idx_refresh_tokens_hash ON refresh_tokens(token_hash);
```

#### Таблица `content_versions`

Версионирование контента (remote config).

```sql
CREATE TABLE content_versions (
    id              SERIAL PRIMARY KEY,
    content_type    VARCHAR(50) NOT NULL,   -- 'sector', 'level', 'balance', 'shop_catalog'
    content_id      VARCHAR(100),           -- e.g. 'sector_1' (NULL для глобальных)
    version         INT NOT NULL DEFAULT 1,
    data            JSONB NOT NULL,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_content_type ON content_versions(content_type, content_id) WHERE is_active = TRUE;
```

#### Таблица `analytics_events`

Буфер аналитических событий. Рассчитана на bulk-insert.

```sql
CREATE TABLE analytics_events (
    id              BIGSERIAL PRIMARY KEY,
    player_id       UUID NOT NULL,
    session_id      UUID,
    event_name      VARCHAR(50) NOT NULL,
    params          JSONB,
    client_ts       BIGINT NOT NULL,       -- Unix timestamp от клиента
    server_ts       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Партицирование по месяцам для больших объёмов
-- CREATE TABLE analytics_events ... PARTITION BY RANGE (created_at);

CREATE INDEX idx_analytics_player ON analytics_events(player_id, created_at DESC);
CREATE INDEX idx_analytics_event ON analytics_events(event_name, created_at DESC);
```

### 5.2 Redis — кеш и сессии

| Ключ                         | TTL    | Назначение                                         |
| ---------------------------- | ------ | -------------------------------------------------- |
| `idempotency:{key}`          | 24h    | Ответ на мутирующий запрос с Idempotency-Key       |
| `rate:{playerId}:{endpoint}` | 1 min  | Счётчик запросов для rate limiting                 |
| `rate:ip:{ip}:{endpoint}`    | 1 min  | Счётчик запросов по IP (для auth)                  |
| `content:manifest`           | 5 min  | Кеш манифеста контента                             |
| `content:sector:{sectorId}`  | 10 min | Кеш определения сектора                            |
| `content:levels:{sectorId}`  | 10 min | Кеш уровней сектора                                |
| `content:balance`            | 10 min | Кеш глобального баланса                            |
| `content:shop`               | 10 min | Кеш каталога магазина                              |
| `player:balance:{playerId}`  | 5 min  | Кеш баланса игрока (инвалидируется при транзакции) |

---

## 6. Доменный слой (Domain Layer)

### 6.1 Модели предметной области

#### Player (SQLAlchemy ORM model)

```python
# infrastructure/persistence/models.py

class PlayerModel(Base):
    __tablename__ = "players"

    id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    device_id: Mapped[uuid.UUID] = mapped_column(unique=True)
    platform: Mapped[str] = mapped_column(String(10))       # "android" | "ios"
    client_version: Mapped[str] = mapped_column(String(20))

    # Привязки
    google_play_id: Mapped[str | None] = mapped_column(String(255), unique=True)
    apple_gc_id: Mapped[str | None] = mapped_column(String(255), unique=True)
    display_name: Mapped[str | None] = mapped_column(String(100))

    created_at: Mapped[datetime] = mapped_column(server_default=func.now())
    updated_at: Mapped[datetime] = mapped_column(server_default=func.now(), onupdate=func.now())
```

#### PlayerSaveData (Domain dataclass)

Зеркалирует клиентскую модель (см. [API.md §5.1](API.md#51-playersavedata-облачное-сохранение)):

```python
# domain/models.py

@dataclass
class SectorProgress:
    state: SectorState                      # Locked | Available | InProgress | Completed
    stars_collected: int = 0
    control_passed: bool = False

@dataclass
class LevelProgress:
    is_completed: bool = False
    best_stars: int = 0
    best_time: float = 0.0
    attempts: int = 0

@dataclass
class PlayerSaveData:
    save_version: int = 1
    version: int = 1
    last_modified: int = 0

    # Прогрессия
    current_sector_index: int = 0
    sector_progress: dict[str, SectorProgress] = field(default_factory=dict)
    level_progress: dict[str, LevelProgress] = field(default_factory=dict)

    # Экономика
    total_fragments: int = 0

    # Жизни
    current_lives: int = 5
    last_life_restore_timestamp: int = 0

    # Магазин
    owned_items: list[str] = field(default_factory=list)
    consumables: dict[str, int] = field(default_factory=dict)

    # Статистика
    total_levels_completed: int = 0
    total_stars_collected: int = 0
    total_play_time: float = 0.0
```

### 6.2 Бизнес-правила

#### ValidationEngine

Ядро серверной валидации ответов. Реализует проверку для всех 6 типов заданий:

```python
# domain/rules/validation_engine.py

class ValidationEngine:
    """Проверяет ответ игрока на уровне.
    Возвращает CheckResult с is_valid, match_percentage, errors.
    """

    def validate(self, level: LevelDefinition, answer: PlayerAnswer) -> CheckResult:
        ...

    # Внутренние методы по типам:
    def _validate_choose_option(self, level: LevelDefinition, selected_option_id: str) -> CheckResult:
        ...

    def _validate_function(
        self, level: LevelDefinition, function_type: FunctionType, coefficients: list[float]
    ) -> CheckResult:
        ...

    def _validate_identify_stars(self, level: LevelDefinition, selected_star_ids: list[str]) -> CheckResult:
        ...

    def _validate_place_stars(
        self, level: LevelDefinition, placements: list[StarPlacement], threshold: float
    ) -> CheckResult:
        ...
```

#### StarRatingCalculator

```python
# domain/rules/star_rating.py

class StarRatingCalculator:
    """Рассчитывает количество звёзд (0-3) на основе ошибок и времени."""

    def calculate(self, config: StarRatingConfig, error_count: int, elapsed_time: float) -> int:
        ...
```

Правила:

- `error_count <= three_star_max_errors` → 3 звезды
- `error_count <= two_star_max_errors` → 2 звезды
- `error_count <= one_star_max_errors` → 1 звезда
- Иначе → 0 звёзд (уровень не пройден)
- Если `timer_affects_rating and elapsed_time > three_star_max_time` → снижение на 1 звезду

#### ProgressionRules

```python
# domain/rules/progression.py

class ProgressionRules:
    def is_level_unlocked(self, save: PlayerSaveData, level: LevelDefinition) -> bool:
        """Проверяет, разблокирован ли следующий уровень.
        Бонусные уровни (type=Bonus) опциональны: игрок может пропустить их
        и перейти к следующему обязательному уровню.
        """
        ...

    def can_unlock_sector(self, save: PlayerSaveData, sector: SectorDefinition) -> bool:
        """Проверяет, можно ли разблокировать следующий сектор.
        Условия: пройден контрольный уровень (index 18) + порог звёзд.
        Звёзды бонусных уровней НЕ учитываются в пороге разблокировки.
        """
        ...

    def get_unlocked_levels(self, save: PlayerSaveData, completed_level_id: str) -> list[str]:
        """Возвращает список вновь разблокированных уровней после прохождения."""
        ...

    def get_unlocked_sectors(self, save: PlayerSaveData) -> list[str]:
        """Возвращает список вновь разблокированных секторов."""
        ...
```

#### EconomyRules

```python
# domain/rules/economy.py

class EconomyRules:
    def calculate_level_reward(self, level: LevelDefinition, stars: int) -> int:
        """Рассчитывает фрагменты за первичное прохождение уровня."""
        ...

    def calculate_improvement_bonus(self, old_stars: int, new_stars: int, config: BalanceConfig) -> int:
        """Рассчитывает бонус за улучшение результата (повторное прохождение).
        Формула: (new_stars - old_stars) * improvement_bonus_per_star.
        """
        ...

    def validate_transaction(self, tx_type: TransactionType, amount: int, current_balance: int) -> bool:
        """Проверяет валидность транзакции."""
        ...
```

#### SaveMerger

Реализует стратегию «прогресс всегда вперёд» (см. [API.md §8.2](API.md#82-алгоритм-мержа-клиент)):

```python
# domain/rules/save_merger.py

class SaveMerger:
    def merge(self, local: PlayerSaveData, server: PlayerSaveData) -> PlayerSaveData:
        """Мержит локальное и серверное сохранения.
        Для прогрессии — max. Для экономики — серверное значение.
        """
        ...
```

---

## 7. Сервисный слой (Service Layer)

### 7.1 AuthService

```python
# services/auth_service.py

class AuthService:
    async def register(self, request: RegisterRequest, session: AsyncSession) -> AuthResponse:
        """Регистирует устройство (или возвращает существующего игрока).
        Идемпотентно по device_id.
        """
        ...

    async def refresh(self, request: RefreshRequest, session: AsyncSession) -> AuthResponse:
        """Обновляет access token по refresh token.
        Ротация: старый refresh-токен отзывается, выдаётся новый.
        """
        ...

    async def link_account(
        self, player_id: uuid.UUID, request: LinkAccountRequest, session: AsyncSession
    ) -> LinkResponse:
        """Привязывает сторонний аккаунт."""
        ...
```

**Логика `register`:**

1. Проверить `device_id` в базе.
2. Если существует — выдать новые токены для существующего игрока.
3. Если нет — создать запись `Player`, сгенерировать пару токенов.
4. Вернуть `player_id`, `access_token`, `refresh_token`, `expires_in`.

**Логика `refresh`:**

1. Найти refresh-токен по хешу.
2. Проверить: не отозван, не истёк.
3. Отозвать текущий токен.
4. Сгенерировать новый access + refresh.
5. Сохранить новый refresh-токен с ссылкой `replaced_by_id` на старый.
6. При обнаружении повторного использования отозванного токена — отозвать всю цепочку (защита от кражи).

### 7.2 SaveService

```python
# services/save_service.py

class SaveService:
    async def get_save(self, player_id: uuid.UUID, session: AsyncSession) -> SaveResponse:
        """Загрузить облачное сохранение."""
        ...

    async def put_save(
        self, player_id: uuid.UUID, request: SaveRequest, session: AsyncSession
    ) -> SaveUpdateResponse:
        """Записать сохранение с оптимистичной блокировкой.
        Проверяет expected_version; при несовпадении возвращает 409 SAVE_CONFLICT.
        """
        ...
```

**Логика `put_save`:**

1. Загрузить текущую версию из БД.
2. Если `expected_version != current_version` → вернуть `409 SAVE_CONFLICT` с серверным сохранением.
3. Если совпадает → записать новое сохранение с `version = expected_version + 1`.
4. Обновить `updated_at`.

### 7.3 EconomyService

```python
# services/economy_service.py

class EconomyService:
    async def get_balance(self, player_id: uuid.UUID, session: AsyncSession) -> int:
        """Получить текущий баланс фрагментов."""
        ...

    async def execute_transaction(
        self, player_id: uuid.UUID, request: TransactionRequest, session: AsyncSession
    ) -> TransactionResponse:
        """Провести транзакцию (earn/spend).
        Атомарно: проверка баланса + обновление + запись в лог.
        """
        ...
```

**Логика `execute_transaction`:**

1. Проверить Idempotency-Key (если повтор → вернуть сохранённый ответ).
2. Получить текущий баланс из `player_saves.save_data->'totalFragments'`.
3. Для `spend` — проверить `balance >= amount`; если нет → `422 INSUFFICIENT_FUNDS`.
4. Обновить баланс в `player_saves.save_data`.
5. **Если `reason == "skip_level"`:**
   a. Загрузить `LevelDefinition` по `reference_id` (levelId).
   b. Обновить `level_progress`: `is_completed=True`, `best_stars=1`, `best_time=0`, `fragments_earned=0`.
   c. Обновить `sector_progress`: `stars_collected += 1`, state-переходы (аналогично `LevelCheckService` шаг 5d).
   d. Обновить `total_levels_completed += 1`, `total_stars_collected += 1`.
   e. Проверить разблокировку (ProgressionRules).
   f. Инкрементировать save version.
6. Записать запись в `transactions`.
7. Вернуть `previous_balance`, `new_balance`, `transaction_id`, и для `skip_level` — `progress_update`.
8. Инвалидировать Redis-кеш баланса.

> Вся операция выполняется в одной БД-транзакции с `SELECT ... FOR UPDATE` на строке `player_saves` для предотвращения race conditions.

### 7.4 LivesService

```python
# services/lives_service.py

class LivesService:
    async def get_lives(self, player_id: uuid.UUID, session: AsyncSession) -> LivesResponse:
        """Получить текущее состояние жизней (с пересчётом по серверному времени)."""
        ...

    async def restore_one(self, player_id: uuid.UUID, session: AsyncSession) -> RestoreLifeResponse:
        """Восстановить одну жизнь за фрагменты."""
        ...

    async def restore_all(self, player_id: uuid.UUID, session: AsyncSession) -> RestoreAllResponse:
        """Восстановить все жизни за фрагменты."""
        ...
```

**Пересчёт жизней (при каждом запросе):**

```py
elapsed_seconds = server_now - last_life_restore_timestamp
restored_lives = floor(elapsed_seconds / restore_interval_seconds)
new_lives = min(current_lives + restored_lives, max_lives)

if new_lives > current_lives:
    update last_life_restore_timestamp += restored_lives * restore_interval_seconds
    update current_lives = new_lives

seconds_until_next = restore_interval_seconds - (elapsed_seconds % restore_interval_seconds)
    if current_lives == max_lives: seconds_until_next = 0
```

### 7.5 LevelCheckService

Самый сложный сервис. Атомарно проверяет ответ, начисляет/списывает ресурсы, обновляет прогрессию.

```python
# services/level_check_service.py

class LevelCheckService:
    async def check(
        self, player_id: uuid.UUID, request: CheckLevelRequest, session: AsyncSession
    ) -> CheckLevelResponse:
        """Серверная проверка ответа на уровне.
        Атомарная операция: валидация → экономика → жизни → прогрессия.
        """
        ...
```

**Полный алгоритм `check`:**

```txt
1. Загрузить LevelDefinition по level_id из content-таблицы
2. Загрузить PlayerSaveData (с блокировкой строки — SELECT ... FOR UPDATE)
3. Пересчитать жизни по серверному времени (LivesRules)
3a. Если current_lives == 0 после пересчёта → вернуть 422 NO_LIVES (попытка не засчитывается)
4. Вызвать ValidationEngine.validate(level, answer)

5. ЕСЛИ ответ верный:
   a. Рассчитать звёзды (StarRatingCalculator)
   b. Рассчитать фрагменты:
      - Если первое прохождение → level.fragment_reward
      - Если улучшение → EconomyRules.calculate_improvement_bonus()
      - Если результат хуже или равен → 0
   c. Обновить level_progress: is_completed=True, best_stars=max, best_time=min
   d. Обновить sector_progress: stars_collected, state
      - Если это первый пройденный уровень в секторе и state == Available → state = InProgress
      - Если пройден финальный уровень (index 19, type=Final) → state = Completed
      - Звёзды бонусных уровней (type=Bonus) не учитываются в пороге разблокировки сектора
   e. Обновить total_fragments += fragments_earned
   f. Проверить разблокировку (ProgressionRules)
   g. Обновить total_levels_completed, total_stars_collected
   h. Инкрементировать save version

6. ЕСЛИ ответ неверный:
   a. Списать одну жизнь (current_lives -= 1)
   b. Обновить last_life_restore_timestamp (если жизни были полные)
   c. Проверить провал уровня:
      - current_lives == 0 → level_failed=True, fail_reason="no_lives"
      - max_attempts > 0 and attempt >= max_attempts → level_failed=True, fail_reason="max_attempts_reached"
   d. Инкрементировать save version

7. Записать обновлённый PlayerSaveData в БД
8. Вернуть ответ с result + progress_update/lives_update + new_save_version
```

### 7.6 ShopService

```python
# services/shop_service.py

class ShopService:
    async def get_items(self, session: AsyncSession) -> ShopItemsResponse:
        """Каталог товаров."""
        ...

    async def purchase(
        self, player_id: uuid.UUID, request: PurchaseRequest, session: AsyncSession
    ) -> PurchaseResponse:
        """Покупка товара за фрагменты."""
        ...
```

**Логика `purchase`:**

1. Найти товар по `item_id` в каталоге.
2. Если `not is_consumable and item already owned` → `400 INVALID_REQUEST`.
3. Определить цену: серверная цена для online-покупок. Для offline-покупок (sync queue) используется `cached_price` — цена, которую видел игрок на момент покупки.
4. Списать фрагменты через `EconomyService` → `422 INSUFFICIENT_FUNDS` при нехватке.
5. Обновить `owned_items` или `consumables` в `PlayerSaveData`.
6. Вернуть результат.

### 7.7 ContentService

```python
# services/content_service.py

class ContentService:
    async def get_manifest(self, session: AsyncSession) -> ContentManifestResponse:
        """Манифест контента с версиями."""
        ...

    async def get_sectors(self, session: AsyncSession) -> SectorsResponse:
        """Все секторы."""
        ...

    async def get_sector(self, sector_id: str, session: AsyncSession) -> SectorResponse:
        """Один сектор."""
        ...

    async def get_levels(self, sector_id: str, session: AsyncSession) -> LevelsResponse:
        """Уровни сектора."""
        ...

    async def get_level(self, level_id: str, session: AsyncSession) -> LevelResponse:
        """Один уровень."""
        ...

    async def get_balance_config(self, session: AsyncSession) -> BalanceConfigResponse:
        """Глобальные настройки баланса."""
        ...
```

Все ответы кешируются в Redis. При обновлении контента через админ-панель или миграцию — инвалидация кеша.

### 7.8 AnalyticsService

```python
# services/analytics_service.py

class AnalyticsService:
    async def ingest_events(
        self, player_id: uuid.UUID, request: AnalyticsEventsRequest, session: AsyncSession
    ) -> AnalyticsResponse:
        """Батчевый приём аналитических событий.
        Асинхронная обработка (202 Accepted).
        """
        ...
```

**Логика:**

1. Валидация: проверка `event_name` по белому списку, максимум 100 событий в батче.
2. Bulk-insert в `analytics_events`.
3. Возвращает количество принятых / отклонённых.

---

## 8. API-слой (Router Layer)

### 8.1 Middleware pipeline

Запрос проходит через цепочку middleware в следующем порядке:

```txt
Request
  │
  ├─ 1. RequestLoggingMiddleware      (логирование входящего запроса)
  ├─ 2. ExceptionHandlerMiddleware    (глобальный try-catch → ApiErrorResponse)
  ├─ 3. RateLimitingMiddleware        (проверка лимитов по player_id / IP)
  ├─ 4. ClientInfoMiddleware          (извлечение X-Client-Version и X-Platform из заголовков)
  ├─ 5. Authentication (JWT Bearer)   (FastAPI Depends + jwt_provider)
  ├─ 6. IdempotencyMiddleware         (проверка Idempotency-Key для POST/PUT)
  ├─ 7. Pydantic validation           (автоматическая валидация request body)
  ├─ 8. ServerTimeMiddleware          (добавление serverTime в ответ)
  │
  └─ Router endpoint
```

В FastAPI middleware реализуется через `@app.middleware("http")` или через `BaseHTTPMiddleware`, а аутентификация — через `Depends()`:

```python
# dependencies.py

async def get_current_player(
    token: str = Depends(oauth2_scheme),
    jwt: JwtProvider = Depends(get_jwt_provider),
    session: AsyncSession = Depends(get_session),
) -> uuid.UUID:
    """FastAPI dependency: извлекает player_id из JWT."""
    payload = jwt.decode(token)
    return uuid.UUID(payload["sub"])
```

### 8.2 Роутеры

Маппинг 1:1 с эндпоинтами из [API.md §6](API.md#6-эндпоинты):

| Роутер             | Маршрут             | Методы                                                                                                               |
| ------------------ | ------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `auth.router`      | `/api/v1/auth`      | `POST /register`, `POST /refresh`, `POST /link`                                                                      |
| `save.router`      | `/api/v1/save`      | `GET /`, `PUT /`                                                                                                     |
| `economy.router`   | `/api/v1/economy`   | `GET /balance`, `POST /transaction`                                                                                  |
| `lives.router`     | `/api/v1/lives`     | `GET /`, `POST /restore`, `POST /restore-all`                                                                        |
| `shop.router`      | `/api/v1/shop`      | `GET /items`, `POST /purchase`                                                                                       |
| `content.router`   | `/api/v1/content`   | `GET /manifest`, `GET /sectors`, `GET /sectors/{id}`, `GET /sectors/{id}/levels`, `GET /levels/{id}`, `GET /balance` |
| `check.router`     | `/api/v1/check`     | `POST /level`                                                                                                        |
| `analytics.router` | `/api/v1/analytics` | `POST /events`                                                                                                       |
| `health.router`    | `/api/v1/health`    | `GET /`, `GET /ready`                                                                                                |

Пример регистрации в `main.py`:

```python
from fastapi import FastAPI
from app.api.routers import auth, save, economy, lives, shop, content, check, analytics, health

app = FastAPI(title="STAR FUNC API", version="1.0.0")

app.include_router(auth.router, prefix="/api/v1/auth", tags=["auth"])
app.include_router(save.router, prefix="/api/v1/save", tags=["save"])
app.include_router(economy.router, prefix="/api/v1/economy", tags=["economy"])
app.include_router(lives.router, prefix="/api/v1/lives", tags=["lives"])
app.include_router(shop.router, prefix="/api/v1/shop", tags=["shop"])
app.include_router(content.router, prefix="/api/v1/content", tags=["content"])
app.include_router(check.router, prefix="/api/v1/check", tags=["check"])
app.include_router(analytics.router, prefix="/api/v1/analytics", tags=["analytics"])
app.include_router(health.router, prefix="/api/v1/health", tags=["health"])
```

### 8.3 Идемпотентность

Все мутирующие эндпоинты (`POST`, `PUT`) поддерживают заголовок `Idempotency-Key: <uuid>`.

**Алгоритм (IdempotencyMiddleware):**

1. Извлечь `Idempotency-Key` из заголовка.
2. Проверить в Redis: `GET idempotency:{key}`.
3. Если найден — вернуть сохранённый ответ (HTTP-код + тело).
4. Если нет — выполнить запрос, сохранить ответ в Redis с TTL 24h.
5. Ключ привязывается к `player_id` + `endpoint` для дополнительной безопасности.

> **Примечание:** Идемпотентность реализована только через Redis (TTL 24h). Хранение в базе данных не используется.

---

## 9. Аутентификация и авторизация

### 9.1 JWT-токены

| Параметр         | Значение                                   |
| ---------------- | ------------------------------------------ |
| Алгоритм         | HS256 (или RS256 для production)           |
| Issuer           | `starfunc-api`                             |
| Audience         | `starfunc-client`                          |
| TTL Access Token | 1 час                                      |
| Payload Claims   | `sub` (playerId), `iat`, `exp`, `platform` |

```json
{
  "sub": "player_abc123",
  "iat": 1711700000,
  "exp": 1711703600,
  "platform": "android",
  "iss": "starfunc-api",
  "aud": "starfunc-client"
}
```

Реализация через `python-jose`:

```python
# infrastructure/auth/jwt_provider.py

from jose import jwt

class JwtProvider:
    def __init__(self, settings: Settings):
        self._secret = settings.jwt_secret
        self._algorithm = "HS256"
        self._access_ttl = timedelta(minutes=settings.jwt_access_token_expire_minutes)

    def create_access_token(self, player_id: uuid.UUID, platform: str) -> str:
        now = datetime.now(UTC)
        payload = {
            "sub": str(player_id),
            "platform": platform,
            "iat": now,
            "exp": now + self._access_ttl,
            "iss": "starfunc-api",
            "aud": "starfunc-client",
        }
        return jwt.encode(payload, self._secret, algorithm=self._algorithm)

    def decode(self, token: str) -> dict:
        return jwt.decode(
            token, self._secret,
            algorithms=[self._algorithm],
            audience="starfunc-client",
            issuer="starfunc-api",
        )
```

### 9.2 Refresh-токены

- **Формат:** JWT.
- **Хранение на сервере:** SHA-256 хеш токена в таблице `refresh_tokens` (оригинал никогда не хранится на сервере).
- **Ротация:** каждый `POST /auth/refresh` отзывает старый токен и выдаёт новый.
- **TTL:** 90 дней.
- **Детекция кражи:** если клиент пытается использовать уже отозванный токен → отзыв всей цепочки для этого игрока (forced re-auth).

### 9.3 Привязка сторонних аккаунтов

```txt
POST /auth/link
  │
  ├─ provider = "google_play"
  │   └─ Верифицировать provider_token через Google Play Games API
  │      └─ Получить google_play_id
  │         └─ Проверить уникальность
  │            └─ Записать в players.google_play_id
  │
  └─ provider = "apple_game_center"
      └─ Верифицировать подпись через Apple Verification API
         └─ Получить apple_gc_id
            └─ Проверить уникальность
               └─ Записать в players.apple_gc_id
```

При попытке привязать аккаунт, уже связанный с другим `player_id` → `409 ACCOUNT_ALREADY_LINKED`.

---

## 10. Валидация ответов (Level Check)

### 10.1 Алгоритм серверной проверки

Сервер **не доверяет** клиенту:

- Количество заработанных фрагментов
- Статус жизней
- Правильность ответа

Сервер доверяет клиенту (информационные поля):

- `elapsed_time` — время прохождения
- `errors_before_submit` — количество ошибок до отправки (используется для расчёта звёздного рейтинга)
- `attempt` — номер попытки

### 10.2 Правила по типам заданий

| TaskType               | Метод валидации            | Логика                                                               |
| ---------------------- | -------------------------- | -------------------------------------------------------------------- |
| `ChooseCoordinate`     | `_validate_choose_option`  | Проверка `selected_option_id` против `answer_options[].is_correct`   |
| `ChooseFunction`       | `_validate_choose_option`  | Аналогично — проверка ID выбранного варианта                         |
| `AdjustGraph`          | `_validate_function`       | Сравнение коэффициентов с эталоном, допуск `accuracy_threshold`      |
| `BuildFunction`        | `_validate_function`       | Сравнение через среднеквадратичное отклонение на контрольных точках  |
| `IdentifyError`        | `_validate_identify_stars` | Проверка, что выбранные `star_ids` совпадают с `is_distractor=True`  |
| `RestoreConstellation` | `_validate_place_stars`    | Проверка координат размещённых звёзд с допуском `accuracy_threshold` |

**Валидация функций (AdjustGraph / BuildFunction):**

```txt
1. Получить эталонную функцию из level.reference_functions[0]
2. Получить ответ игрока: function_type + coefficients
3. Вычислить значения обеих функций на N контрольных точках (stars с belongs_to_solution=True)
4. Рассчитать среднеквадратичное отклонение:
   rmsd = sqrt(sum((y_player - y_reference) ** 2) / n)
5. Если rmsd <= accuracy_threshold → is_valid=True, match_percentage = 1 - rmsd / max_rmsd
6. Иначе → is_valid=False
```

### 10.3 Звёздный рейтинг

```py
errors_for_rating = errors_before_submit + (0 if is_valid else 1)

stars = star_rating_calculator.calculate(level.star_rating, errors_for_rating, elapsed_time)
```

Правила начисления (из `StarRatingConfig`):

- 3 звезды: `errors_for_rating <= three_star_max_errors` (обычно 0)
- 2 звезды: `errors_for_rating <= two_star_max_errors`
- 1 звезда: `errors_for_rating <= one_star_max_errors`
- 0 звёзд: уровень не пройден (но может не быть «провалом» — зависит от жизней и попыток)

### 10.4 Провал уровня

Уровень считается **проваленным** (`level_failed=True`), если:

| Условие                                              | `fail_reason`            |
| ---------------------------------------------------- | ------------------------ |
| После списания жизни `current_lives == 0`            | `"no_lives"`             |
| `level.max_attempts > 0 and attempt >= max_attempts` | `"max_attempts_reached"` |

При провале клиент показывает экран поражения. Для продолжения нужно: восстановить жизни (ожидание / фрагменты) или повторить позже.

---

## 11. Экономика и транзакции

### Атомарность

Каждая транзакция выполняется внутри SQL-транзакции с `SELECT ... FOR UPDATE`:

```sql
BEGIN;
  SELECT save_data FROM player_saves WHERE player_id = $1 FOR UPDATE;
  -- проверка баланса
  -- обновление save_data->'totalFragments'
  INSERT INTO transactions (...) VALUES (...);
COMMIT;
```

В SQLAlchemy async — через `session.execute()` с `with_for_update()`:

```python
stmt = (
    select(PlayerSaveModel)
    .where(PlayerSaveModel.player_id == player_id)
    .with_for_update()
)
save = (await session.execute(stmt)).scalar_one()
```

### Источники и расходы фрагментов

| Операция             | Тип   | reason              | Описание                                                   |
| -------------------- | ----- | ------------------- | ---------------------------------------------------------- |
| Прохождение уровня   | earn  | `level_completion`  | Через `POST /check/level`, не через `/economy/transaction` |
| Улучшение результата | earn  | `level_improvement` | Через `POST /check/level`                                  |
| Покупка подсказок    | spend | `shop_purchase`     | Через `POST /shop/purchase`                                |
| Пропуск уровня       | spend | `skip_level`        | Через `POST /economy/transaction`                          |
| Восстановление жизни | spend | `restore_life`      | Через `POST /lives/restore`                                |
| Покупка предмета     | spend | `shop_purchase`     | Через `POST /shop/purchase`                                |

> **Важно:** Начисление фрагментов за уровень происходит **только** внутри `POST /check/level`. Эндпоинт `/economy/transaction` используется только для `shop_purchase` и `skip_level`.

### Защита от двойного начисления

- `Idempotency-Key` на всех мутирующих запросах.
- Сервер хранит использованные ключи 24 часа в Redis.
- Повторный запрос с тем же ключом → тот же ответ без повторного изменения баланса.

---

## 12. Система жизней

### Серверный пересчёт

Сервер — единственный авторитетный источник для количества жизней. При каждом запросе `GET /lives` или `POST /check/level`:

```python
# domain/rules/lives.py

@dataclass
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
        config: BalanceConfig,
    ) -> LivesState:
        if current_lives >= config.max_lives:
            return LivesState(current_lives, 0, last_restore_ts)

        elapsed = server_now - last_restore_ts
        restored = elapsed // config.restore_interval_seconds
        new_lives = min(current_lives + restored, config.max_lives)

        new_last_restore_ts = last_restore_ts + restored * config.restore_interval_seconds
        if new_lives >= config.max_lives:
            new_last_restore_ts = server_now

        seconds_until_next = (
            0 if new_lives >= config.max_lives
            else config.restore_interval_seconds - (elapsed % config.restore_interval_seconds)
        )

        return LivesState(new_lives, seconds_until_next, new_last_restore_ts)
```

### Восстановление за фрагменты

- `POST /lives/restore` — восстанавливает 1 жизнь, списывает `restore_cost_fragments` из `BalanceConfig`.
- `POST /lives/restore-all` — восстанавливает до `max_lives`, стоимость: `restore_cost_fragments × (max_lives - current_lives)`.
- Обе операции атомарны: списание фрагментов + добавление жизней в одной транзакции.

---

## 13. Контент-менеджмент (Remote Config)

### Хранение контента

Контент (определения уровней, секторов, баланс, каталог магазина) хранится в таблице `content_versions` как JSONB с версионированием.

### Стратегия обновления

1. Клиент при запуске запрашивает `GET /content/manifest`.
2. Сравнивает `content_version` с локально кешированным.
3. Если версия изменилась — запрашивает изменённые секторы/уровни.
4. Клиент содержит bundled fallback всех 100 уровней для offline-first режима.

### Кеширование

- Все `/content/*` ответы кешируются в Redis (TTL 5-10 мин).
- При обновлении контента — инвалидация кеша.
- HTTP-заголовки `ETag` / `Cache-Control` для клиентского кеширования.

### Инициализация контента (seed data)

При первом деплое необходимо заполнить таблицу `content_versions` данными:

- **5 секторов** (`SectorDefinition`) — из спецификации в [GDD](GDD.md) и [Architecture.md §12.1](Architecture.md).
- **100 уровней** (`LevelDefinition`) — по 20 на сектор, структура из [Architecture.md §12.2](Architecture.md).
- **Глобальный баланс** (`BalanceConfig`) — maxLives, restoreInterval, costs.
- **Каталог магазина** (`ShopItem[]`).

Seed data реализуется через скрипт `seed/seed_content.py`, который загружает JSON-файлы из `seed/data/` и вставляет их в БД через SQLAlchemy. Запускается через CLI: `python -m seed.seed_content`.

---

## 14. Аналитика

### Архитектура

```txt
Client (буферизует события)
  │
  ├─ Каждые 30 секунд или OnApplicationPause
  │
  └─ POST /analytics/events (batch, до 100 событий)
       │
       ├─ Валидация (белый список event_name, структура params)
       ├─ Bulk INSERT в analytics_events
       └─ 202 Accepted
```

### Поддерживаемые события

Полный список из [API.md §6.8](API.md#68-analytics):

| Событие          | Параметры                                                   |
| ---------------- | ----------------------------------------------------------- |
| `session_start`  | —                                                           |
| `session_end`    | `duration`                                                  |
| `level_start`    | `levelId`, `sectorId`, `attempt`                            |
| `level_complete` | `levelId`, `sectorId`, `stars`, `time`, `errors`, `attempt` |
| `level_fail`     | `levelId`, `sectorId`, `reason`, `attempt`                  |
| `level_skip`     | `levelId`, `sectorId`, `cost`                               |
| `sector_unlock`  | `sectorId`                                                  |
| `purchase`       | `itemId`, `cost`, `currency`                                |
| `hint_used`      | `levelId`, `hintIndex`                                      |
| `life_lost`      | `levelId`, `remainingLives`                                 |
| `life_restored`  | `method`                                                    |
| `action_undo`    | `levelId`, `actionType`                                     |
| `level_reset`    | `levelId`, `sectorId`                                       |

### Масштабирование аналитики

При росте объёмов:

1. Партицирование таблицы по месяцам (`PARTITION BY RANGE`).
2. Перенос старых данных в cold storage (S3/MinIO + Parquet).
3. При необходимости — отдельный сервис для приёма событий (Kafka → ClickHouse).

---

## 15. Синхронизация и конфликты

### 15.1 Очередь синхронизации

При восстановлении сети клиент отправляет накопленные операции FIFO (см. [API.md §7.4](API.md#74-очередь-синхронизации)):

```txt
1. POST /auth/refresh           (обновить токен)
2. POST /check/level            (каждый отложенный результат)
3. POST /shop/purchase           (каждая отложенная покупка)
4. POST /economy/transaction     (каждая отложенная транзакция)
5. PUT  /save                    (финальное сохранение)
6. POST /analytics/events        (буферизованные события)
```

**Правила обработки на сервере:**

- Каждая операция обрабатывается **независимо**. Откат одной не блокирует следующие.
- Для `check_level`: если результат отклонён (reconciliation mismatch) — сервер возвращает свой результат, клиент принимает его.
- Каскадных откатов нет: откат уровня 5 не отменяет уровни 6 и 7.
- Принцип «прогресс всегда вперёд»: если игрок разблокировал контент через спорный результат — сервер принимает прогрессию.

### 15.2 Алгоритм мержа сохранений

При `409 SAVE_CONFLICT` на `PUT /save`:

```txt
Для каждого level_progress:
  is_completed    = local or server
  best_stars      = max(local, server)
  best_time       = if local == 0: server; if server == 0: local; else min(local, server)
  attempts        = max(local, server)

Для каждого sector_progress:
  state           = max(local, server)   по порядку: Locked < Available < InProgress < Completed
  stars_collected = max(local, server)
  ctrl_passed     = local or server

total_fragments   = server              (source of truth)
current_lives     = server              (source of truth)
consumables       = server              (source of truth)
owned_items       = union(local, server)
version           = server.version + 1
```

Серверная реализация мержа дублируется в классе `SaveMerger` для случаев, когда серверу нужно самому провести мерж (например, при обработке очереди синхронизации).

---

## 16. Безопасность

### 16.1 Транспорт

- **TLS 1.2+** обязательно (termination на уровне reverse proxy).
- **Certificate pinning** на клиенте (production-билды).
- **HSTS** заголовки.

### 16.2 Защита от атак

| Угроза                     | Защита                                                                 |
| -------------------------- | ---------------------------------------------------------------------- |
| **Replay attacks**         | `Idempotency-Key` + 24h TTL в Redis                                    |
| **Brute-force auth**       | Rate limiting: 10 req/min на `/auth/*` по IP                           |
| **Token theft**            | Ротация refresh-токенов, детекция повторного использования             |
| **Balance manipulation**   | Серверная source of truth для экономики                                |
| **Answer spoofing**        | Полная серверная ре-валидация через `LevelCheckService`                |
| **SQL Injection**          | Parameterized queries (SQLAlchemy)                                     |
| **Mass data exfiltration** | Rate limiting на всех эндпоинтах                                       |
| **Content manipulation**   | Контент read-only для клиентов, обновление только через миграции/админ |

### 16.3 Rate Limiting

Конфигурация из [API.md §9.4](API.md#94-rate-limiting):

| Эндпоинт                 | Лимит                 |
| ------------------------ | --------------------- |
| `POST /auth/*`           | 10 req/min per IP     |
| `PUT /save`              | 30 req/min per player |
| `POST /economy/*`        | 60 req/min per player |
| `POST /lives/*`          | 30 req/min per player |
| `POST /check/level`      | 60 req/min per player |
| `POST /analytics/events` | 10 req/min per player |
| `GET /content/*`         | 30 req/min per player |
| `GET /shop/*`            | 30 req/min per player |

Реализация через Redis: `INCR rate:{player_id}:{endpoint}` с `EXPIRE 60`.

### 16.4 Серверная валидация

Сервер **перепроверяет** все критические значения:

| Что проверяется     | Как                                                                                                |
| ------------------- | -------------------------------------------------------------------------------------------------- |
| `fragment_reward`   | По конфигу уровня, не по значению клиента                                                          |
| Звёзды              | Пересчитываются через `StarRatingCalculator` (с использованием доверенного `errors_before_submit`) |
| Жизни               | Пересчитываются по серверному таймеру                                                              |
| Цена покупки        | По серверному каталогу (не по `cached_price`)                                                      |
| Правильность ответа | Полная ре-валидация на сервере                                                                     |

---

## 17. Инфраструктура и деплой

### 17.1 Окружения

| Окружение       | Назначение                 | БД                                  | Домен                      |
| --------------- | -------------------------- | ----------------------------------- | -------------------------- |
| **Development** | Локальная разработка       | Docker Compose (Postgres + Redis)   | `localhost:8000`           |
| **Staging**     | Тестирование перед релизом | Отдельный инстанс Postgres          | `staging-api.starfunc.app` |
| **Production**  | Боевое окружение           | Managed Postgres / отдельный сервер | `api.starfunc.app`         |

### 17.2 CI/CD

```txt
Push to main
  │
  ├─ 1. Setup Python 3.12 + uv
  ├─ 2. Install dependencies (uv sync)
  ├─ 3. Lint (ruff check + ruff format --check)
  ├─ 4. Type check (mypy)
  ├─ 5. Run unit tests (pytest tests/unit)
  ├─ 6. Run integration tests (pytest tests/integration — testcontainers)
  ├─ 7. Build Docker image
  ├─ 8. Push to container registry
  │
  └─ Deploy to staging (auto)
       │
       └─ Manual approval → Deploy to production
```

### 17.3 Контейнеризация

**Dockerfile:**

```dockerfile
FROM python:3.12-slim AS base

WORKDIR /app

# Установка uv для быстрого управления зависимостями
COPY --from=ghcr.io/astral-sh/uv:latest /uv /usr/local/bin/uv

COPY pyproject.toml uv.lock ./
RUN uv sync --frozen --no-dev

COPY src/ src/
COPY alembic/ alembic/
COPY alembic.ini .

EXPOSE 8000

CMD ["uv", "run", "gunicorn", "app.main:app", \
     "--worker-class", "uvicorn.workers.UvicornWorker", \
     "--bind", "0.0.0.0:8000", \
     "--workers", "4"]
```

**docker-compose.yml:**

```yaml
services:
  api:
    build: .
    ports:
      - "8000:8000"
    env_file:
      - .env
    depends_on:
      db:
        condition: service_healthy
      redis:
        condition: service_healthy

  db:
    image: postgres:16-alpine
    volumes:
      - pgdata:/var/lib/postgresql/data
    environment:
      POSTGRES_DB: starfunc
      POSTGRES_USER: starfunc_user
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U starfunc_user -d starfunc"]
      interval: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    volumes:
      - redisdata:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      retries: 5

  nginx:
    image: nginx:alpine
    ports:
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf
      - ./certs:/etc/nginx/certs
    depends_on:
      - api

volumes:
  pgdata:
  redisdata:
```

---

## 18. Масштабирование

### 18.1 Начальная нагрузка

Для casual мобильной игры с 100 уровнями начальная нагрузка невелика:

| Метрика                | Оценка   |
| ---------------------- | -------- |
| DAU (первые месяцы)    | 100–1000 |
| RPS (пик)              | 10–50    |
| Размер БД (первый год) | < 10 GB  |

Один VPS (4 vCPU, 8 GB RAM) покрывает эту нагрузку с запасом.

### 18.2 Стратегия масштабирования

При росте:

1. **Горизонтальное масштабирование API** — stateless-архитектура позволяет запускать несколько инстансов за load balancer. Gunicorn с N Uvicorn workers.
2. **Read Replicas PostgreSQL** — для read-heavy эндпоинтов (`GET /content/*`, `GET /save`).
3. **Connection Pooling** — PgBouncer перед PostgreSQL (дополнительно к встроенному пулу SQLAlchemy).
4. **Партицирование** — таблица `analytics_events` по месяцам.
5. **CDN** — для статического контента (если добавятся ассеты).
6. **Отдельный сервис аналитики** — вынести приём событий в отдельный процесс при высокой нагрузке.

### 18.3 Что НЕ нужно сейчас

- Микросервисная архитектура (монолит достаточен для 100 уровней и <10K DAU)
- Kubernetes (Docker Compose покрывает потребности)
- Message queues (прямой bulk-insert быстрее для текущих объёмов)
- Шардирование БД

---

## 19. Мониторинг и наблюдаемость

### 19.1 Логирование

| Уровень     | Что логируется                                           |
| ----------- | -------------------------------------------------------- |
| **Info**    | Входящие запросы, успешные операции, регистрации         |
| **Warning** | Rate limit hits, конфликты сохранений, невалидные ответы |
| **Error**   | Необработанные исключения, таймауты БД, ошибки Redis     |

Структурированное логирование через **structlog**:

```python
import structlog

logger = structlog.get_logger()

logger.info(
    "level_check_completed",
    player_id=str(player_id),
    level_id=level_id,
    is_valid=result.is_valid,
    stars=result.stars,
)
```

Sink: stdout (structured JSON) + Grafana Loki для поиска. В production Gunicorn направляет stdout в файл или log collector.

### 19.2 Метрики

| Метрика                         | Тип       | Описание                                |
| ------------------------------- | --------- | --------------------------------------- |
| `http_requests_total`           | Counter   | Запросы по эндпоинту и HTTP-коду        |
| `http_request_duration_seconds` | Histogram | Время обработки запросов                |
| `active_players_total`          | Gauge     | Уникальные player_id за последние 5 мин |
| `level_checks_total`            | Counter   | Проверки уровней (valid/invalid)        |
| `transactions_total`            | Counter   | Экономические транзакции (earn/spend)   |
| `save_conflicts_total`          | Counter   | Конфликты сохранений (409)              |
| `db_query_duration_seconds`     | Histogram | Время SQL-запросов                      |
| `redis_operations_total`        | Counter   | Операции с Redis                        |

Экспорт: **prometheus-fastapi-instrumentator** для автометрик + кастомные метрики через `prometheus_client`. Визуализация — **Grafana** дашборды.

### 19.3 Health Checks

```txt
GET /health          — базовый liveness (200 OK)
GET /health/ready    — readiness (проверка PostgreSQL + Redis)
```

```python
@app.get("/health")
async def health():
    return {"status": "ok"}

@app.get("/health/ready")
async def health_ready(
    session: AsyncSession = Depends(get_session),
    redis: Redis = Depends(get_redis),
):
    await session.execute(text("SELECT 1"))
    await redis.ping()
    return {"status": "ready"}
```

### 19.4 Алерты

| Условие                   | Действие                     |
| ------------------------- | ---------------------------- |
| Error rate > 1% за 5 мин  | Уведомление в Telegram/Slack |
| P99 latency > 2 сек       | Уведомление                  |
| DB connections > 80% пула | Уведомление                  |
| Redis memory > 80%        | Уведомление                  |
| Disk usage > 85%          | Уведомление                  |

---

## 20. Оценка задач

### Фаза S0 — Инфраструктура

| ID   | Задача                  | Зависимости | Описание                                                                 |
| ---- | ----------------------- | ----------- | ------------------------------------------------------------------------ |
| S0.1 | Скаффолдинг проекта     | —           | Создание структуры пакетов, pyproject.toml, docker-compose, .env.example |
| S0.2 | Настройка БД и миграции | S0.1        | SQLAlchemy models, Alembic init, начальные миграции                      |
| S0.3 | Redis интеграция        | S0.1        | Подключение redis-py, базовые helper-функции кеширования                 |
| S0.4 | CI/CD пайплайн          | S0.1        | GitHub Actions: lint (ruff), type check (mypy), test, docker image       |

### Фаза S1 — Аутентификация и базовые сервисы

| ID   | Задача                    | Зависимости | Описание                                                             |
| ---- | ------------------------- | ----------- | -------------------------------------------------------------------- |
| S1.1 | JwtProvider               | S0.1        | Генерация и валидация JWT (python-jose)                              |
| S1.2 | AuthService + auth router | S0.2, S1.1  | `POST /auth/register`, `POST /auth/refresh`                          |
| S1.3 | Middleware pipeline       | S1.2        | Exception handling, logging, rate limiting, idempotency, server time |
| S1.4 | Привязка аккаунтов        | S1.2        | `POST /auth/link` (Google Play, Apple GC)                            |

### Фаза S2 — Основные сервисы

| ID   | Задача                          | Зависимости | Описание                                                       |
| ---- | ------------------------------- | ----------- | -------------------------------------------------------------- |
| S2.1 | SaveService + save router       | S1.2, S0.2  | `GET /save`, `PUT /save`, оптимистичная блокировка             |
| S2.2 | EconomyService + economy router | S2.1        | `GET /economy/balance`, `POST /economy/transaction`            |
| S2.3 | LivesService + lives router     | S2.1        | `GET /lives`, `POST /lives/restore`, `POST /lives/restore-all` |
| S2.4 | ContentService + content router | S0.2, S0.3  | Все `/content/*` эндпоинты, кеширование                        |
| S2.5 | Seed data для контента          | S2.4        | Заполнение 5 секторов, 100 уровней, баланса, каталога          |

### Фаза S3 — Ключевая бизнес-логика

| ID   | Задача                           | Зависимости                  | Описание                                 |
| ---- | -------------------------------- | ---------------------------- | ---------------------------------------- |
| S3.1 | ValidationEngine                 | S2.5                         | Серверная валидация всех 6 типов ответов |
| S3.2 | StarRatingCalculator             | —                            | Расчёт звёздного рейтинга                |
| S3.3 | ProgressionRules                 | S2.5                         | Разблокировка уровней и секторов         |
| S3.4 | LevelCheckService + check router | S3.1, S3.2, S3.3, S2.2, S2.3 | `POST /check/level` — атомарная операция |
| S3.5 | SaveMerger                       | S2.1                         | Стратегия мержа при конфликтах           |

### Фаза S4 — Магазин, аналитика, полировка

| ID   | Задача                              | Зависимости | Описание                                                 |
| ---- | ----------------------------------- | ----------- | -------------------------------------------------------- |
| S4.1 | ShopService + shop router           | S2.2        | `GET /shop/items`, `POST /shop/purchase`                 |
| S4.2 | AnalyticsService + analytics router | S0.2        | `POST /analytics/events`, bulk insert                    |
| S4.3 | Health checks + метрики             | S0.1        | `/health`, `/metrics`, prometheus-fastapi-instrumentator |
| S4.4 | Интеграционные тесты                | S3.4        | Full flow тесты с testcontainers-python                  |
| S4.5 | Документация API                    | S3.4        | Автогенерация OpenAPI/Swagger через FastAPI              |

### Диаграмма зависимостей серверных задач

```txt
S0.1 ─┬─ S0.2 ─┬─ S1.2 ─┬─ S2.1 ─┬─ S2.2 ─── S4.1
      │        │        │        │         │
      ├─ S0.3  │  S1.1 ─┘  S1.3  ├─ S2.3   └── S3.4
      │        │               │         │
      └─ S0.4  └─ S2.4 ─ S2.5  │   S3.1 ─┤
                                │   S3.2 ─┤
                          S1.4  │   S3.3 ─┘
                                │
                                └─ S3.5

S0.2 ──── S4.2
S0.1 ──── S4.3
S3.4 ──── S4.4, S4.5
```

---

## Приложение A — Конфигурация приложения

### Переменные окружения (.env.example)

```bash
# Database
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_DB=starfunc
POSTGRES_USER=starfunc_user
POSTGRES_PASSWORD=changeme
DATABASE_URL=postgresql+asyncpg://${POSTGRES_USER}:${POSTGRES_PASSWORD}@${POSTGRES_HOST}:${POSTGRES_PORT}/${POSTGRES_DB}

# Redis
REDIS_URL=redis://localhost:6379/0

# JWT
JWT_SECRET=changeme-min-256-bits-long-secret-key-here
JWT_ALGORITHM=HS256
JWT_ACCESS_TOKEN_EXPIRE_MINUTES=60
JWT_REFRESH_TOKEN_EXPIRE_DAYS=90

# Rate Limiting
RATE_LIMIT_AUTH=10           # req/min per IP
RATE_LIMIT_SAVE=30           # req/min per player
RATE_LIMIT_ECONOMY=60
RATE_LIMIT_LIVES=30
RATE_LIMIT_CHECK=60
RATE_LIMIT_ANALYTICS=10
RATE_LIMIT_CONTENT=30
RATE_LIMIT_SHOP=30

# Idempotency
IDEMPOTENCY_KEY_EXPIRATION_HOURS=24

# Game Balance
MAX_LIVES=5
RESTORE_INTERVAL_SECONDS=1800
RESTORE_COST_FRAGMENTS=20
SKIP_LEVEL_COST_FRAGMENTS=100
IMPROVEMENT_BONUS_PER_STAR=5
HINT_COST_FRAGMENTS=10

# Server
ENV=development              # development | staging | production
LOG_LEVEL=INFO
WORKERS=4
```

### Pydantic Settings (config.py)

```python
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

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}
```

---

## Приложение B — Связь с клиентскими задачами

Маппинг серверных задач на клиентские задачи из [Tasks.md](Tasks.md) и раздел клиентской архитектуры из [API.md §10](API.md#10-клиентская-архитектура):

| Клиентская задача                              | Серверная зависимость              | Описание                                                          |
| ---------------------------------------------- | ---------------------------------- | ----------------------------------------------------------------- |
| 1.12 (ApiClient, NetworkMonitor, TokenManager) | S1.2 (AuthService)                 | Клиент должен интегрироваться с `POST /auth/register`, `/refresh` |
| 1.13 (AuthService клиент)                      | S1.2, S1.4                         | Клиентский AuthService вызывает серверный                         |
| 2.1a (CloudSaveClient, HybridSaveService)      | S2.1 (SaveService)                 | `GET /save`, `PUT /save`                                          |
| 2.3a (ServerEconomyService)                    | S2.2 (EconomyService)              | `GET /economy/balance`, `POST /economy/transaction`               |
| 2.4a (ServerLivesService)                      | S2.3 (LivesService)                | `GET /lives`, `POST /lives/restore`                               |
| 2.13 (ContentService клиент)                   | S2.4, S2.5 (ContentService + seed) | `/content/*`                                                      |
| 4.3a (ServerShopService)                       | S4.1 (ShopService)                 | `GET /shop/items`, `POST /shop/purchase`                          |
| 4.8a (REST analytics)                          | S4.2 (AnalyticsService)            | `POST /analytics/events`                                          |

> **Порядок разработки:** серверные задачи фаз S0-S1 можно параллелить с клиентскими задачами фаз 1-2 (клиент работает offline-first). Интеграция начинается с фазы S2, когда клиент получает задачи 1.12-1.13.

---

## Приложение C — Зависимости (pyproject.toml)

```toml
[project]
name = "starfunc-server"
version = "0.1.0"
requires-python = ">=3.12"

dependencies = [
    # Web framework
    "fastapi>=0.110",
    "uvicorn[standard]>=0.29",
    "gunicorn>=22.0",

    # Database
    "sqlalchemy[asyncio]>=2.0",
    "asyncpg>=0.29",
    "alembic>=1.13",

    # Redis
    "redis[hiredis]>=5.0",

    # Auth
    "python-jose[cryptography]>=3.3",
    "passlib[bcrypt]>=1.7",

    # Validation & Settings
    "pydantic>=2.7",
    "pydantic-settings>=2.2",

    # Logging & Monitoring
    "structlog>=24.1",
    "prometheus-fastapi-instrumentator>=7.0",
    "prometheus-client>=0.20",

    # HTTP client (for external auth verification)
    "httpx>=0.27",
]

[project.optional-dependencies]
dev = [
    "pytest>=8.1",
    "pytest-asyncio>=0.23",
    "httpx>=0.27",              # AsyncClient for testing
    "testcontainers[postgres,redis]>=4.4",
    "factory-boy>=3.3",
    "ruff>=0.4",
    "mypy>=1.10",
]

[tool.ruff]
target-version = "py312"
line-length = 120

[tool.ruff.lint]
select = ["E", "F", "I", "N", "UP", "B", "SIM", "RUF"]

[tool.mypy]
python_version = "3.12"
strict = true
plugins = ["pydantic.mypy"]

[tool.pytest.ini_options]
asyncio_mode = "auto"
testpaths = ["tests"]
```
