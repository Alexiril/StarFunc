# Архитектура проекта STAR FUNC

> Клиентская архитектура мобильного приложения на Unity.
> Спецификация API описана в [API.md](API.md). Серверная архитектура — в [Server architecture.md](Server%20architecture.md).
> Раздел [API.md §10](API.md#10-клиентская-архитектура) дополняет этот документ Hybrid-сервисами, сетевым слоем и очередью синхронизации.

## Техническая спецификация

| Параметр                   | Значение                                                        |
| -------------------------- | --------------------------------------------------------------- |
| Движок                     | Unity 6 (6000.4.0f1)                                            |
| Рендер-пайплайн            | URP 17.4.0                                                      |
| Платформа                  | Мобильные устройства (Android — Google Play, iOS — планируется) |
| Ввод                       | Unity Input System 1.19.0                                       |
| UI-фреймворк               | uGUI 2.0.0                                                      |
| 2D-пакеты                  | Sprite, SpriteShape, 2D Animation, Tilemap                      |
| Минимальная версия Android | API 24 (Android 7.0) рекомендуется                              |
| Ориентация                 | Portrait                                                        |
| Целевое разрешение         | 1080×2340 (основное), 750×1334 (альтернативное)                 |

---

## 1. Высокоуровневая архитектура

Проект строится на паттерне **слоёной архитектуры** с чётким разделением ответственности:

```md
┌─────────────────────────────────────────────────────┐
│ PRESENTATION LAYER │
│ UI Screens · HUD · Animations · Visual Effects │
├─────────────────────────────────────────────────────┤
│ GAME LAYER │
│ Level Logic · Coordinate Plane · Star Entities │
│ Function Rendering · Answer System · Validation │
├─────────────────────────────────────────────────────┤
│ SYSTEMS LAYER │
│ Progression · Economy · Lives · Timer · Audio │
├─────────────────────────────────────────────────────┤
│ DATA LAYER │
│ Level Data · Player Save · Config · Localization │
├─────────────────────────────────────────────────────┤
│ INFRASTRUCTURE LAYER │
│ Scene Management · Save/Load · Asset Management │
│ Analytics │
└─────────────────────────────────────────────────────┘
```

### Ключевые принципы

- **ScriptableObject-driven data** — данные уровней, секторов, функций и конфигурации хранятся в ScriptableObject, а не захардкожены.
- **Событийная архитектура** — взаимодействие между системами через `ScriptableObject`-события или `UnityEvent`/`C# event`, а не прямые ссылки.
- **Dependency Injection через сервис-локатор** — глобальные системы (прогрессия, экономика, аудио, сохранения) регистрируются в сервис-локаторе и доступны через интерфейсы.
- **Отделение данных от логики** — конфигурация (SO) → логика (сервисы) → представление (MonoBehaviour/UI).

---

## 2. Структура папок проекта

```md
Assets/
├── / # Корень проектных файлов
│ ├── Prefabs/ # Все префабы
│ │ ├── UI/ # Префабы экранов и UI-элементов
│ │ ├── Gameplay/ # Звезды, графики, координатная сетка
│ │ └── Effects/ # Визуальные эффекты (VFX, particles)
│ │
│ ├── Scenes/ # Сцены
│ │ ├── Boot.unity # Начальная сцена (инициализация)
│ │ ├── Hub.unity # Хаб/карта галактики
│ │ └── Level.unity # Универсальная игровая сцена (аддитивная)
│ │
│ ├── Scripts/ # Исходный код (C#)
│ │ ├── Core/ # Ядро: сервис-локатор, события, утилиты
│ │ ├── Data/ # Модели данных и ScriptableObject
│ │ ├── Gameplay/ # Игровая логика уровня
│ │ ├── Meta/ # Мета-системы (прогрессия, экономика, жизни)
│ │ ├── UI/ # Код UI-экранов и виджетов
│ │ └── Infrastructure/ # Сохранения, загрузка, аналитика
│ │
│ ├── ScriptableObjects/ # Экземпляры SO (данные)
│ │ ├── Levels/ # Данные уровней (по секторам)
│ │ ├── Sectors/ # Конфигурация секторов
│ │ ├── Functions/ # Определения математических функций
│ │ ├── Config/ # Глобальные настройки баланса
│ │ └── Events/ # SO-события
│ │
│ ├── Art/ # Художественные ресурсы
│ │ ├── Sprites/ # Спрайты (звезды, UI, иконки)
│ │ ├── Animations/ # Анимационные клипы и контроллеры
│ │ ├── Materials/ # Материалы
│ │ ├── Shaders/ # Кастомные шейдеры / Shader Graph
│ │ └── Fonts/ # Шрифты (максимум 2)
│ │
│ ├── Audio/ # Звук
│ │ ├── Music/ # Фоновая музыка
│ │ └── SFX/ # Звуковые эффекты
│ │
│ └── Resources/ # Ресурсы для динамической загрузки
│ └── Localization/ # Локализация (если потребуется)
│
├── Plugins/ # Нативные плагины / SDK
├── Settings/ # URP Assets, Volume Profiles
└── TextMesh Pro/ # TMP ресурсы
```

---

## 3. Сцены и навигация

### 3.1 Схема сцен

```md
Boot → Hub ⇄ Level (additive)
```

> Сюжетные вставки реализуются как `CutscenePopup` (UI-попап поверх текущего экрана),
> а не как отдельная сцена.
>
> Level-сцена загружается **аддитивно** поверх Hub-сцены (`LoadSceneMode.Additive`).
> Это обеспечивает бесшовный переход: Hub UI скрывается, Level-контент появляется;
> при выходе из уровня Level-сцена выгружается, Hub UI восстанавливается.
> Экран загрузки реализован как `LoadingOverlay` — UI-оверлей на DontDestroyOnLoad-объекте,
> а не как отдельная сцена.

| Сцена     | Назначение                                                                                                                           |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Boot**  | Инициализация сервисов, загрузка сохранений, проверка версий. Автоматический переход в Hub.                                          |
| **Hub**   | Карта галактики. Навигация по секторам, отображение прогрессии, сюжетные вставки, магазин. Остаётся загруженной при входе в уровень. |
| **Level** | Универсальная сцена для всех 100 уровней. Загружается аддитивно поверх Hub, выгружается при выходе.                                  |

### 3.2 Менеджер сцен — `SceneFlowManager`

- Управляет загрузкой/выгрузкой сцен через `SceneManager.LoadSceneAsync`
- При входе в уровень: загружает Level-сцену аддитивно (`LoadSceneMode.Additive`), скрывает Hub UI
- При выходе из уровня: выгружает Level-сцену (`SceneManager.UnloadSceneAsync`), показывает Hub UI
- Показывает `LoadingOverlay` (DontDestroyOnLoad UI-оверлей) при необходимости
- Гарантирует порядок инициализации сервисов

---

## 4. Ядро (Core)

### 4.1 Сервис-локатор — `ServiceLocator`

```md
ServiceLocator
├── Register<T>(T instance)
├── Get<T>() : T
└── Contains<T>() : bool
```

Инициализируется в Boot-сцене. Регистрирует все глобальные сервисы:

| Сервис                 | Интерфейс              | Ответственность                                             |
| ---------------------- | ---------------------- | ----------------------------------------------------------- |
| `HybridSaveService`    | `ISaveService`         | Сохранение/загрузка данных игрока (локально + облако)       |
| `ProgressionService`   | `IProgressionService`  | Состояние прогрессии: звёзды, секторы, открытые уровни      |
| `HybridEconomyService` | `IEconomyService`      | Фрагменты: начисление, расход, баланс (локально + сервер)   |
| `HybridLivesService`   | `ILivesService`        | Жизни: списание, восстановление, таймер (локально + сервер) |
| `AudioService`         | `IAudioService`        | Воспроизведение музыки и SFX                                |
| `UIService`            | `IUIService`           | Управление UI-экранами (стек)                               |
| `AnalyticsService`     | `IAnalyticsService`    | Отправка аналитических событий                              |
| `TimerService`         | `ITimerService`        | Таймер уровня (отображение + аналитика)                     |
| `HybridShopService`    | `IShopService`         | Магазин: покупка предметов (локально + сервер)              |
| `NotificationService`  | `INotificationService` | Бэйджи и уведомления о новом контенте                       |
| `FeedbackService`      | `IFeedbackService`     | Обратная связь: звук + вибрация + VFX                       |

> **Примечание:** Hybrid-сервисы делегируют вызовы локальной и серверной реализациям в зависимости от состояния сети. Подробности в [API.md §10](API.md#10-клиентская-архитектура).

### 4.2 Событийная система — `GameEvent` (ScriptableObject)

```csharp
// ScriptableObject-событие без параметров
[CreateAssetMenu(menuName = "StarFunc/Events/GameEvent")]
public class GameEvent : ScriptableObject { ... }

// ScriptableObject-событие с параметрами
[CreateAssetMenu(menuName = "StarFunc/Events/GameEvent<T>")]
public class GameEvent<T> : ScriptableObject { ... }
```

**Список ключевых событий:**

| Событие                   | Параметр         | Описание                               |
| ------------------------- | ---------------- | -------------------------------------- |
| `OnLevelStarted`          | `LevelData`      | Уровень начат                          |
| `OnLevelCompleted`        | `LevelResult`    | Уровень завершён                       |
| `OnLevelFailed`           | —                | Уровень провален                       |
| `OnStarCollected`         | `StarData`       | Звезда правильно размещена             |
| `OnStarRejected`          | `StarData`       | Звезда размещена неверно               |
| `OnAnswerSelected`        | `AnswerData`     | Игрок выбрал ответ                     |
| `OnAnswerConfirmed`       | `bool`           | Ответ подтверждён (верный/неверный)    |
| `OnFunctionChanged`       | `FunctionParams` | Параметры функции изменены             |
| `OnGraphUpdated`          | —                | График перестроен                      |
| `OnSectorUnlocked`        | `SectorData`     | Новый сектор открыт                    |
| `OnSectorCompleted`       | `SectorData`     | Сектор завершён                        |
| `OnConstellationRestored` | `SectorData`     | Созвездие визуально восстановлено      |
| `OnLivesChanged`          | `int`            | Количество жизней изменилось           |
| `OnFragmentsChanged`      | `int`            | Количество фрагментов изменилось       |
| `OnActionUndone`          | —                | Игрок отменил последнее действие       |
| `OnLevelReset`            | —                | Уровень сброшен к начальному состоянию |
| `OnLevelSkipped`          | `LevelData`      | Уровень пропущен за фрагменты          |
| `OnGhostEmotionChanged`   | `GhostEmotion`   | Эмоция персонажа изменилась            |

---

## 5. Данные (Data Layer)

### 5.1 Конфигурация секторов — `SectorData` (SO)

```csharp
[CreateAssetMenu(menuName = "StarFunc/Data/SectorData")]
public class SectorData : ScriptableObject
{
    [Header("Identity")]
    public string SectorId;
    public string DisplayName;                    // «Созвездие первого следа» и т.д.
    public int SectorIndex;                       // 0..4

    [Header("Levels")]
    public LevelData[] Levels;                    // 20 уровней

    [Header("Unlock Conditions")]
    public SectorData PreviousSector;             // null для первого
    public int RequiredStarsToUnlock;             // порог звёзд для открытия
    // Контрольный уровень всегда 19-й (index 18) — фиксировано в шаблоне сектора

    [Header("Visual")]
    public Sprite ConstellationSprite;            // Спрайт созвездия (пустое/восстановленное)
    public Sprite ConstellationRestoredSprite;    // Спрайт восстановленного созвездия
    public Sprite SectorIcon;                     // Иконка на карте
    public Color AccentColor;                     // Секторная вариация LINE_PRIMARY
    public Color StarColor;                       // Секторная вариация POINT_PRIMARY
    public float[] ConstellationStarAngles;       // Углы/позиции для анимации восстановления

    [Header("Narrative")]
    public CutsceneData IntroCutscene;            // Сюжетная вставка при входе
    public CutsceneData OutroCutscene;            // Сюжетная вставка при завершении
}
```

### 5.2 Конфигурация уровней — `LevelData` (SO)

```csharp
[CreateAssetMenu(menuName = "StarFunc/Data/LevelData")]
public class LevelData : ScriptableObject
{
    [Header("Identity")]
    public string LevelId;
    public int LevelIndex;                        // 0..19 внутри сектора

    [Header("Type")]
    public LevelType Type;                        // Normal, Tutorial, Bonus, Control, Final

    [Header("Coordinate Plane")]
    public Vector2 PlaneMin;                      // Границы видимой области
    public Vector2 PlaneMax;
    public float GridStep;                        // Шаг сетки

    [Header("Stars")]
    public StarConfig[] Stars;                    // Набор звёзд уровня

    [Header("Task")]
    public TaskType TaskType;                     // ChooseCoordinate, ChooseFunction,
                                                  // AdjustGraph, BuildFunction
    public FunctionDefinition[] ReferenceFunctions; // Эталонные функции (одна или несколько для смешанных уровней Сектора 5)
    public AnswerOption[] AnswerOptions;           // Варианты ответов (для режима выбора)

    [Header("Validation")]
    public float AccuracyThreshold;               // Допустимый порог ошибки
    public StarRatingConfig StarRating;            // Условия для 0/1/2/3 звёзд

    [Header("Constraints")]
    public int MaxAttempts;                        // Макс. попыток на уровне (0 = бесконечно)
    public int MaxAdjustments;                    // Макс. перестроек графика за попытку (0 = бесконечно, для AdjustGraph/BuildFunction)

    [Header("Visibility")]
    public bool UseMemoryMode;                    // Показать эталон на время, затем скрыть (для уровней «по памяти»)
    public float MemoryDisplayDuration;           // Время показа эталона перед скрытием (сек)
    public GraphVisibilityConfig GraphVisibility; // Настройки частичной видимости графика

    [Header("Tutorial")]
    public bool ShowHints;                         // Показывать подсказки
    public HintConfig[] Hints;                     // Конфигурация подсказок

    [Header("Rewards")]
    public int FragmentReward;                     // Фрагменты за прохождение
}
```

### 5.3 Enums

```csharp
public enum LevelType
{
    Tutorial,     // Обучающий (уровни 1-2 каждого сектора)
    Normal,       // Обычный
    Bonus,        // Бонусный (не влияет на разблокировку)
    Control,      // Контрольный
    Final         // Финальный (уровень 20)
}

public enum TaskType
{
    ChooseCoordinate,    // Выбрать правильную координату
    ChooseFunction,      // Выбрать правильную функцию из вариантов
                         // (включая визуальное сравнение графиков/траекторий)
    AdjustGraph,         // Скорректировать график (слайдеры и/или прямое перетаскивание графика)
    BuildFunction,       // Самостоятельно построить функцию
    IdentifyError,       // Определить ошибку / лишнюю точку
    RestoreConstellation // Восстановить созвездие (финальный)
}

public enum StarState
{
    Hidden,       // Скрыта до определённого момента
    Active,       // Видимая, ожидает действия
    Placed,       // Правильно размещена
    Incorrect,    // Размещена неверно
    Restored      // Часть восстановленного созвездия
}

public enum SectorState
{
    Locked,       // Заблокирован
    Available,    // Доступен для прохождения
    InProgress,   // В процессе прохождения
    Completed     // Завершён, созвездие восстановлено
}
```

### 5.4 Конфигурация звезды — `StarConfig`

```csharp
[System.Serializable]
public class StarConfig
{
    public string StarId;
    public Vector2 Coordinate;            // Позиция на координатной плоскости
    public StarState InitialState;        // Начальное состояние
    public bool IsControlPoint;           // Является ли контрольной точкой графика
    public bool IsDistractor;             // Является ли звездой-дистрактором (для IdentifyError)
    public bool BelongsToSolution;        // Принадлежит ли решению (true для корректных звёзд)
    public int RevealAfterAction;         // Номер действия, после которого появляется (-1 = сразу)
}
```

### 5.5 Определение функции — `FunctionDefinition` (SO)

```csharp
[CreateAssetMenu(menuName = "StarFunc/Data/FunctionDefinition")]
public class FunctionDefinition : ScriptableObject
{
    public FunctionType Type;             // Linear, Quadratic, Sinusoidal, Mixed
    public float[] Coefficients;          // Коэффициенты: [a, b, c, ...]
                                          // Linear: y = a*x + b
                                          // Quadratic: y = a*(x-b)² + c
                                          // Sinusoidal: y = a*sin(b*x + c) + d
    public Vector2 DomainRange;           // Диапазон X для отрисовки
}

public enum FunctionType
{
    Linear,       // Сектор 1-2: y = kx + b
    Quadratic,    // Сектор 3: y = a(x-h)² + k
    Sinusoidal,   // Сектор 4: y = a*sin(bx + c) + d
    Mixed         // Сектор 5: комбинации
}
```

### 5.6 Конфигурация оценки — `StarRatingConfig`

```csharp
[System.Serializable]
public class StarRatingConfig
{
    // Оценка основана на количестве ошибок.
    // Таймер может влиять на награду — это настраивается через TimerAffectsRating.
    public int ThreeStarMaxErrors;        // Макс. ошибок для 3 звёзд (рекомендуется 0)
    public int TwoStarMaxErrors;          // Макс. ошибок для 2 звёзд
    public int OneStarMaxErrors;          // Макс. ошибок для 1 звезды
    // 0 звёзд = уровень не пройден (все попытки исчерпаны или ошибок > OneStarMaxErrors)
    public bool TimerAffectsRating;       // Влияет ли таймер на звёздный рейтинг (по умолчанию false)
    public float ThreeStarMaxTime;        // Макс. время для 3 звёзд (если TimerAffectsRating = true)
}
```

### 5.7 Конфигурация подсказок — `HintConfig`

```csharp
[System.Serializable]
public class HintConfig
{
    public HintTrigger Trigger;               // Когда показывать подсказку
    public string HintText;                   // Текст подсказки
    public Vector2 HighlightPosition;         // Позиция выделения на экране (для туториалов)
    public int TriggerAfterErrors;            // Показать после N ошибок (для Trigger = AfterErrors)
}

public enum HintTrigger
{
    OnLevelStart,     // При входе на уровень
    AfterErrors,      // После N ошибок
    OnFirstInteraction // При первом взаимодействии с элементом
}
```

> **Два типа подсказок (важно не путать):**
>
> 1. **Авто-подсказки (бесплатные)** — настраиваются в `LevelData.Hints[]` и управляются флагом `ShowHints`. Срабатывают автоматически по триггеру (`OnLevelStart`, `AfterErrors`, `OnFirstInteraction`). Не расходуют никаких ресурсов игрока. Используются в обучающих и ранних уровнях для мягкого направления.
> 2. **Покупные подсказки (расходуемые)** — хранятся в `PlayerSaveData.Consumables["hints"]`. Активируются нажатием кнопки `HintButton` в HUD. При нажатии расходуется одна единица. Покупаются в магазине за фрагменты.
>
> Авто-подсказки **не** расходуют покупные подсказки. Это две независимые системы.

### 5.8 Конфигурация видимости графика — `GraphVisibilityConfig`

```csharp
[System.Serializable]
public class GraphVisibilityConfig
{
    public bool PartialReveal;                // Показать график частично (постепенное раскрытие)
    public int InitialVisibleSegments;        // Кол-во видимых сегментов при старте
    public int RevealPerCorrectAction;        // Сколько сегментов раскрывать за правильное действие
}
```

### 5.9 Конфигурация сюжетных вставок — `CutsceneData` (SO)

```csharp
[CreateAssetMenu(menuName = "StarFunc/Data/CutsceneData")]
public class CutsceneData : ScriptableObject
{
    public string CutsceneId;
    public CutsceneFrame[] Frames;                // Последовательность кадров
}

[System.Serializable]
public class CutsceneFrame
{
    public Sprite Background;                     // Фоновое изображение кадра
    public Sprite CharacterSprite;                // Спрайт персонажа (Ghost)
    public GhostEmotion Emotion;                  // Эмоция призрака на этом кадре
    [TextArea] public string Text;                // Текст/диалог
    public float Duration;                        // Длительность кадра (0 = ждать tap)
    public AnimationClip FrameAnimation;          // Опциональная анимация
}

public enum GhostEmotion
{
    Idle,         // Нейтральное состояние
    Happy,        // Радость (успешное действие)
    Sad,          // Грусть (ошибка или потеря)
    Excited,      // Волнение (разблокировка, открытие)
    Determined    // Решительность (контрольный уровень)
}
```

### 5.10 Данные сохранения — `PlayerSaveData`

```csharp
[System.Serializable]
public class PlayerSaveData
{
    // Версионирование
    public int SaveVersion;                    // Версия формата сохранения (для миграции)
    public int Version;                        // Монотонно возрастающий счётчик (optimistic lock)
    public long LastModified;                  // Unix timestamp последнего изменения

    // Прогрессия
    public Dictionary<string, SectorProgress> SectorProgress;
    public Dictionary<string, LevelProgress> LevelProgress;
    public int CurrentSectorIndex;

    // Экономика
    public int TotalFragments;

    // Жизни
    public int CurrentLives;
    public long LastLifeRestoreTimestamp;   // Unix timestamp

    // Магазин
    public List<string> OwnedItems;        // Купленные перманентные предметы (скины, темы)

    // Расходуемые предметы
    public Dictionary<string, int> Consumables; // e.g. { "hints": 5 }

    // Статистика
    public int TotalLevelsCompleted;
    public int TotalStarsCollected;
    public float TotalPlayTime;
}

[System.Serializable]
public class SectorProgress
{
    public SectorState State;
    public int StarsCollected;             // Всего звёзд в секторе
    public bool ControlLevelPassed;
}

[System.Serializable]
public class LevelProgress
{
    public bool IsCompleted;
    public int BestStars;                  // 0-3
    public float BestTime;
    public int Attempts;
}
```

---

## 6. Игровой слой (Gameplay)

### 6.1 Координатная плоскость — `CoordinatePlane`

Основной игровой объект, отвечающий за отрисовку и взаимодействие с координатной сеткой.

```md
CoordinatePlane (MonoBehaviour)
├── GridRenderer — отрисовка сетки (LineRenderer / GL / SpriteShape)
├── AxisRenderer — отрисовка осей X/Y с метками
├── CoordinateLabeler — отображение числовых меток на осях
├── TouchInputHandler — обработка touch-ввода на плоскости
└── PlaneCamera — управление масштабом/смещением (если нужно)
```

**Ответственность:**

- Отрисовка координатной сетки с настраиваемым шагом
- Отрисовка осей X/Y
- Преобразование экранных координат в координаты плоскости и обратно
- Поддержка масштабирования (pinch-to-zoom) если требуется
- Визуальная проверка правильности расположения (highlight при верном/неверном позиционировании)

### 6.2 Звезда — `StarEntity`

```md
StarEntity (MonoBehaviour)
├── StarConfig — данные (координата, состояние)
├── StarVisuals — визуал (спрайт, glow, анимации)
├── StarAnimator — анимации (появление, установка, ошибка)
└── StarInteraction — обработка взаимодействия (tap, drag)
```

**Состояния и переходы:**

```md
Hidden → Active → Placed → Restored
↘ Incorrect → Active (повтор)
```

**Анимации:**

- `Appear` — появление звезды на поле (fade in + scale)
- `Place` — успешная установка (flash + glow pulse)
- `Error` — неверная установка (shake + red flash)
- `Restore` — визуальное объединение в созвездие (line draw + constellation glow)

### 6.3 Рендер графика — `GraphRenderer`

Отвечает за отрисовку математических функций в реальном времени.

```md
GraphRenderer (MonoBehaviour)
├── FunctionEvaluator — вычисление значений функции
├── CurveRenderer — отрисовка кривой (LineRenderer с сэмплами)
├── ControlPointsRenderer — отображение контрольных точек
└── ComparisonOverlay — наложение эталона для сравнения
```

**Ключевые возможности:**

- Отрисовка графика по `FunctionDefinition`
- Плавное обновление при изменении параметров (анимированный морфинг)
- Отображение нескольких графиков одновременно (для сравнения)
- Подсветка совпадения / расхождения с эталоном
- Настраиваемое количество сэмплов (для производительности на мобильных)

### 6.4 Персонаж — `GhostEntity`

Космический призрак — визуальный аватар игрока, присутствующий в хабе и на уровнях. Реагирует на действия игрока эмоциями.

```md
GhostEntity (MonoBehaviour)
├── GhostVisuals — спрайт, glow-эффект, анимационный контроллер
├── GhostAnimator — управление анимациями (idle, happy, sad, excited)
├── GhostEmotionController — выбор эмоции на основе игровых событий
└── GhostPositioner — позиционирование в хабе / на уровне
```

**Эмоции и триггеры:**

| Эмоция       | Триггер                                           |
| ------------ | ------------------------------------------------- |
| `Idle`       | Ожидание действия, навигация по хабу              |
| `Happy`      | Правильный ответ, сбор звезды, прохождение уровня |
| `Sad`        | Ошибка, потеря жизни                              |
| `Excited`    | Разблокировка сектора, восстановление созвездия   |
| `Determined` | Вход в контрольный уровень                        |

**Присутствие:**

- **Хаб:** привязан к текущей позиции игрока на карте, реагирует на навигацию
- **Уровень:** отображается рядом с игровым полем (вне зоны взаимодействия)
- **Катсцены:** центральный персонаж, спрайт и эмоция задаются через `CutsceneFrame`
- **Нарратив:** связующий элемент сюжета — путешествие призрака по разрушенному созвездию

### 6.5 Контроллер уровня — `LevelController`

Центральный контроллер, управляющий логикой прохождения одного уровня.

```md
LevelController (MonoBehaviour)
├── LevelData — загруженные данные уровня
├── CoordinatePlane — ссылка на координатную плоскость
├── StarManager — управление звёздами на уровне
├── AnswerSystem — система ответов (выбор/построение)
├── ValidationSystem — проверка правильности решения
├── LevelTimer — таймер уровня (отображение/аналитика)
├── ErrorCounter — счётчик ошибок до отправки (errorsBeforeSubmit)
├── HintSystem — система подсказок
├── ActionHistory — стек действий для Undo/Reset
└── LevelResultCalculator — подсчёт результата (звёзды)
```

**ErrorCounter (errorsBeforeSubmit):**

- Считает количество ошибок игрока до отправки ответа на сервер
- Инкрементируется при каждом неверном действии (неверное размещение звезды, неверный выбор и т.д.)
- Сбрасывается при `ResetLevel()`
- Передаётся серверу в `POST /check/level` как `errorsBeforeSubmit` — доверенное поле, используемое для расчёта звёздного рейтинга (см. [API.md §6.7](API.md#67-level-check))

````txt

**Undo / Reset:**

- `ActionHistory` хранит стек действий игрока (`Stack<PlayerAction>`)
- `Undo()` — откат последнего действия (снятие размещённой звезды, откат параметра функции)
- `ResetLevel()` — полный сброс уровня к начальному состоянию (без списания жизни)
- Кнопки `UndoButton` и `ResetButton` в HUD привязаны к этим методам

**Жизненный цикл уровня:**

```md
Initialize → ShowTask → AwaitInput → ValidateAnswer
↑ ↓
└── (error) ← DeductLife/Retry
↓
(success)
↓
CalculateResult → ShowResult → Complete
````

### 6.6 Система ответов — `AnswerSystem`

Адаптируется в зависимости от `TaskType` уровня:

| TaskType               | UI компонент                                                                  | Логика                           |
| ---------------------- | ----------------------------------------------------------------------------- | -------------------------------- |
| `ChooseCoordinate`     | Список из 3-5 вариантов координат                                             | Выбор одного варианта            |
| `ChooseFunction`       | Список из 3-5 вариантов функций/траекторий (с визуальным сравнением графиков) | Выбор одного варианта            |
| `AdjustGraph`          | Слайдеры параметров и/или прямое перетаскивание кривой                        | Настройка k, b, a, h и др.       |
| `BuildFunction`        | Полный ввод параметров + drag-точки                                           | Свободное построение             |
| `IdentifyError`        | Tap на звёзды для выбора «лишней»                                             | Выбор одной или нескольких звёзд |
| `RestoreConstellation` | Последовательное размещение звёзд                                             | Пошаговое восстановление         |

### 6.7 Система валидации — `ValidationSystem`

```csharp
public class ValidationSystem
{
    // Проверка координаты
    bool ValidateCoordinate(Vector2 selected, Vector2 expected, float threshold);

    // Проверка функции (сравнение графиков)
    bool ValidateFunction(FunctionDefinition player, FunctionDefinition reference, float threshold);

    // Проверка набора контрольных точек
    ValidationResult ValidateControlPoints(StarConfig[] placed, StarConfig[] reference);

    // Общая проверка решения уровня
    LevelResult ValidateLevel(LevelData level, PlayerAnswer answer);
}
```

**Допуск ошибки:** настраивается через `AccuracyThreshold` в `LevelData`. Для сравнения графиков используется среднеквадратическое отклонение на контрольных точках.

---

## 7. Мета-системы (Meta Layer)

### 7.1 Система прогрессии — `ProgressionService`

```csharp
public interface IProgressionService
{
    // Состояние секторов
    SectorState GetSectorState(string sectorId);
    bool IsSectorUnlocked(string sectorId);
    bool IsSectorCompleted(string sectorId);
    void CompleteSector(string sectorId);

    // Состояние уровней
    bool IsLevelUnlocked(string levelId);
    bool IsLevelCompleted(string levelId);
    int GetBestStars(string levelId);
    void CompleteLevel(string levelId, LevelResult result);

    // Звёзды
    int GetTotalStars();
    int GetSectorStars(string sectorId);

    // Проверка условий
    bool CanUnlockSector(string sectorId);  // Проверка порога звёзд + контрольный уровень

    // Пропуск уровня
    bool CanSkipLevel(string levelId);      // Есть ли ресурсы для пропуска
    void SkipLevel(string levelId);         // Пропуск за фрагменты (1 звезда, без награды)
}
```

**Правила разблокировки сектора:**

1. Пройден контрольный уровень предыдущего сектора (уровень 19, фиксированный индекс 18)
2. Набрано минимальное количество звёзд в предыдущем секторе (`RequiredStarsToUnlock`)

> Разблокировка следующего сектора происходит после контрольного уровня (19), но сектор считается **завершённым** (созвездие восстановлено) только после прохождения финального уровня (20).

**Правила разблокировки уровня:**

- Уровень N+1 разблокируется после прохождения уровня N
- Бонусные уровни (7 и 12) — **опциональны**: игрок может пропустить их и перейти к следующему обязательному уровню. Их результат **не учитывается** при проверке условий разблокировки следующего сектора (не влияют на звёзды для порога и не являются обязательными для «завершения сектора»)

### 7.2 Система экономики — `EconomyService`

```csharp
public interface IEconomyService
{
    int GetFragments();
    void AddFragments(int amount);
    bool SpendFragments(int amount);       // false если недостаточно
    bool CanAfford(int amount);
}
```

**Источники фрагментов:**

- Первичное прохождение уровня: согласно `FragmentReward` в `LevelData`
- Улучшение результата (больше звёзд при повторном прохождении): бонус

**Расход фрагментов:**

- Покупка подсказок
- Пропуск уровня
- Восстановление жизней (ускоренное)
- Кастомизация (если реализована)

### 7.3 Система жизней — `LivesService`

```csharp
public interface ILivesService
{
    int GetCurrentLives();
    int GetMaxLives();
    bool HasLives();
    void RestoreLife();                    // Восстановление одной жизни
    void RestoreAllLives();               // Полное восстановление
    float GetTimeUntilNextRestore();      // Таймер до след. восстановления
}
```

> Списание жизней происходит **только** внутри flow проверки ответа (`POST /check/level`). Отдельного метода `DeductLife()` нет — клиент не списывает жизни самостоятельно, а получает обновлённое количество от сервера (или из локальной валидации при offline).

**Правила:**

- Максимум жизней: настраивается (рекомендуется 5)
- Списание: **одна жизнь за попытку** (= один вызов `POST /check/level` с неверным ответом). Несколько локальных ошибок до подтверждения не расходуют жизни.
- При 0 жизней: **вход на любой уровень заблокирован**. Игрок должен либо дождаться автовосстановления, либо восстановить жизни за фрагменты.
- Восстановление: автоматически по таймеру (например, 1 жизнь за 30 минут)
- Восстановление за фрагменты: мгновенное восстановление одной жизни

### 7.4 Таймер уровня — `TimerService`

```csharp
public interface ITimerService
{
    void StartTimer();
    void StopTimer();
    void PauseTimer();
    void ResumeTimer();
    float GetElapsedTime();
}
```

**Использование:**

- Старт при входе на уровень
- Пауза при открытии меню паузы
- Остановка при завершении/провале
- Отображается в HUD для информирования игрока
- Влияние на рейтинг определяется флагом `TimerAffectsRating` в `StarRatingConfig` (по умолчанию false)
- Время записывается для аналитики и отображения на экране результата
- Используется для контроля темпа игры (аналитика), а не для давления на игрока

### 7.5 Система магазина — `ShopService`

```csharp
public interface IShopService
{
    ShopItem[] GetAvailableItems();
    bool PurchaseItem(string itemId);     // Покупка за фрагменты
    bool IsItemOwned(string itemId);
}
```

**Типы товаров:**

| Категория      | Примеры                                                                | Валюта    |
| -------------- | ---------------------------------------------------------------------- | --------- |
| Подсказки      | Пакет подсказок                                                        | Фрагменты |
| Жизни          | Мгновенное восстановление                                              | Фрагменты |
| Пропуск уровня | Пропустить текущий уровень                                             | Фрагменты |
| Кастомизация   | Скины для Ghost, темы координатной плоскости, оформление линий графика | Фрагменты |

> Конкретные цены и ассортимент определяются после заполнения раздела «Монетизация» в ГДД.

### 7.6 Система уведомлений — `NotificationService`

```csharp
public interface INotificationService
{
    bool HasNewContent(string sectorId);       // Есть ли новые доступные уровни
    bool HasUnclaimedRewards();                // Есть ли невостребованные награды
    void MarkSeen(string contentId);           // Пометить как просмотренное
    int GetBadgeCount(string context);         // Количество уведомлений для бейджа
}
```

**Триггеры уведомлений:**

- Новый сектор разблокирован → бейдж на секторе в хабе
- Жизни восстановлены → бейдж на индикаторе жизней
- Новый контент доступен после обновления

---

## 8. Система UI

### 8.1 Менеджер UI — `UIService`

Управляет стеком экранов:

```csharp
public interface IUIService
{
    void ShowScreen<T>() where T : UIScreen;
    void HideScreen<T>() where T : UIScreen;
    void ShowPopup<T>(PopupData data) where T : UIPopup;
    void HideAllPopups();
    T GetScreen<T>() where T : UIScreen;
}
```

### 8.2 Иерархия экранов

```md
UIRoot (Canvas - Screen Space Overlay)
├── ScreensLayer # Полноэкранные экраны
│ ├── HubScreen # Карта галактики
│ ├── SectorScreen # Последовательная карта уровней сектора (линейный путь)
│ ├── LevelHUD # HUD игрового уровня
│ ├── LevelResultScreen # Экран результата
│ └── ShopScreen # Магазин
│
├── PopupsLayer # Попапы (поверх экранов)
│ ├── PausePopup # Пауза
│ ├── SettingsPopup # Настройки
│ ├── NoLivesPopup # «Нет жизней»
│ ├── SectorUnlockPopup # Разблокировка сектора
│ ├── SkipLevelPopup # Подтверждение пропуска уровня (стоимость в фрагментах)
│ └── CutscenePopup # Сюжетная вставка
│
└── OverlayLayer # Постоянные элементы
├── LoadingOverlay # Экран загрузки
└── TransitionOverlay # Переходы между экранами
```

### 8.3 HUD уровня — `LevelHUD`

```md
LevelHUD
├── TopBar
│ ├── PauseButton # Кнопка паузы
│ ├── TimerDisplay # Отображение времени
│ ├── LivesDisplay # Индикатор жизней
│ └── FragmentsDisplay # Счётчик фрагментов
│
├── GameArea
│ ├── CoordinatePlane # Основное игровое поле
│ ├── StarsContainer # Контейнер для звёзд
│ └── GraphContainer # Контейнер для графиков
│
├── BottomBar
│ ├── AnswerPanel # Панель вариантов ответа
│ ├── FunctionEditor # Редактор функции (слайдеры/ввод)
│ ├── ConfirmButton # Кнопка подтверждения
│ ├── UndoButton # Кнопка отмены последнего действия
│ ├── ResetButton # Кнопка сброса уровня
│ └── HintButton # Кнопка подсказки
│
└── FeedbackOverlay
├── SuccessEffect # Эффект правильного ответа
└── ErrorEffect # Эффект ошибки
```

### 8.4 Экран результата — `LevelResultScreen`

| Элемент            | Описание                                    |
| ------------------ | ------------------------------------------- |
| Звёзды (0-3)       | Анимированное появление звёзд               |
| Время прохождения  | Отображение затраченного времени            |
| Фрагменты          | Количество заработанных фрагментов          |
| Созвездие          | Превью восстановленного фрагмента созвездия |
| Кнопка «Далее»     | Переход к следующему уровню                 |
| Кнопка «Повторить» | Повторное прохождение                       |
| Кнопка «В хаб»     | Возврат на карту                            |

### 8.5 Карта галактики — `HubScreen`

Хаб представляет собой **визуальное космическое пространство** (карта галактики), а не плоский UI-список. Прогрессия игрока визуально проявляется через постепенное восстановление созвездий на карте.

| Элемент            | Описание                                                               |
| ------------------ | ---------------------------------------------------------------------- |
| Карта секторов     | 5 секторов + хаб, соединённые визуальными линиями                      |
| Иконки секторов    | Состояния: Locked / Available / Completed                              |
| Созвездия          | Разрушенные → постепенно восстанавливаются по мере прохождения уровней |
| Персонаж (Ghost)   | Космический призрак привязан к текущей позиции на карте                |
| Счётчики           | Общие звёзды, фрагменты, жизни                                         |
| Бэйджи уведомлений | Индикаторы новых доступных уровней/секторов                            |
| Кнопки             | Настройки, магазин                                                     |

**Визуальная прогрессия на карте:**

- Каждый пройденный уровень добавляет звезду в созвездие на карте
- При завершении сектора созвездие полностью восстанавливается (анимация соединения + glow)
- Заблокированные секторы отображаются как тусклые / разрушенные контуры
- Переходы между секторами — световые линии (появляются при разблокировке)

---

## 9. Визуальная система

### 9.1 Цветовые токены (скриптовые константы)

```csharp
public static class ColorTokens
{
    public static readonly Color BG_DARK       = new Color32(7, 20, 40, 255);      // #071428
    public static readonly Color BG_SECOND     = new Color32(11, 43, 74, 255);     // #0B2B4A
    public static readonly Color LINE_PRIMARY  = new Color32(90, 232, 228, 255);   // #5AE8E4
    public static readonly Color POINT_PRIMARY = new Color32(255, 212, 122, 255);  // #FFD47A
    public static readonly Color ACCENT_PINK   = new Color32(255, 122, 198, 255);  // #FF7AC6
    public static readonly Color UI_NEUTRAL    = new Color32(230, 238, 246, 255);  // #E6EEF6
    public static readonly Color ERROR         = new Color32(255, 92, 92, 255);    // #FF5C5C
    public static readonly Color SUCCESS       = new Color32(126, 231, 135, 255);  // #7EE787
}
```

### 9.2 Визуальные правила

- Одновременно на экране не более 5 основных цветов
- Фон всегда тёмный (диапазон `BG_DARK` → `BG_SECOND`)
- Яркие цвета только для интерактивных элементов
- Секторная вариация: меняются `LINE_PRIMARY` и `POINT_PRIMARY` по акцентной палитре
- Минимальный контраст с фоном сохраняется для всех элементов
- Иконки: один стиль (stroke или filled), читаемость при 24×24 px
- Все спрайты — `@1x / @2x / @3x` для разных DPI

### 9.3 Анимации и эффекты

| Анимация                     | Описание                         | Реализация                 |
| ---------------------------- | -------------------------------- | -------------------------- |
| Сбор звезды                  | Flash + glow pulse               | Animator / DOTween         |
| Ошибка                       | Shake + red flash                | Animator / DOTween         |
| Восстановление созвездия     | Линии между звёздами, волна glow | Coroutine + LineRenderer   |
| Построение графика           | Плавная отрисовка кривой         | Coroutine + LineRenderer   |
| Переход между экранами       | Fade / slide                     | CanvasGroup + DOTween      |
| Разблокировка сектора        | Свечение + раскрытие             | Particle System + Animator |
| Отображение звёзд результата | Последовательное появление 1-3   | Animator с задержкой       |

---

## 10. Аудио-система

### 10.1 Структура `AudioService`

```md
AudioService
├── MusicPlayer # Фоновая музыка (crossfade между треками)
│ ├── Hub Theme
│ ├── Sector 1-5 Themes
│ └── Result Theme
│
└── SFXPlayer # Звуковые эффекты (пул AudioSource)
├── UI: tap, confirm, back, popup_open, popup_close
├── Gameplay: star_place, star_error, graph_draw, hint_use
├── Result: star_earn_1, star_earn_2, star_earn_3, level_complete, level_fail
└── Meta: sector_unlock, constellation_restore, fragment_earn
```

### 10.2 Правила

- Музыка: один трек на контекст (хаб / сектор / результат), crossfade при переходе
- SFX: пул из 8-12 `AudioSource` для одновременного воспроизведения
- Настройки громкости: отдельные слайдеры для музыки и SFX, сохраняются в `PlayerPrefs`
- Вибрация: короткая вибрация при ошибке (через `Handheld.Vibrate()` или Input System haptics)

### 10.3 Система обратной связи — `FeedbackService`

Объединяет все каналы обратной связи (звук, визуал, вибрация) в единую точку вызова.

```csharp
public interface IFeedbackService
{
    void PlayFeedback(FeedbackType type);  // Вызывает SFX + вибрацию + VFX одним вызовом
    void SetHapticsEnabled(bool enabled);  // Вкл/выкл вибрации (настройка игрока)
}

public enum FeedbackType
{
    StarPlaced,            // Правильное размещение: лёгкая вибрация + SFX + glow
    StarError,             // Ошибка: сильная вибрация + shake + SFX
    LevelComplete,         // Прохождение: длинная вибрация + SFX + VFX
    ConstellationRestored, // Восстановление созвездия: паттерн вибрации
    ButtonTap,             // Нажатие кнопки UI: микро-вибрация + tap SFX
    SectorUnlock           // Разблокировка сектора: паттерн вибрации + SFX + VFX
}
```

Интегрируется с `AudioService` (звук) и `GhostEmotionController` (эмоции персонажа). Настройка вибрации сохраняется в `PlayerPrefs`.

---

## 11. Сохранение данных

### 11.1 `SaveService`

```csharp
public interface ISaveService
{
    PlayerSaveData Load();
    void Save(PlayerSaveData data);
    void Delete();
    bool HasSave();
}
```

### 11.2 Реализация

- **Формат:** JSON (через `Newtonsoft.Json` — необходим для `Dictionary<string, SectorProgress>`)
- **Хранилище:** `Application.persistentDataPath` + файл `save.json`
- **Защита от потери:** запись при каждом значимом событии (завершение уровня, покупка, изменение жизней)
- **Автосохранение:** при `OnApplicationPause(true)` и `OnApplicationQuit()`
- **Целостность:** контрольная сумма для базовой защиты от ручного редактирования
- **Миграция:** версия формата в файле сохранения для совместимости при обновлениях

---

## 12. Структура секторов и уровней

### 12.1 Карта контента

| Сектор  | Название                   | Механика                           | Функции               | Уровни |
| ------- | -------------------------- | ---------------------------------- | --------------------- | ------ |
| 0 (Хаб) | Космическая мастерская     | —                                  | —                     | —      |
| 1       | Созвездие первого следа    | Выбор координат, соединение точек  | Точки, простые прямые | 20     |
| 2       | Созвездие ориентира        | Работа с прямыми, наклон, смещение | y = kx + b            | 20     |
| 3       | Созвездие сдвига           | Параболы, симметрия, вершина       | y = a(x−h)² + k       | 20     |
| 4       | Созвездие свободной формы  | Волны, амплитуда, фаза, комбинации | y = a·sin(bx + c) + d | 20     |
| 5       | Созвездие последней звезды | Смешанные задачи, все механики     | Все типы              | 20     |

**Итого:** 100 уровней (5 секторов × 20 уровней)

### 12.2 Шаблон структуры сектора (20 уровней)

| Уровни | Тип      | Описание                                    |
| ------ | -------- | ------------------------------------------- |
| 1-2    | Tutorial | Обучающие — знакомство с новой механикой    |
| 3-6    | Normal   | Основные — закрепление механики             |
| 7      | Bonus    | Бонусный — смена темпа, без штрафов         |
| 8-11   | Normal   | Усложнённые задачи                          |
| 12     | Bonus    | Бонусный — разгрузка                        |
| 13-16  | Normal   | Сложные задачи, меньше подсказок            |
| 17-18  | Normal   | Предподготовка к контрольному               |
| 19     | Control  | Контрольный — проверка усвоения             |
| 20     | Final    | Финальный — полное восстановление созвездия |

---

## 13. Игровой цикл (Game Loop)

### 13.1 Основной цикл

```md
┌─────────────────────────────────────────┐
│ ХАБ │
│ Карта ← Прогрессия ← Ресурсы ← Сюжет │
└──────────────┬──────────────────────────┘
│ Выбор сектора
▼
┌──────────────────────────────────────────┐
│ СЕКТОР │
│ Список уровней → Выбор уровня │
└──────────────┬───────────────────────────┘
│ Вход в уровень
▼
┌──────────────────────────────────────────────────┐
│ УРОВЕНЬ │
│ │
│ 1. Показать задачу (координаты, звёзды, функцию)│
│ 2. Ожидание действия игрока │
│ 3. Проверка ответа │
│ ├─ Верно → Звезда установлена │
│ └─ Неверно → Списание попытки, обратная связь│
│ 4. Все условия выполнены? │
│ ├─ Да → Подсчёт результата │
│ └─ Нет → Повтор шага 2 │
│ 5. Экран результата (звёзды, фрагменты, время) │
│ 6. Визуальное восстановление части созвездия │
│ │
└──────────────┬───────────────────────────────────┘
│ Продолжить / Повторить / В хаб
▼
┌──────────────────────────────────────────┐
│ Следующий уровень / Возврат в хаб │
│ (если сектор завершён → сюжетная вставка│
│ → восстановление созвездия на карте) │
└──────────────────────────────────────────┘
```

### 13.2 Цикл на мета-уровне

```md
Прохождение уровня
→ Получение звёзд (оценка)
→ Получение фрагментов (ресурс)
→ Обновление прогрессии
→ Проверка условий разблокировки сектора
→ (Опционально) Повторное прохождение для улучшения
```

---

## 14. Производительность и оптимизация (Mobile)

### 14.1 Целевые метрики

| Параметр             | Целевое значение     |
| -------------------- | -------------------- |
| FPS                  | 60 (стабильно)       |
| Время загрузки сцены | < 2 сек              |
| Потребление памяти   | < 300 MB             |
| Размер APK           | < 100 MB             |
| Потребление батареи  | Низкое (casual-игра) |

### 14.2 Рекомендации

- **Sprite Atlas** — все UI-спрайты и игровые объекты собраны в атласы
- **Object Pooling** — пулинг для звёзд, эффектов частиц, элементов UI-списков
- **LineRenderer оптимизация** — ограниченное количество сэмплов на кривую (50-100)
- **Canvas разделение** — отдельные Canvas для статичных и динамичных элементов UI
- **Batching** — слои сортировки настроены для минимизации draw calls
- **Загрузка** — `Addressables` или `Resources.Load` по необходимости, lazy-loading текстур секторов
- **GC-давление** — минимизация аллокаций в Update-цикле, кэширование

### 14.3 URP-настройки для мобильных

- Отключить HDR в URP Asset (если не требуется для glow-эффектов)
- Отключить MSAA или ограничить 2x
- Отключить Depth/Opaque Textures если не используются
- Использовать 2D Renderer
- Шейдеры: Sprite-Lit-Default или кастомные Shader Graph для glow/bloom

---

## 15. Диаграмма зависимостей компонентов

```md
                    ┌─────────────┐
                    │   Boot      │
                    │  (Scene)    │
                    └──────┬──────┘
                           │ инициализирует
                           ▼
                    ┌─────────────┐
                    │  Service    │
                    │  Locator    │
                    └──────┬──────┘
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
    ┌────────────┐  ┌────────────┐  ┌────────────┐
    │ SaveService│  │AudioService│  │ UIService   │
    └─────┬──────┘  └────┬───────┘  └──────┬─────┘
          │              │                 │
    ┌─────┴──────────────┴────┐            │
    ▼               ▼        ▼             ▼

┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
│Progression│ │ Economy │ │ Lives │ │ Feedback │
│ Service │ │ Service │ │ Service │ │ Service │
└──────┬───┘ └────┬─────┘ └──────────┘ └──────────┘
│ │
│ ┌────┴──────────────┐
│ ▼ ▼
│ ┌────────────┐ ┌──────────────┐
│ │ShopService │ │Notification │
│ │ │ │Service │
│ └────────────┘ └──────────────┘
│
│ читает/пишет
▼
┌──────────────┐ ┌──────────────┐
│ SectorData │────▶│ LevelData │
│ (SO) │ │ (SO) │
└──────────────┘ └──────┬───────┘
│ конфигурирует
▼
┌───────────────┐
│LevelController│
└───────┬───────┘
┌────────────┼────────────┐
▼ ▼ ▼
┌──────────┐ ┌──────────┐ ┌──────────┐
│Coordinate│ │ Answer │ │Validation│
│ Plane │ │ System │ │ System │
└──────────┘ └──────────┘ └──────────┘
│
┌──────┴──────┐
▼ ▼
┌──────────┐ ┌──────────┐
│StarEntity│ │ Graph │
│ │ │ Renderer │
└──────┬───┘ └──────────┘
│
▼
┌───────────┐
│GhostEntity│
└───────────┘
```

---

## 16. Порядок реализации (Roadmap)

### Фаза 1: Ядро и прототип

1. Структура папок и базовая конфигурация проекта
2. Сервис-локатор и событийная система
3. SceneFlowManager (Boot → Hub, аддитивная загрузка Level)
4. `CoordinatePlane` — отрисовка сетки и осей
5. `StarEntity` — базовая звезда с состояниями
6. `GhostEntity` — персонаж с эмоциями (базовые состояния)
7. `LevelData` SO — минимальная конфигурация первого уровня
8. `LevelController` — простейший цикл: показать задачу → проверить ответ
9. `AnswerSystem` — режим `ChooseCoordinate` (выбор из вариантов)
10. `ValidationSystem` — проверка координат
11. Минимальный HUD: таймер, кнопка подтверждения, варианты ответа, Undo/Reset

### Фаза 2: Основные системы

12. `SaveService` — загрузка/сохранение прогресса
13. `ProgressionService` — звёзды, уровни, секторы, пропуск уровней
14. `EconomyService` — фрагменты
15. `LivesService` — жизни и восстановление
16. `TimerService` — таймер уровня (аналитика + отображение)
17. `FeedbackService` — обратная связь (звук + вибрация + VFX)
18. `LevelResultScreen` — экран результата с анимацией звёзд
19. `HubScreen` — карта галактики с секторами и визуальным восстановлением
20. `SectorScreen` — последовательная карта уровней (линейный путь, не свободный выбор)
21. `GraphRenderer` — отрисовка линейных функций
22. `AnswerSystem` — режим `ChooseFunction`

### Фаза 3: Расширение механик

23. `AnswerSystem` — режим `AdjustGraph` (корректировка графика)
24. `GraphRenderer` — параболы (`Quadratic`)
25. `GraphRenderer` — синусоиды (`Sinusoidal`)
26. `AnswerSystem` — режим `BuildFunction` (свободное построение)
27. `HintSystem` — подсказки
28. Анимации звёзд (сбор, ошибка, восстановление)
29. Визуальное восстановление созвездий
30. `NotificationService` — бэйджи нового контента
31. Конфигурация всех 100 уровней (данные в SO)

### Фаза 4: Полировка и контент

32. `AudioService` — музыка и SFX
33. Сюжетные вставки (`CutscenePopup` + `CutsceneData`)
34. `ShopService` — магазин / расход фрагментов
35. Настройки (звук, вибрация)
36. Онбординг первых уровней
37. Эффекты и particle systems
38. Оптимизация (профилирование, атласы, пулинг)
39. Локализация (если нужна)
40. Аналитика (события прохождения)
41. Тестирование на целевых устройствах

---

## 17. Открытые вопросы

| #   | Вопрос                                          | Контекст                                                 |
| --- | ----------------------------------------------- | -------------------------------------------------------- |
| 1   | Точные правила начисления фрагментов            | В ГДД отмечено «правил ещё нет», нужна спецификация      |
| 2   | Содержание мета-прогрессии и системы улучшений  | Разделы ГДД пусты — ожидают заполнения                   |
| 3   | Детали монетизации (цены ShopService)           | Раздел «Монетизация» в ГДД не заполнен                   |
| 4   | Уровень 11 сектора 5                            | Помечен как «Не придумала» — требует дизайна             |
| 5   | Конкретные пороги ошибок для звёздного рейтинга | Баланс по ошибкам для 1/2/3 звёзд (время не влияет)      |
| 6   | Количество и время восстановления жизней        | Конкретные значения не указаны                           |
| 7   | Типография: выбор конкретных шрифтов            | В ГДД указано «максимум 2 шрифта», конкретные не выбраны |
| 8   | Кастомизация и система улучшений                | Разделы ГДД ожидают заполнения                           |
| 9   | Стоимость пропуска уровня в фрагментах          | Механизм SkipLevel добавлен, цена не определена          |
