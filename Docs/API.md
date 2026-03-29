# API — Клиент-серверное взаимодействие STAR FUNC

> Документ описывает REST API бэкенда для гибридной архитектуры.
> Клиент (Unity) работает offline-first: все данные кешируются локально, синхронизация происходит при наличии сети.

---

## Оглавление

- [1. Общие сведения](#1-общие-сведения)
- [2. Аутентификация](#2-аутентификация)
- [3. Формат запросов и ответов](#3-формат-запросов-и-ответов)
- [4. Обработка ошибок](#4-обработка-ошибок)
- [5. Модели данных](#5-модели-данных)
- [6. Эндпоинты](#6-эндпоинты)
  - [6.1 Auth](#61-auth)
  - [6.2 Save / Cloud Sync](#62-save--cloud-sync)
  - [6.3 Economy](#63-economy)
  - [6.4 Lives](#64-lives)
  - [6.5 Shop](#65-shop)
  - [6.6 Level Content (Remote Config)](#66-level-content-remote-config)
  - [6.7 Level Check](#67-level-check)
  - [6.8 Analytics](#68-analytics)
  - [6.9 Health Check](#69-health-check)
- [7. Offline-first стратегия](#7-offline-first-стратегия)
- [8. Конфликты и синхронизация](#8-конфликты-и-синхронизация)
- [9. Безопасность](#9-безопасность)
- [10. Клиентская архитектура](#10-клиентская-архитектура)
- [11. Влияние на задачи (Tasks.md)](#11-влияние-на-задачи-tasksmd)

---

## 1. Общие сведения

| Параметр        | Значение                                              |
| --------------- | ----------------------------------------------------- |
| Транспорт       | HTTPS (TLS 1.2+)                                      |
| Формат данных   | JSON (UTF-8)                                          |
| API-стиль       | REST                                                  |
| Версионирование | Через URL-префикс: `/api/v1/`                         |
| Аутентификация  | Bearer Token (JWT)                                    |
| Таймаут клиента | 10 секунд (hard), 5 секунд (soft — переход в offline) |
| Сжатие          | gzip (Accept-Encoding / Content-Encoding)             |
| Идемпотентность | Все мутирующие запросы поддерживают `Idempotency-Key` |

**Base URL:**

```md
https://api.starfunc.app/api/v1
```

---

## 2. Аутентификация

### 2.1 Анонимная регистрация устройства

При первом запуске клиент генерирует `deviceId` (UUID v7) и регистрируется анонимно. Это позволяет начать игру без создания аккаунта.

### 2.2 Привязка аккаунта (опционально)

Игрок может привязать аккаунт через Google Play Games / Apple Game Center для кросс-девайс синхронизации.

### 2.3 Токены

| Токен         | Формат | Время жизни | Хранение на клиенте                           |
| ------------- | ------ | ----------- | --------------------------------------------- |
| Access Token  | JWT    | 1 час       | В памяти (не persisted)                       |
| Refresh Token | JWT    | 90 дней     | `Application.persistentDataPath` (зашифрован) |

Все запросы (кроме `POST /auth/*`) требуют заголовок:

```md
Authorization: Bearer <access_token>
```

При получении `401 Unauthorized` клиент использует refresh token для обновления. При неудаче — переход в offline-режим.

---

## 3. Формат запросов и ответов

### 3.1 Успешный ответ

```json
{
  "status": "ok",
  "data": { ... },
  "serverTime": 1711700000
}
```

| Поле         | Тип    | Описание                                         |
| ------------ | ------ | ------------------------------------------------ |
| `status`     | string | `"ok"` для успешных ответов                      |
| `data`       | object | Тело ответа (специфично для эндпоинта)           |
| `serverTime` | long   | Unix timestamp сервера (для синхронизации часов) |

HTTP-код по умолчанию — `200 OK`. Исключение: `POST /analytics/events` возвращает `202 Accepted` (асинхронная обработка), но тело ответа сохраняет тот же формат `{ status, data, serverTime }`.

### 3.2 Стандартные заголовки запросов

```md
Content-Type: application/json
Authorization: Bearer <token>
Accept-Encoding: gzip
X-Client-Version: 1.0.0
X-Platform: android|ios
Idempotency-Key: <uuid> # для мутирующих запросов
```

### 3.3 Стандартные заголовки ответов

```md
Content-Type: application/json
Content-Encoding: gzip
Strict-Transport-Security: max-age=63072000; includeSubDomains; preload
```

Для эндпоинтов `/content/*`:

```md
ETag: "<hash>"
Cache-Control: public, max-age=300
```

Клиент может отправлять `If-None-Match: "<hash>"` для условных запросов. Сервер возвращает `304 Not Modified` если контент не изменился.

---

## 4. Обработка ошибок

### 4.1 Формат ошибки

```json
{
  "status": "error",
  "error": {
    "code": "INSUFFICIENT_FUNDS",
    "message": "Not enough fragments",
    "details": {
      "required": 50,
      "available": 30
    }
  },
  "serverTime": 1711700000
}
```

### 4.2 HTTP-коды и коды ошибок

| HTTP | Код ошибки               | Описание                                           |
| ---- | ------------------------ | -------------------------------------------------- |
| 400  | `INVALID_REQUEST`        | Невалидный JSON или отсутствуют обязательные поля  |
| 400  | `INVALID_TRANSACTION`    | Некорректная операция (отрицательная сумма и т.д.) |
| 401  | `TOKEN_EXPIRED`          | Access token истёк, нужен refresh                  |
| 401  | `INVALID_TOKEN`          | Токен невалиден                                    |
| 403  | `FORBIDDEN`              | Нет доступа к ресурсу                              |
| 404  | `NOT_FOUND`              | Ресурс не найден                                   |
| 409  | `SAVE_CONFLICT`          | Конфликт версий при синхронизации сохранения       |
| 409  | `ACCOUNT_ALREADY_LINKED` | Провайдер уже привязан к другому playerId          |
| 422  | `INSUFFICIENT_FUNDS`     | Недостаточно фрагментов                            |
| 422  | `NO_LIVES`               | Нет жизней                                         |
| 422  | `VALIDATION_FAILED`      | Ответ проверен и признан неверным                  |
| 429  | `RATE_LIMITED`           | Слишком много запросов                             |
| 500  | `INTERNAL_ERROR`         | Внутренняя ошибка сервера                          |
| 503  | `SERVICE_UNAVAILABLE`    | Сервер на обслуживании                             |

### 4.3 Стратегия retry на клиенте

| HTTP-код | Стратегия                                                |
| -------- | -------------------------------------------------------- |
| 401      | Refresh token → повторить запрос → если неудача, offline |
| 409      | Не повторять, обработать конфликт                        |
| 422      | Не повторять, обработать ошибку бизнес-логики            |
| 429      | Exponential backoff (1s, 2s, 4s), макс. 3 попытки        |
| 500, 503 | Exponential backoff, макс. 3 попытки → offline           |
| Timeout  | 1 retry → offline                                        |

---

## 5. Модели данных

### 5.1 PlayerSaveData (облачное сохранение)

Полная модель, синхронизируемая между клиентом и сервером.

```jsonc
{
  "saveVersion": 1,
  "version": 42, // инкрементируется при каждом сохранении
  "lastModified": 1711700000, // Unix timestamp

  // Прогрессия
  "currentSectorIndex": 1, // 0-indexed (сектор 2 → индекс 1)
  "sectorProgress": {
    "sector_1": {
      "state": "Completed", // Locked | Available | InProgress | Completed
      "starsCollected": 48,
      "controlLevelPassed": true,
    },
    "sector_2": {
      "state": "InProgress",
      "starsCollected": 12,
      "controlLevelPassed": false,
    },
  },
  "levelProgress": {
    "sector_1_level_01": {
      "isCompleted": true,
      "bestStars": 3,
      "bestTime": 14.5,
      "attempts": 2,
    },
  },

  // Экономика
  "totalFragments": 230,

  // Жизни
  "currentLives": 4,
  "lastLifeRestoreTimestamp": 1711698200, // Unix timestamp (серверное время)

  // Магазин
  "ownedItems": ["skin_neon_ghost", "theme_dark_grid"],

  // Расходуемые предметы
  "consumables": {
    "hints": 5, // Доступные подсказки
  },

  // Статистика
  "totalLevelsCompleted": 24,
  "totalStarsCollected": 60,
  "totalPlayTime": 3600.5,
}
```

Поле `version` — монотонно возрастающий счётчик. Каждая мутация (прохождение уровня, покупка, списание жизни) инкрементирует его. Используется для обнаружения конфликтов.

### 5.2 LevelDefinition (серверная конфигурация уровня)

Серверное представление `LevelData` SO. Клиент получает это и конвертирует в рантайм-объект.

```jsonc
{
  "levelId": "sector_2_level_05",
  "levelIndex": 4,
  "sectorId": "sector_2",
  "type": "Normal", // Tutorial | Normal | Bonus | Control | Final
  // Бонусные уровни (type=Bonus, индексы 7 и 12 в секторе) — опциональны:
  // игрок может пропустить их и перейти к следующему обязательному уровню.
  // Звёзды бонусных уровней НЕ учитываются в пороге разблокировки следующего сектора.

  "coordinatePlane": {
    "planeMin": { "x": -5, "y": -5 },
    "planeMax": { "x": 5, "y": 5 },
    "gridStep": 1.0,
  },

  "stars": [
    {
      "starId": "s2_l5_star_1",
      "coordinate": { "x": 1, "y": 3 },
      "initialState": "Active", // Hidden | Active
      "isControlPoint": true,
      "isDistractor": false,
      "belongsToSolution": true,
      "revealAfterAction": -1, // -1 = видна сразу
    },
  ],

  "taskType": "ChooseFunction", // ChooseCoordinate | ChooseFunction | AdjustGraph |
  // BuildFunction | IdentifyError | RestoreConstellation
  "referenceFunctions": [
    {
      "functionId": "func_linear_01",
      "type": "Linear", // Linear | Quadratic | Sinusoidal | Mixed
      "coefficients": [2.0, 1.0], // y = 2x + 1
      "domainRange": { "x": -5, "y": 5 },
    },
  ],

  "answerOptions": [
    { "optionId": "opt_1", "text": "y = 2x + 1", "isCorrect": true },
    { "optionId": "opt_2", "text": "y = 2x - 1", "isCorrect": false },
    { "optionId": "opt_3", "text": "y = x + 2", "isCorrect": false },
  ],

  "accuracyThreshold": 0.5,

  "starRating": {
    "threeStarMaxErrors": 0,
    "twoStarMaxErrors": 1,
    "oneStarMaxErrors": 3,
    "timerAffectsRating": false,
    "threeStarMaxTime": 0,
  },

  "constraints": {
    "maxAttempts": 0, // 0 = бесконечно
    "maxAdjustments": 0,
  },

  "visibility": {
    "useMemoryMode": false,
    "memoryDisplayDuration": 0,
    "graphVisibility": {
      "partialReveal": false,
      "initialVisibleSegments": 0,
      "revealPerCorrectAction": 0,
    },
  },

  "tutorial": {
    "showHints": true,
    "hints": [
      {
        "trigger": "OnLevelStart", // OnLevelStart | AfterErrors | OnFirstInteraction
        "hintText": "Выберите функцию, проходящую через все звёзды",
        "highlightPosition": { "x": 0.5, "y": 0.3 },
        "triggerAfterErrors": 0,
      },
    ],
  },

  "fragmentReward": 10,
}
```

### 5.3 SectorDefinition (серверная конфигурация сектора)

```jsonc
{
  "sectorId": "sector_2",
  "displayName": "Созвездие ориентира",
  "sectorIndex": 1, // 0-indexed (сектор 2 → индекс 1)
  "levelIds": ["sector_2_level_01", "sector_2_level_02", "..."],
  "previousSectorId": "sector_1", // null для первого
  "requiredStarsToUnlock": 30, // звёзды бонусных уровней (type=Bonus) не учитываются

  "visual": {
    "accentColor": "#5AE8E4",
    "starColor": "#FFD47A",
  },

  "introCutsceneId": "cutscene_sector_2_intro",
  "outroCutsceneId": "cutscene_sector_2_outro",
}
```

### 5.4 ShopItem

```jsonc
{
  "itemId": "hint_pack_5",
  "category": "Hints", // Hints | Lives | Skip | Customization
  "price": 50,
  "displayName": "Пакет подсказок (5 шт.)",
  "description": "Получите 5 дополнительных подсказок",
  "iconId": "icon_hint_pack",
  "isConsumable": true, // true = расходуемый, false = перманентный
  "isAvailable": true,
}
```

### 5.5 PlayerAnswer (отправка на проверку)

```jsonc
// ChooseCoordinate / ChooseFunction
{
  "answerType": "ChooseOption",
  "selectedOptionId": "opt_1"
}

// AdjustGraph / BuildFunction
{
  "answerType": "Function",
  "functionType": "Linear",
  "coefficients": [2.0, 1.0]
}

// IdentifyError
{
  "answerType": "IdentifyStars",
  "selectedStarIds": ["s2_l5_star_3"]
}

// RestoreConstellation
{
  "answerType": "PlaceStars",
  "placements": [
    { "starId": "s2_l5_star_1", "coordinate": { "x": 1, "y": 3 } },
    { "starId": "s2_l5_star_2", "coordinate": { "x": 2, "y": 5 } }
  ]
}
```

### 5.6 CheckResult (ответ сервера)

Полная структура ответа описана в [§6.7 Level Check](#67-level-check). Ответ включает:

- `result` — результат проверки (`isValid`, `stars`, `fragmentsEarned`, `time`, `errorCount`, `matchPercentage`, `errors`)
- `progressUpdate` — обновление прогрессии при верном ответе (`levelProgress`, `newFragmentBalance`, `unlockedLevels`, и т.д.)
- `livesUpdate` — обновление жизней при неверном ответе (`currentLives`, `secondsUntilNextRestore`)
- `levelFailed` / `failReason` — флаг провала уровня
- `newSaveVersion` — новая версия сохранения для последующего `PUT /save`

### 5.7 AnalyticsEvent

```jsonc
{
  "eventName": "level_complete",
  "timestamp": 1711700000,
  "sessionId": "uuid",
  "params": {
    "levelId": "sector_2_level_05",
    "sectorId": "sector_2",
    "stars": 3,
    "time": 14.5,
    "errors": 0,
    "attempt": 1,
  },
}
```

---

## 6. Эндпоинты

### 6.1 Auth

#### `POST /auth/register`

Анонимная регистрация устройства.

**Тело запроса:**

```jsonc
{
  "deviceId": "uuid-v7",
  "platform": "android", // android | ios
  "clientVersion": "1.0.0",
}
```

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "playerId": "player_abc123",
    "accessToken": "jwt...",
    "refreshToken": "jwt...",
    "expiresIn": 3600,
  },
  "serverTime": 1711700000,
}
```

Если `deviceId` уже зарегистрирован, возвращает существующего игрока (идемпотентно).

---

#### `POST /auth/refresh`

Обновление access token.

**Тело запроса:**

```jsonc
{
  "refreshToken": "jwt...",
}
```

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "accessToken": "jwt...",
    "refreshToken": "jwt...",
    "expiresIn": 3600,
  },
  "serverTime": 1711700000,
}
```

---

#### `POST /auth/link`

Привязка стороннего аккаунта (Google Play Games / Apple Game Center).

**Тело запроса:**

```jsonc
{
  "provider": "google_play", // google_play | apple_game_center
  "providerToken": "oauth-token...",
}
```

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "linked": true,
    "provider": "google_play",
    "displayName": "Player123",
  },
  "serverTime": 1711700000,
}
```

**Ошибки:**

- `409 ACCOUNT_ALREADY_LINKED` — этот провайдер уже привязан к другому playerId.

---

### 6.2 Save / Cloud Sync

#### `GET /save`

Получить облачное сохранение.

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "save": {
      /* PlayerSaveData */
    },
    "exists": true,
  },
  "serverTime": 1711700000,
}
```

Если сохранения нет — `exists: false`, `save: null`.

---

#### `PUT /save`

Записать / обновить облачное сохранение.

**Тело запроса:**

```jsonc
{
  "save": {
    /* PlayerSaveData */
  },
  "expectedVersion": 41, // текущая версия на клиенте (optimistic lock)
}
```

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "version": 42,
    "accepted": true,
  },
  "serverTime": 1711700000,
}
```

**Ошибки:**

- `409 SAVE_CONFLICT` — версия на сервере отличается от `expectedVersion`. Тело ответа содержит серверное сохранение для мержа:

```jsonc
{
  "status": "error",
  "error": {
    "code": "SAVE_CONFLICT",
    "message": "Save version mismatch",
    "details": {
      "serverVersion": 43,
      "serverSave": {
        /* PlayerSaveData */
      },
    },
  },
  "serverTime": 1711700000,
}
```

---

### 6.3 Economy

#### `GET /economy/balance`

Текущий баланс фрагментов (серверный source of truth).

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "totalFragments": 230,
  },
  "serverTime": 1711700000,
}
```

---

#### `POST /economy/transaction`

Атомарная проводка: начисление или списание фрагментов.

**Тело запроса:**

```jsonc
{
  "type": "spend", // earn | spend
  "amount": 50, // всегда положительное число
  "reason": "shop_purchase", // shop_purchase | skip_level
  "referenceId": "hint_pack_5", // ID связанной сущности (levelId / itemId)
}
```

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "previousBalance": 230,
    "newBalance": 180,
    "transactionId": "txn_abc123",
    // Только для reason: "skip_level" — серверное обновление прогрессии:
    "progressUpdate": {
      // null для shop_purchase
      "levelProgress": {
        "sector_2_level_05": {
          "isCompleted": true,
          "bestStars": 1,
          "bestTime": 0,
          "attempts": 0,
        },
      },
      "newFragmentBalance": 180,
      "sectorStarsCollected": 16,
      "unlockedLevels": ["sector_2_level_06"],
      "unlockedSectors": [],
      "sectorCompleted": false,
    },
    "newSaveVersion": 43, // при skip_level — инкрементированная версия сохранения
  },
  "serverTime": 1711700000,
}
```

**Ошибки:**

- `422 INSUFFICIENT_FUNDS` — недостаточно фрагментов.
- `400 INVALID_TRANSACTION` — отрицательная сумма, неизвестный reason и т.д.

**Правила серверной валидации:**

- `amount` > 0
- `reason` должен быть одним из: `shop_purchase`, `skip_level`
- Для `type: "spend"` → проверка баланса
- Двойные проводки по одному `Idempotency-Key` — возвращают исходный результат

> **Примечание:** Начисление фрагментов за уровень (`level_completion`, `level_improvement`) происходит атомарно внутри `POST /check/level`. Списание за восстановление жизней — через `POST /lives/restore`. Покупки подсказок — через `POST /shop/purchase`. Эндпоинт `/economy/transaction` используется только для операций `shop_purchase` (если не через `/shop/purchase`) и `skip_level`.
> **Примечание (skip_level):** При `reason: "skip_level"` сервер атомарно списывает фрагменты **и** обновляет прогрессию: уровень помечается пройденным с 1 звездой, 0 фрагментов награды, `best_time=0`. Поле `referenceId` должно содержать `levelId` пропускаемого уровня. В ответ включаются `progressUpdate` и `newSaveVersion`.

---

### 6.4 Lives

#### `GET /lives`

Текущее состояние жизней (серверный source of truth для таймера).

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "currentLives": 3,
    "maxLives": 5,
    "secondsUntilNextRestore": 1245, // 0 если жизни полные
    "restoreIntervalSeconds": 1800, // 30 минут
    "lastLifeRestoreTimestamp": 1711698200,
  },
  "serverTime": 1711700000,
}
```

Сервер при каждом запросе пересчитывает жизни на основе `lastLifeRestoreTimestamp` и `serverTime`.

---

#### `POST /lives/restore`

Восстановление одной жизни за фрагменты.

**Тело запроса:**

```jsonc
{
  "paymentMethod": "fragments", // fragments (единственный вариант пока)
}
```

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "currentLives": 4,
    "fragmentsSpent": 20,
    "newFragmentBalance": 160,
    "lastLifeRestoreTimestamp": 1711698200, // обновлённый timestamp (start timer если Жизни были полными до списания)
  },
  "serverTime": 1711700000,
}
```

**Ошибки:**

- `422 INSUFFICIENT_FUNDS` — недостаточно фрагментов.
- `400 INVALID_REQUEST` — жизни уже полные.

---

#### `POST /lives/restore-all`

Восстановление всех жизней до максимума за фрагменты. Стоимость рассчитывается на сервере: `restoreCostFragments × (maxLives - currentLives)`.

**Тело запроса:**

```jsonc
{
  "paymentMethod": "fragments",
}
```

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "currentLives": 5,
    "fragmentsSpent": 40,
    "newFragmentBalance": 120,
    "livesRestored": 2,
    "lastLifeRestoreTimestamp": 1711700000, // обновлённый timestamp (жизни восстановлены до максимума)
  },
  "serverTime": 1711700000,
}
```

**Ошибки:**

- `422 INSUFFICIENT_FUNDS` — недостаточно фрагментов.
- `400 INVALID_REQUEST` — жизни уже полные.

---

### 6.5 Shop

#### `GET /shop/items`

Каталог доступных товаров.

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "items": [
      {
        "itemId": "hint_pack_5",
        "category": "Hints",
        "price": 50,
        "displayName": "Пакет подсказок (5 шт.)",
        "description": "Получите 5 дополнительных подсказок",
        "iconId": "icon_hint_pack",
        "isConsumable": true,
        "isAvailable": true,
      },
    ],
    "catalogVersion": 3,
  },
  "serverTime": 1711700000,
}
```

Клиент кеширует каталог и проверяет `catalogVersion` периодически.

---

#### `POST /shop/purchase`

Покупка товара за фрагменты.

**Тело запроса:**

```jsonc
{
  "itemId": "hint_pack_5",
  "cachedPrice": 50, // Цена, которую видел клиент на момент покупки (для offline-покупок)
}
```

> **Offline-покупки:** если клиент совершил покупку offline, при синхронизации сервер принимает `cachedPrice` как актуальную стоимость, даже если цена в каталоге уже изменилась. Принцип: игрок получает товар по той цене, которую он видел. После реконнекта клиент обновляет кеш каталога через `GET /shop/items` для будущих покупок.

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "purchased": true,
    "itemId": "hint_pack_5",
    "fragmentsSpent": 50,
    "newFragmentBalance": 130,
    "consumablesUpdate": {
      // только для расходуемых товаров
      "hints": 10,
    },
  },
  "serverTime": 1711700000,
}
```

**Ошибки:**

- `422 INSUFFICIENT_FUNDS`
- `404 NOT_FOUND` — неизвестный `itemId`
- `400 INVALID_REQUEST` — товар уже куплен (для не-consumable)

---

### 6.6 Level Content (Remote Config)

#### `GET /content/manifest`

Манифест всего контента с версиями. Клиент запрашивает при запуске и использует для diff-обновления.

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "contentVersion": 7,
    "sectors": [
      {
        "sectorId": "sector_1",
        "version": 3,
        "levelCount": 20,
      },
      {
        "sectorId": "sector_2",
        "version": 5,
        "levelCount": 20,
      },
    ],
    "shopCatalogVersion": 3,
    "balanceConfigVersion": 2,
  },
  "serverTime": 1711700000,
}
```

---

#### `GET /content/sectors`

Все секторы (метаданные).

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "sectors": [
      /* массив SectorDefinition */
    ],
  },
  "serverTime": 1711700000,
}
```

---

#### `GET /content/sectors/{sectorId}`

Один сектор по ID.

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "sector": {
      /* SectorDefinition */
    },
  },
  "serverTime": 1711700000,
}
```

---

#### `GET /content/sectors/{sectorId}/levels`

Все уровни сектора.

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "levels": [
      /* массив LevelDefinition */
    ],
  },
  "serverTime": 1711700000,
}
```

---

#### `GET /content/levels/{levelId}`

Один уровень по ID.

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "level": {
      /* LevelDefinition */
    },
  },
  "serverTime": 1711700000,
}
```

---

#### `GET /content/balance`

Глобальные настройки баланса (стоимости, таймеры, множители).

**Ответ (200):**

```jsonc
{
  "status": "ok",
  "data": {
    "version": 2,
    "livesConfig": {
      "maxLives": 5,
      "restoreIntervalSeconds": 1800, // 30 минут
      "restoreCostFragments": 20,
    },
    "skipLevelCostFragments": 100,
    "improvementBonusPerStar": 5, // доп. фрагменты за каждую новую звезду при повторном прохождении
    "hintCostFragments": 10,
  },
  "serverTime": 1711700000,
}
```

---

### 6.7 Level Check

#### `POST /check/level`

Серверная проверка ответа на уровне. Используется для сверки (reconciliation) после локальной проверки.

Этот эндпоинт является основной точкой мутации игрового состояния при прохождении уровня. Сервер атомарно:

- Проверяет правильность ответа
- Начисляет фрагменты (при успехе)
- Списывает **одну жизнь** за попытку (при ошибке). Один вызов `/check/level` = одна попытка = максимум одна жизнь.
- Обновляет прогрессию (разблокировка уровней/секторов)

> **Заблокированные уровни:** Сервер **не отклоняет** запросы для заблокированных уровней. Это необходимо для корректной обработки offline-очереди: клиент мог пройти предыдущий уровень локально (но серверная reconciliation отклонила его), а следующий уровень при этом был пройден корректно. Сервер проверяет ответ и засчитывает правильные попытки независимо от статуса разблокировки.

- Определяет провал уровня (при исчерпании попыток или жизней)
- Инкрементирует `version` сохранения и возвращает `newSaveVersion` для последующего `PUT /save`

**Предусловия:**

- У игрока должна быть хотя бы 1 жизнь. При `currentLives == 0` сервер возвращает `422 NO_LIVES`.

> Отдельные эндпоинты `/economy/transaction` и `/lives/restore` используются для действий вне игрового процесса (магазин, восстановление жизней, пропуск уровня). Начисление фрагментов за уровень происходит только через `/check/level`.

**Тело запроса:**

```jsonc
{
  "levelId": "sector_2_level_05",
  "answer": {
    /* PlayerAnswer (см. 5.5) */
  },
  "elapsedTime": 14.5,
  "errorsBeforeSubmit": 0,
  "attempt": 1,
}
```

**Ответ (200) — верный ответ:**

```jsonc
{
  "status": "ok",
  "data": {
    "result": {
      "isValid": true,
      "stars": 3,
      "fragmentsEarned": 10,
      "time": 14.5,
      "errorCount": 0,
      "matchPercentage": 1.0,
      "errors": [],
    },
    "progressUpdate": {
      "levelProgress": {
        "sector_2_level_05": {
          "isCompleted": true,
          "bestStars": 3,
          "bestTime": 14.5,
          "attempts": 1,
        },
      },
      "newFragmentBalance": 240,
      "sectorStarsCollected": 22,
      "unlockedLevels": ["sector_2_level_06"],
      "unlockedSectors": [],
      "sectorCompleted": false,
    },
    "newSaveVersion": 43, // Новая версия сохранения; использовать как expectedVersion при PUT /save
  },
  "serverTime": 1711700000,
}
```

**Ответ (200) — неверный ответ:**

```jsonc
{
  "status": "ok",
  "data": {
    "result": {
      "isValid": false,
      "stars": 0,
      "fragmentsEarned": 0,
      "time": 14.5,
      "errorCount": 1,
      "matchPercentage": 0.0,
      "errors": ["Выбрана неверная функция"],
    },
    "livesUpdate": {
      "currentLives": 2,
      "secondsUntilNextRestore": 1800,
      "lastLifeRestoreTimestamp": 1711698200, // обновляется, если жизни были полными до списания (старт таймера восстановления)
    },
    "levelFailed": false, // true если уровень провален (см. правила ниже)
    "failReason": null, // "no_lives" | "max_attempts_reached" | null
    "newSaveVersion": 43, // Новая версия сохранения
  },
  "serverTime": 1711700000,
}
```

**Правила провала уровня:**

Уровень считается проваленным (`levelFailed: true`), когда:

- `failReason: "no_lives"` — после списания жизни `currentLives` стало 0
- `failReason: "max_attempts_reached"` — `maxAttempts > 0` в конфиге уровня и `attempt >= maxAttempts`

**Ошибки:**

- `422 NO_LIVES` — у игрока 0 жизней (после пересчёта по серверному таймеру). Попытка не засчитывается.

Сервер определяет `levelFailed` авторитетно: клиент показывает экран провала на основе локальной проверки, но при reconciliation серверное значение `levelFailed` является окончательным.

**Повторная отправка для завершённого уровня:**

Если уровень уже пройден, сервер возвращает `200` с ранее сохранённым результатом (лучшим). При улучшении результата (e.g., больше звёзд при повторном прохождении) сервер обновляет `bestStars` / `bestTime` и начисляет бонусные фрагменты за улучшение.

> **Важно:** Клиент всегда делает локальную проверку ПЕРВЫМ (для мгновенного фидбека). Серверная проверка — это reconciliation-шаг. Если результаты расходятся, серверный считается авторитетным.

---

### 6.8 Analytics

#### `POST /analytics/events`

Массовая отправка аналитических событий (батч).

**Тело запроса:**

```jsonc
{
  "events": [
    {
      "eventName": "level_start",
      "timestamp": 1711700000,
      "sessionId": "uuid",
      "params": {
        "levelId": "sector_2_level_05",
        "sectorId": "sector_2",
        "attempt": 1,
      },
    },
    {
      "eventName": "level_complete",
      "timestamp": 1711700015,
      "sessionId": "uuid",
      "params": {
        "levelId": "sector_2_level_05",
        "sectorId": "sector_2",
        "stars": 3,
        "time": 14.5,
        "errors": 0,
        "attempt": 1,
      },
    },
  ],
}
```

**Ответ (202 Accepted):**

```jsonc
{
  "status": "ok",
  "data": {
    "accepted": 2,
    "rejected": 0,
  },
  "serverTime": 1711700000,
}
```

**Поддерживаемые события:**

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
| `life_restored`  | `method` (`timer` \| `fragments`)                           |
| `action_undo`    | `levelId`, `actionType`                                     |
| `level_reset`    | `levelId`, `sectorId`                                       |

Клиент буферизирует события локально и отправляет батчами (каждые 30 секунд или при `OnApplicationPause`). Неотправленные события переживают перезапуск приложения (очередь в `persistentDataPath`).

---

### 6.9 Health Check

#### `GET /health`

Базовая проверка работоспособности сервера (liveness).

**Ответ (200):**

```jsonc
{
  "status": "ok",
}
```

Не требует аутентификации.

---

#### `GET /health/ready`

Проверка готовности сервера к обработке запросов (readiness). Проверяет подключение к PostgreSQL и Redis.

**Ответ (200):**

```jsonc
{
  "status": "ready",
}
```

**Ответ (503):**

```jsonc
{
  "status": "error",
  "error": {
    "code": "SERVICE_UNAVAILABLE",
    "message": "Database connection failed",
  },
}
```

Не требует аутентификации.

---

## 7. Offline-first стратегия

### 7.1 Принцип

Клиент **всегда** работает с локальными данными. Сеть используется для:

- Синхронизации сохранений при запуске и значимых событиях
- Проверки обновлений контента (remote config)
- Серверной валидации (reconciliation)
- Отправки аналитики

### 7.2 Поведение при отсутствии сети

| Система             | Offline-поведение                                                                                                                                                                                                                                      |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Сохранение**      | Локальный `save.json` используется как основной. Ставится в очередь на sync.                                                                                                                                                                           |
| **Экономика**       | Транзакции выполняются локально. Очередь транзакций отправляется при reconnect.                                                                                                                                                                        |
| **Жизни**           | Таймер работает по локальным часам. Небольшая погрешность допустима.                                                                                                                                                                                   |
| **Контент уровней** | Используется закешированная версия. Bundled fallback при первом запуске.                                                                                                                                                                               |
| **Валидация**       | Только локальная. Reconciliation при reconnect.                                                                                                                                                                                                        |
| **Магазин**         | Покупки выполняются локально по **кешированной цене** и ставятся в очередь. При reconnect сервер принимает покупку по цене, которую видел клиент (передаётся в `cachedPrice`). После синхронизации клиент обновляет кеш каталога с актуальными ценами. |
| **Аналитика**       | Буферизуется на диске, отправляется при reconnect.                                                                                                                                                                                                     |

### 7.3 Bundled fallback

Клиент включает в билд JSON с полным набором всех 100 уровней и 5 секторов. Это fallback на случай первого запуска без сети. При наличии сети — проверяется `contentVersion`, обновлённый контент скачивается и заменяет bundled.

Бандлы включают:

| Файл                        | Содержимое                                                           |
| --------------------------- | -------------------------------------------------------------------- |
| `content/sectors.json`      | Все 5 секторов (SectorDefinition[])                                  |
| `content/levels/`           | Все 100 уровней (LevelDefinition[]), по файлу на сектор              |
| `content/balance.json`      | Глобальные настройки баланса (стоимость жизней, пропуска, подсказок) |
| `content/shop_catalog.json` | Каталог магазина (ShopItem[])                                        |

Хранение кеша: `Application.persistentDataPath/content/`.

### 7.4 Очередь синхронизации

Все отложенные мутации (транзакции, результаты уровней, покупки) складываются в очередь:

```md
Application.persistentDataPath/sync_queue.json
```

Формат:

```jsonc
{
  "pendingOperations": [
    {
      "id": "uuid",
      "type": "check_level",
      "endpoint": "POST /check/level",
      "payload": { ... },
      "createdAt": 1711700000,
      "retries": 0
    },
    {
      "id": "uuid",
      "type": "shop_purchase",
      "endpoint": "POST /shop/purchase",
      "payload": { "itemId": "hint_pack_5", "cachedPrice": 50 },
      "createdAt": 1711700100,
      "retries": 0
    },
    {
      "id": "uuid",
      "type": "economy_transaction",
      "endpoint": "POST /economy/transaction",
      "payload": { ... },
      "createdAt": 1711700200,
      "retries": 0
    }
  ]
}
```

> **Важно:** Результаты прохождения уровней всегда синхронизируются через `check_level`, а **не** через `economy_transaction`. Начисление фрагментов за уровень происходит атомарно внутри `/check/level`. Тип `economy_transaction` используется только для действий вне игрового процесса (пропуск уровня, покупка в магазине).

При восстановлении сети очередь обрабатывается последовательно (FIFO). Если операция возвращает конфликт — включается flow конфликт-резолюции (раздел 8).

**Обработка ошибок в очереди:**

- Каждая операция обрабатывается независимо. Отклонение одной операции не блокирует следующие.
- Для `check_level`: если сервер отклоняет результат (reconciliation mismatch), клиент принимает серверное решение для этого уровня, но продолжает обработку остальных операций.
- Каскадных откатов нет: если уровень 5 отклонён, а уровни 6 и 7 корректны, сервер принимает 6 и 7.
- В спорных ситуациях (например, игрок разблокировал уровень через отклонённый результат) сервер принимает прогрессию клиента (принцип «прогресс всегда вперёд»).

---

## 8. Конфликты и синхронизация

### 8.1 Стратегия: «прогресс всегда вперёд»

Для STAR FUNC (однопользовательская игра) конфликты редки: они возникают только при игре на двух устройствах. Правило мержа:

> **Для каждого уровня берётся лучший результат. Для звёзд и прогрессии — максимальное значение. Состояние сектора — наиболее продвинутое. Для транзакционных ресурсов (фрагменты, жизни, расходуемые) — серверное значение как source of truth.**

### 8.2 Алгоритм мержа (клиент)

При получении `409 SAVE_CONFLICT`:

1. Получить серверное сохранение из тела ошибки (`details.serverSave`).
2. Для каждого `levelProgress`:
   - `isCompleted = local.isCompleted || server.isCompleted`
   - `bestStars = max(local.bestStars, server.bestStars)`
   - `bestTime`: если `local == 0` → `server`; если `server == 0` → `local`; иначе `min(local, server)`
   - `attempts = max(local.attempts, server.attempts)`
3. Для каждого `sectorProgress`:
   - `state = max(local.state, server.state)` по порядку: Locked < Available < InProgress < Completed
   - `starsCollected = max(local.starsCollected, server.starsCollected)`
   - `controlLevelPassed = local.controlLevelPassed || server.controlLevelPassed`
4. `totalFragments = server.totalFragments` (сервер — source of truth для транзакционных ресурсов)
5. `currentLives = server.currentLives` (сервер пересчитывает по таймеру авторитетно)
6. `ownedItems = union(local.ownedItems, server.ownedItems)`
7. `consumables` = серверное значение (аналогично фрагментам)
8. `version = server.version + 1`
9. Отправить мерженное сохранение через `PUT /save` с `expectedVersion = server.version`.

### 8.3 Конфликты экономических транзакций

Если серверный баланс не совпадает с ожидаемым при `POST /economy/transaction`:

- Клиент запрашивает `GET /economy/balance`
- Обновляет локальный баланс
- Повторяет транзакцию (если всё ещё хватает средств)
- Если не хватает — уведомляет игрока

---

## 9. Безопасность

### 9.1 Транспорт

- Все запросы через HTTPS (TLS 1.2+)
- Certificate pinning на клиенте (для production-билдов)

### 9.2 Аутентификация

- Access Token: JWT с коротким TTL (1 час)
- Refresh Token: JWT с TTL 90 дней, одноразовый (ротация при каждом использовании)
- Refresh token хранится зашифрованным (AES-256, ключ = комбинация `deviceId` + hardware fingerprint)

### 9.3 Серверная валидация

Сервер **не доверяет** клиенту в следующих вопросах:

- Количество заработанных фрагментов (`fragmentReward` проверяется по конфигу уровня)
- Статус жизней (пересчитывается по серверному таймеру)
- Стоимость покупки (проверяется по серверному каталогу)
- Правильность ответа (повторная валидация на сервере)

Клиенту доверяется:

- `elapsedTime` (время прохождения) — сложно фальсифицировать осмысленно, используется для аналитики
- `errorsBeforeSubmit` (количество ошибок до отправки) — используется для расчёта звёздного рейтинга
- `attempt` — информационное поле

### 9.4 Rate limiting

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

### 9.5 Защита от replay-атак

- `Idempotency-Key` на всех мутирующих запросах
- Сервер хранит использованные ключи 24 часа
- Повторный запрос с тем же ключом возвращает сохранённый результат

---

## 10. Клиентская архитектура

### 10.1 Двойные реализации сервисов

Каждый сервис, затронутый серверным взаимодействием, получает две реализации за одним интерфейсом:

```txt
ISaveService
├── LocalSaveService       (существующий, сохраняет в persistentDataPath)
└── CloudSaveService       (новый, обёртка над REST + локальный кеш)

IEconomyService
├── LocalEconomyService    (существующий, работает с PlayerSaveData в памяти)
└── ServerEconomyService   (новый, проксирует транзакции через REST)

ILivesService
├── LocalLivesService      (существующий, таймер по локальным часам)
└── ServerLivesService     (новый, таймер по серверному времени)

IShopService
├── LocalShopService       (кешированный каталог + локальные покупки)
└── ServerShopService      (REST каталог + серверные покупки)
```

### 10.2 Составной сервис (рекомендуемый паттерн)

Вместо выбора «или локальный, или серверный» — использовать составной сервис, который делегирует:

```csharp
public class HybridSaveService : ISaveService
{
    private readonly LocalSaveService _local;
    private readonly CloudSaveClient _cloud;
    private readonly SyncQueue _syncQueue;

    public PlayerSaveData Load()
    {
        // 1. Загрузить локальное
        // 2. Попробовать загрузить серверное (async, не блокировать)
        // 3. При наличии обоих — мерж
        // 4. При отсутствии сети — вернуть локальное
    }

    public void Save(PlayerSaveData data)
    {
        // 1. Сохранить локально (мгновенно)
        // 2. Поставить в очередь на облачную синхронизацию
    }
}
```

### 10.3 NetworkMonitor

Утилита для отслеживания состояния сети:

```csharp
public class NetworkMonitor
{
    public bool IsOnline { get; }
    public event Action<bool> OnConnectivityChanged;
}
```

Используется всеми Hybrid-сервисами для принятия решения: отправлять запрос или ставить в очередь.

### 10.4 Новые файлы (влияние на структуру папок)

```txt
Assets/Scripts/Infrastructure/
├── Network/
│   ├── ApiClient.cs              — HTTP-клиент (UnityWebRequest wrapper)
│   ├── ApiEndpoints.cs           — константы URL
│   ├── NetworkMonitor.cs         — отслеживание состояния сети
│   ├── SyncQueue.cs              — очередь отложенных операций
│   └── TokenManager.cs           — хранение и обновление JWT
├── Save/
│   ├── ISaveService.cs           — (существующий)
│   ├── LocalSaveService.cs       — (переименованный SaveService)
│   ├── CloudSaveClient.cs        — REST-клиент облачных сохранений
│   ├── HybridSaveService.cs      — составной сервис
│   └── SaveMerger.cs             — логика мержа конфликтов
└── Auth/
    └── AuthService.cs            — регистрация, refresh, link
```

### 10.5 Обновлённый BootInitializer (порядок инициализации)

```md
1. NetworkMonitor (проверить сеть)
2. AuthService (register / refresh token)
3. LocalSaveService (загрузить локальное сохранение)
4. CloudSaveClient (попробовать загрузить облачное, мерж)
5. HybridSaveService → ServiceLocator.Register<ISaveService>()
6. ContentService (проверить contentVersion, скачать diff)
7. HybridEconomyService → ServiceLocator.Register<IEconomyService>()
8. HybridLivesService → ServiceLocator.Register<ILivesService>()
9. HybridShopService → ServiceLocator.Register<IShopService>()
10. ... остальные сервисы (TimerService, FeedbackService, UIService, AnalyticsService)
11. SceneFlowManager.LoadScene("Hub")
```

---

## 11. Влияние на задачи (Tasks.md)

### Изменения в существующих задачах

| Задача | Изменение                                                                                         |
| ------ | ------------------------------------------------------------------------------------------------- |
| 0.1    | Добавить папки `Scripts/Infrastructure/Network/`, `Scripts/Infrastructure/Auth/`                  |
| 1.5    | `BootInitializer` — расширить порядок инициализации (см. 10.5)                                    |
| 2.1    | `SaveService` → `LocalSaveService`. Добавить `CloudSaveClient`, `HybridSaveService`, `SaveMerger` |
| 2.3    | `EconomyService` → `LocalEconomyService`. Добавить `ServerEconomyService`, `HybridEconomyService` |
| 2.4    | `LivesService` → `LocalLivesService`. Добавить `ServerLivesService`, `HybridLivesService`         |
| 2.12   | Обновить `BootInitializer` для регистрации Hybrid-сервисов и сетевых компонентов                  |
| 3.9    | SO-ассеты уровней дублируются как bundled JSON fallback                                           |
| 4.3    | `ShopService` → `LocalShopService`. Добавить `ServerShopService`, `HybridShopService`             |
| 4.8    | `AnalyticsService` → REST-батчи вместо (или в дополнение к) Firebase                              |

### Новые задачи

| ID   | Задача                                               | Фаза | Зависимости     |
| ---- | ---------------------------------------------------- | ---- | --------------- |
| 1.12 | `ApiClient`, `NetworkMonitor`, `TokenManager`        | 1    | 0.1, 1.1        |
| 1.13 | `AuthService` (register, refresh, link)              | 1    | 1.12            |
| 2.1a | `CloudSaveClient`, `HybridSaveService`, `SaveMerger` | 2    | 2.1, 1.12, 1.13 |
| 2.3a | `ServerEconomyService`                               | 2    | 2.3, 1.12       |
| 2.4a | `ServerLivesService`                                 | 2    | 2.4, 1.12       |
| 2.13 | `ContentService` (remote config загрузчик)           | 2    | 1.12, 1.3       |
| 4.3a | `ServerShopService`                                  | 4    | 4.3, 1.12       |
| 4.8a | REST analytics sender + offline queue                | 4    | 4.8, 1.12       |

---

## Приложение A — Sequence Diagrams

### A.1 Boot Flow

```txt
Client                            Server
  │                                  │
  ├─ POST /auth/register ──────────►│
  │◄─── 200 { accessToken } ────────┤
  │                                  │
  ├─ GET /save ─────────────────────►│
  │◄─── 200 { serverSave } ─────────┤
  │                                  │
  │  (merge local + server)          │
  │                                  │
  ├─ GET /content/manifest ─────────►│
  │◄─── 200 { contentVersion } ──────┤
  │                                  │
  │  (if new version: GET sectors,   │
  │   levels for changed sectors)    │
  │                                  │
  ├─ GET /lives ────────────────────►│
  │◄─── 200 { currentLives } ────────┤
  │                                  │
  │  (initialize services, load Hub) │
  │                                  │
```

### A.2 Level Completion Flow

```txt
Client                            Server
  │                                  │
  │  (player submits answer)         │
  │                                  │
  │  LOCAL: check answer             │
  │  LOCAL: show instant feedback    │
  │  LOCAL: update save locally      │
  │                                  │
  ├─ POST /check/level ────────────►│
  │◄─── 200 { result, newSaveVer } ──┤
  │                                  │
  │  (reconcile if mismatch)         │
  │  (use newSaveVersion for PUT)    │
  │                                  │
  ├─ PUT /save ─────────────────────►│
  │◄─── 200 { version } ─────────────┤
  │                                  │
  ├─ POST /analytics/events ────────►│
  │◄─── 202 Accepted ────────────────┤
  │                                  │
```

### A.3 Purchase Flow

```txt
Client                            Server
  │                                  │
  │  (player taps "Buy")            │
  │                                  │
  ├─ POST /shop/purchase ──────────►│
  │◄─── 200 { purchased, balance } ──┤
  │                                  │
  │  LOCAL: update fragment balance  │
  │  LOCAL: add item to ownedItems   │
  │                                  │
  ├─ PUT /save ─────────────────────►│
  │◄─── 200 { version } ─────────────┤
  │                                  │
```

### A.4 Offline → Online Sync

```txt
Client                            Server
  │                                  │
  │  (network restored)             │
  │                                  │
  ├─ POST /auth/refresh ───────────►│
  │◄─── 200 { accessToken } ────────┤
  │                                  │
  │  (process sync queue FIFO,       │
  │   each operation independently)  │
  │                                  │
  ├─ POST /check/level ────────────►│  (queued level result #1)
  │◄─── 200 { result, newSaveVer } ──┤
  │                                  │
  ├─ POST /check/level ────────────►│  (queued level result #2)
  │◄─── 200 { result, newSaveVer } ──┤
  │                                  │
  ├─ POST /shop/purchase ──────────►│  (queued purchase, cachedPrice)
  │◄─── 200 ─────────────────────────┤
  │                                  │
  ├─ PUT /save ────────────────────►│  (final state)
  │◄─── 200 { version } ─────────────┤
  │                                  │
  ├─ POST /analytics/events ───────►│  (buffered events)
  │◄─── 202 ─────────────────────────┤
  │                                  │
```
