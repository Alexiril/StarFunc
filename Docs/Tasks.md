# План разработки STAR FUNC — Задачи

> Порядок задач выстроен по принципу зависимостей: каждая следующая задача опирается на результаты предыдущих.
> Фазы соответствуют разделу «Порядок реализации» из Architecture.md, но детализированы до уровня отдельных файлов и критериев завершения.
> Все пути указаны относительно `Assets/Scripts/`.

---

## Сводка фаз

| Фаза | Название            | Задач | Что на выходе                                                                             |
| ---- | ------------------- | ----- | ----------------------------------------------------------------------------------------- |
| 0    | Подготовка проекта  | 3     | Папки, сцены, настройки URP, asmdef                                                       |
| 1    | Ядро и прототип     | 13    | Играбельный уровень с выбором координаты, HUD, Ghost, сетевое ядро, авторизация           |
| 2    | Основные системы    | 16    | Сохранения (локал + облако), прогрессия, экономика, жизни, хаб, графики, reconciliation   |
| 3    | Расширение механик  | 9     | Все типы заданий, подсказки, анимации, уведомления, контент                               |
| 4    | Полировка и контент | 12    | Аудио, катсцены, магазин (hybrid), настройки, оптимизация, аналитика (REST), безопасность |

---

## ~~Фаза 0 — Подготовка проекта~~ (Done)

### ~~Задача 0.1 — Создание структуры папок и Assembly Definitions~~ (Done)

**Суть:** создать полную иерархию каталогов проекта и assembly definition файлы для разделения кода на модули компиляции.

**Что сделать:**

- Создать дерево папок внутри `Assets/`:
  - `Scripts/Core/`, `Scripts/Data/ScriptableObjects/`, `Scripts/Data/Configs/`, `Scripts/Data/Runtime/`, `Scripts/Data/Enums/`
  - `Scripts/Gameplay/CoordinatePlane/`, `Scripts/Gameplay/Stars/`, `Scripts/Gameplay/Graph/`, `Scripts/Gameplay/Ghost/`, `Scripts/Gameplay/Level/`, `Scripts/Gameplay/FunctionEditor/`
  - `Scripts/Meta/Progression/`, `Scripts/Meta/Economy/`, `Scripts/Meta/Lives/`, `Scripts/Meta/Timer/`, `Scripts/Meta/Shop/`, `Scripts/Meta/Notifications/`, `Scripts/Meta/Audio/`, `Scripts/Meta/Feedback/`
  - `Scripts/UI/Base/`, `Scripts/UI/Service/`, `Scripts/UI/Screens/`, `Scripts/UI/Popups/`, `Scripts/UI/Overlays/`, `Scripts/UI/Widgets/`
  - `Scripts/Infrastructure/Save/`, `Scripts/Infrastructure/Scenes/`, `Scripts/Infrastructure/Analytics/`, `Scripts/Infrastructure/Boot/`
  - `Scripts/Infrastructure/Network/`, `Scripts/Infrastructure/Auth/`
  - `Prefabs/UI/`, `Prefabs/Gameplay/`, `Prefabs/Effects/`
  - `ScriptableObjects/Levels/`, `ScriptableObjects/Sectors/`, `ScriptableObjects/Functions/`, `ScriptableObjects/Config/`, `ScriptableObjects/Events/`
  - `Art/Sprites/`, `Art/Animations/`, `Art/Materials/`, `Art/Shaders/`, `Art/Fonts/`
  - `Audio/Music/`, `Audio/SFX/`
  - `Resources/Localization/`
  - `Plugins/` — нативные плагины / SDK
- Создать `.asmdef` файлы:
  - `StarFunc.Core` — ядро (без зависимостей кроме Unity)
  - `StarFunc.Data` — данные (зависит от Core)
  - `StarFunc.Gameplay` — геймплей (зависит от Core, Data)
  - `StarFunc.Meta` — мета-системы (зависит от Core, Data)
  - `StarFunc.UI` — UI (зависит от Core, Data, Meta)
  - `StarFunc.Infrastructure` — инфраструктура (зависит от Core, Data)
- Добавить `.gitkeep` в пустые папки, которые должны попасть в VCS.

**Зависимости:** нет.

**Критерий завершения:** проект компилируется, все папки видны в Unity Project Window.

---

### ~~Задача 0.2 — Создание сцен и базовая конфигурация проекта~~ (Done)

**Суть:** создать три сцены (Boot, Hub, Level) и настроить Build Settings.

**Что сделать:**

- Создать сцены в `Assets/Scenes/`:
  - `Boot.unity` — пустая сцена, будет содержать `BootInitializer`
  - `Hub.unity` — сцена карты галактики
  - `Level.unity` — универсальная игровая сцена (загружается аддитивно поверх Hub)
- Добавить сцены в Build Settings в правильном порядке (Boot → 0, Hub → 1, Level → 2).
- Настроить Player Settings:
  - Ориентация: Portrait
  - Минимальный API Level: Android 7.0 (API 24)
  - Scripting Backend: IL2CPP
  - Target architectures: ARM64
  - Company Name, Product Name: заполнить

**Зависимости:** 0.1.

- Установить TextMesh Pro через Package Manager (если не установлен) и импортировать TMP Essential Resources (`Window > TextMeshPro > Import TMP Essential Resources`).

**Критерий завершения:** все 3 сцены открываются, Build Settings корректны, Player Settings настроены, TMP Essential Resources импортированы.

---

### ~~Задача 0.3 — Настройка URP для мобильной 2D-игры~~ (Done)

**Суть:** сконфигурировать Universal Render Pipeline под 2D мобильный проект.

**Что сделать:**

- Создать URP Asset в `Assets/Settings/` (если не создан):
  - Renderer: 2D Renderer
  - HDR: отключить (или включить, если нужен bloom/glow — решить и зафиксировать)
  - MSAA: Off или 2x
  - Depth Texture: Off
  - Opaque Texture: Off
- Настроить Quality Settings — один профиль «Mobile» с этим URP Asset.
- Настроить Volume Profile для минимального post-processing (если нужен bloom для glow-эффектов звёзд — добавить Bloom с низкой интенсивностью).

**Зависимости:** 0.2.

**Критерий завершения:** сцена рендерится через 2D Renderer, нет ошибок в консоли.

---

## Фаза 1 — Ядро и прототип

### ~~Задача 1.1 — ServiceLocator и событийная система~~ (Done)

**Суть:** реализовать ядро архитектуры — сервис-локатор и ScriptableObject-события.

**Файлы:**

- `Core/ServiceLocator.cs` — статический класс с `Register<T>()`, `Get<T>()`, `Contains<T>()`. Хранит сервисы в `Dictionary<Type, object>`. Добавить метод `Reset()` для очистки при смене сцен/тестах.
- `Core/GameEvent.cs` — ScriptableObject-событие без параметров. Содержит `List<GameEventListener>`, методы `Raise()`, `RegisterListener()`, `UnregisterListener()`.
- `Core/GameEventGeneric.cs` — `GameEvent<T> : ScriptableObject` — обобщённое событие с параметром типа `T`.
- `Core/GameEventListener.cs` — MonoBehaviour, привязывается к `GameEvent` в инспекторе, вызывает `UnityEvent` при `Raise()`. В `OnEnable` регистрируется, в `OnDisable` отписывается.
- `Core/GameEventListenerGeneric.cs` — MonoBehaviour-слушатель для `GameEvent<T>`, вызывает `UnityEvent<T>`.
- `Core/ColorTokens.cs` — статический класс с цветовыми константами (`BG_DARK`, `BG_SECOND`, `LINE_PRIMARY`, `POINT_PRIMARY`, `ACCENT_PINK`, `UI_NEUTRAL`, `ERROR`, `SUCCESS`).

**Зависимости:** 0.1.

**Критерий завершения:** можно зарегистрировать сервис, получить его обратно; SO-событие создаётся через меню, `Raise()` вызывает слушателей.

---

### ~~Задача 1.2 — Перечисления и конфигурационные структуры данных~~ (Done)

**Суть:** создать все enum-ы и сериализуемые конфигурационные структуры, от которых зависит остальной код.

**Файлы (Enums):**

- `Data/Enums/LevelType.cs` — `Tutorial, Normal, Bonus, Control, Final`
- `Data/Enums/TaskType.cs` — `ChooseCoordinate, ChooseFunction, AdjustGraph, BuildFunction, IdentifyError, RestoreConstellation`
- `Data/Enums/StarState.cs` — `Hidden, Active, Placed, Incorrect, Restored`
- `Data/Enums/SectorState.cs` — `Locked, Available, InProgress, Completed`
- `Data/Enums/FunctionType.cs` — `Linear, Quadratic, Sinusoidal, Mixed`
- `Data/Enums/HintTrigger.cs` — `OnLevelStart, AfterErrors, OnFirstInteraction`
- `Data/Enums/GhostEmotion.cs` — `Idle, Happy, Sad, Excited, Determined`
- `Data/Enums/FeedbackType.cs` — `StarPlaced, StarError, LevelComplete, ConstellationRestored, ButtonTap, SectorUnlock`

**Файлы (Configs):**

- `Data/Configs/StarConfig.cs` — `[Serializable]`: `StarId`, `Coordinate (Vector2)`, `InitialState`, `IsControlPoint`, `IsDistractor`, `BelongsToSolution`, `RevealAfterAction`
- `Data/Configs/StarRatingConfig.cs` — `[Serializable]`: пороги ошибок для 3/2/1 звёзд, `TimerAffectsRating`, `ThreeStarMaxTime`
- `Data/Configs/HintConfig.cs` — `[Serializable]`: `Trigger`, `HintText`, `HighlightPosition`, `TriggerAfterErrors`
- `Data/Configs/GraphVisibilityConfig.cs` — `[Serializable]`: `PartialReveal`, `InitialVisibleSegments`, `RevealPerCorrectAction`
- `Data/Configs/CutsceneFrame.cs` — `[Serializable]`: `Background`, `CharacterSprite`, `Emotion`, `Text`, `Duration`, `FrameAnimation`
- `Data/Configs/AnswerOption.cs` — `[Serializable]`: идентификатор, текст/значение варианта, флаг `IsCorrect`
- `Data/Configs/ShopItem.cs` — `[Serializable]`: `ItemId`, категория, цена, описание

**Зависимости:** 1.1 (нужен asmdef Core).

**Критерий завершения:** все enum-ы и структуры компилируются, видны в инспекторе при использовании в SO.

---

### ~~Задача 1.3 — ScriptableObject-определения данных~~ (Done)

**Суть:** создать SO-классы для конфигурации уровней, секторов, функций и катсцен.

**Файлы:**

- `Data/ScriptableObjects/SectorData.cs` — поля: `SectorId`, `DisplayName`, `SectorIndex`, `Levels[]`, `PreviousSector`, `RequiredStarsToUnlock`, визуальные поля (спрайты, цвета, углы), `IntroCutscene`, `OutroCutscene`. Атрибут `[CreateAssetMenu(menuName = "StarFunc/Data/SectorData")]`.
- `Data/ScriptableObjects/LevelData.cs` — поля: `LevelId`, `LevelIndex`, `Type`, координатная плоскость (`PlaneMin`, `PlaneMax`, `GridStep`), `Stars[]`, `TaskType`, `ReferenceFunctions[]`, `AnswerOptions[]`, `AccuracyThreshold`, `StarRating`, ограничения (`MaxAttempts`, `MaxAdjustments`), видимость (`UseMemoryMode`, `MemoryDisplayDuration`, `GraphVisibility`), подсказки (`ShowHints`, `Hints[]`), `FragmentReward`. Атрибут `[CreateAssetMenu]`.
- `Data/ScriptableObjects/FunctionDefinition.cs` — поля: `Type (FunctionType)`, `Coefficients[]`, `DomainRange`. Атрибут `[CreateAssetMenu]`.
- `Data/ScriptableObjects/CutsceneData.cs` — поля: `CutsceneId`, `Frames[]`. Атрибут `[CreateAssetMenu]`.

**Зависимости:** 1.2.

**Критерий завершения:** можно создать SO-ассеты через меню `Assets > Create > StarFunc/Data/...`, все поля отображаются в инспекторе.

---

### ~~Задача 1.4 — Runtime-модели данных~~ (Done)

**Суть:** создать модели данных, используемые во время выполнения (не SO, а обычные классы/структуры).

**Файлы:**

- `Data/Runtime/PlayerSaveData.cs` — `[Serializable]`: словари прогресса секторов/уровней, `CurrentSectorIndex`, `TotalFragments`, `CurrentLives`, `LastLifeRestoreTimestamp`, статистика.
  - **Дополнительные поля (API.md §5.1):** `SaveVersion` (версия формата для миграции), `Version` (монотонно возрастающий счётчик для optimistic lock), `LastModified` (Unix timestamp последнего изменения), `OwnedItems` (купленные перманентные предметы, например скины/темы), `Consumables` (`Dictionary<string, int>`, например `{ "hints": 5 }`). Каждая мутация (прохождение, покупка, списание жизни) инкрементирует `Version`.
- `Data/Runtime/SectorProgress.cs` — `[Serializable]`: `State (SectorState)`, `StarsCollected`, `ControlLevelPassed`.
- `Data/Runtime/LevelProgress.cs` — `[Serializable]`: `IsCompleted`, `BestStars`, `BestTime`, `Attempts`.
- `Data/Runtime/LevelResult.cs` — результат прохождения: `Stars (int)`, `Time (float)`, `Errors (int)`, `FragmentsEarned`.
- `Data/Runtime/PlayerAnswer.cs` — ответ игрока для передачи в ValidationSystem: выбранная координата / функция / набор точек.
  - **Дополнение (API.md §5.5):** Должен поддерживать 4 типа ответов: `ChooseOption` (с `selectedOptionId`), `Function` (с `functionType` + `coefficients[]`), `IdentifyStars` (с `selectedStarIds[]`), `PlaceStars` (с массивом `placements` где каждый элемент = `starId` + `coordinate`). Должен сериализоваться в JSON для отправки на `POST /check/level`.
- `Data/Runtime/PlayerAction.cs` — действие игрока для стека undo: тип действия, предыдущее/новое состояние.
- `Data/Runtime/FunctionParams.cs` — текущие параметры функции для события `OnFunctionChanged`.
- `Data/Runtime/AnswerData.cs` — данные о выбранном ответе для события `OnAnswerSelected`.
- `Data/Runtime/StarData.cs` — данные о звезде для событий `OnStarCollected/Rejected`.
- `Data/Runtime/PopupData.cs` — данные для инициализации попапа (заголовок, текст, действия).
- `Data/Runtime/ValidationResult.cs` — результат валидации: `IsValid`, список ошибок, процент совпадения.
  - **Дополнение (API.md §5.6, §6.7):** Полный набор полей: `IsValid`, `Stars` (int), `FragmentsEarned` (int), `Time` (float), `ErrorCount` (int), `MatchPercentage` (float 0–1), `Errors[]` (string[]). Эта структура должна соответствовать серверному `CheckResult.result`.

**Зависимости:** 1.2.

**Критерий завершения:** все классы компилируются, можно сериализовать `PlayerSaveData` в JSON и обратно.

---

### ~~Задача 1.5 — SceneFlowManager и BootInitializer~~ (Done)

**Суть:** реализовать управление сценами (аддитивная загрузка Level-сцены) и начальную инициализацию приложения.

**Файлы:**

- `Infrastructure/Scenes/SceneFlowManager.cs` — MonoBehaviour (DontDestroyOnLoad). Методы:
  - `LoadLevel(LevelData level)` — аддитивная загрузка Level-сцены поверх Hub (`SceneManager.LoadSceneAsync("Level", LoadSceneMode.Additive)`), скрывает Hub UI
  - `UnloadLevel()` — выгрузка Level-сцены (`SceneManager.UnloadSceneAsync`), восстанавливает Hub UI
  - `LoadScene(string sceneName)` — полная замена сцены (используется только для Boot → Hub)
  - Приватная корутина, которая: показывает LoadingOverlay (опционально) → загружает сцену → скрывает LoadingOverlay
- `UI/Overlays/LoadingOverlay.cs` — заглушка LoadingOverlay (наследник `UIScreen`), живёт на DontDestroyOnLoad-объекте. Минимальная реализация: полноэкранное затемнение + текст «Loading...». Методы: `Show()`, `Hide()`. Полноценная реализация — в Задаче 3.8.
- `Infrastructure/Boot/BootInitializer.cs` — MonoBehaviour на объекте в Boot-сцене:
  - В `Awake()`: создаёт и регистрирует `SceneFlowManager` (DontDestroyOnLoad)
  - Пока регистрирует только SceneFlowManager (остальные сервисы добавятся позже)
  - В `Start()`: вызывает `SceneFlowManager.LoadScene("Hub")`
  - **Дополнение (API.md §10.5):** В итоговом виде (после задач 1.12–1.13 и 2.12–2.15) порядок инициализации должен стать: 1) NetworkMonitor 2) AuthService (register/refresh) 3) LocalSaveService 4) CloudSaveClient (попытка загрузить облачное, мерж) 5) HybridSaveService → `Register<ISaveService>()` 6) ContentService (проверить contentVersion, скачать diff) 7) HybridEconomyService → `Register<IEconomyService>()` 8) HybridLivesService → `Register<ILivesService>()` 9) HybridShopService → `Register<IShopService>()` 10) Остальные сервисы 11) `SceneFlowManager.LoadScene("Hub")`

**Зависимости:** 1.1, 0.2.

**Критерий завершения:** запуск из Boot-сцены → открывается Hub-сцена; вызов `LoadLevel()` загружает Level-сцену аддитивно, `UnloadLevel()` выгружает её.

---

### ~~Задача 1.6 — CoordinatePlane: сетка, оси, ввод~~ (Done)

**Суть:** реализовать координатную плоскость — основной игровой объект.

**Файлы:**

- `Gameplay/CoordinatePlane/CoordinatePlane.cs` — главный компонент. Хранит ссылки на подкомпоненты. Публичные свойства: `PlaneMin`, `PlaneMax`, `GridStep`. Методы: `WorldToPlane(Vector2)`, `PlaneToWorld(Vector2)` — преобразование координат.
- `Gameplay/CoordinatePlane/GridRenderer.cs` — отрисовка линий сетки. Использует `LineRenderer` или `GL.Begin/End`. Параметры: `PlaneMin`, `PlaneMax`, `GridStep`, цвет линий (приглушённый `BG_SECOND`).
- `Gameplay/CoordinatePlane/AxisRenderer.cs` — отрисовка осей X и Y (выделенным цветом, толще сетки). Стрелки на концах (опционально).
- `Gameplay/CoordinatePlane/CoordinateLabeler.cs` — размещение числовых TextMeshPro-меток вдоль осей с шагом `GridStep`.
- `Gameplay/CoordinatePlane/TouchInputHandler.cs` — обработка touch/mouse: tap → определение координаты на плоскости через raycasting + `WorldToPlane()`. Вызывает событие `OnPlaneClicked(Vector2)`. **Дополнение:** Использовать Unity Input System 1.19.0 (new Input System), а не legacy `UnityEngine.Input`. Согласно Architecture.md проект использует Input System пакет.
- `Gameplay/CoordinatePlane/PlaneCamera.cs` — управление камерой/масштабом: pinch-to-zoom (для мобильных), scroll (для редактора). Ограничивает область видимости в пределах `PlaneMin`...`PlaneMax`.

**Зависимости:** 1.1 (ColorTokens), 1.2.

**Критерий завершения:** на Level-сцене видна координатная сетка с осями и метками; tap на плоскость возвращает правильную координату в консоль.

---

### ~~Задача 1.7 — StarEntity: звезда с состояниями~~ (Done)

**Суть:** реализовать сущность звезды — основной интерактивный элемент уровня.

**Файлы:**

- `Gameplay/Stars/StarEntity.cs` — главный MonoBehaviour звезды. Хранит `StarConfig`, текущий `StarState`. Методы: `Initialize(StarConfig)`, `SetState(StarState)`, `GetCoordinate()`.
- `Gameplay/Stars/StarVisuals.cs` — управление визуалом: SpriteRenderer, цвет по состоянию (`POINT_PRIMARY` — Active, `SUCCESS` — Placed, `ERROR` — Incorrect), glow-эффект (дочерний спрайт с аддитивным материалом).
- `Gameplay/Stars/StarAnimator.cs` — заглушки анимаций (на этом этапе — простые Coroutine): `PlayAppear()` (fade-in + scale), `PlayPlace()` (pulse), `PlayError()` (shake), `PlayRestore()` (заглушка).
- `Gameplay/Stars/StarInteraction.cs` — обработка tap/drag (через Collider2D + `IPointerClickHandler` или Input System). Вызывает событие при tap.
- `Gameplay/Stars/StarManager.cs` — управление всеми звёздами уровня. Методы: `SpawnStars(StarConfig[])` — инстанцирует звёзды по конфигу, `GetStar(string starId)`, `GetAllPlaced()`, `ResetAll()`.

**Зависимости:** 1.2, 1.3, 1.6.

**Критерий завершения:** звёзды появляются на координатной плоскости по позициям из `StarConfig[]`, tap на звезду регистрируется, состояние меняется визуально.

---

### ~~Задача 1.8 — GhostEntity: персонаж с эмоциями~~ (Done)

**Суть:** реализовать космического призрака — визуального аватара игрока.

**Файлы:**

- `Gameplay/Ghost/GhostEntity.cs` — главный MonoBehaviour. Хранит ссылки на подкомпоненты. Метод `SetEmotion(GhostEmotion)`.
- `Gameplay/Ghost/GhostVisuals.cs` — SpriteRenderer, glow-эффект, смена спрайта по эмоции (на этом этапе — один placeholder-спрайт, цвет меняется).
- `Gameplay/Ghost/GhostAnimator.cs` — управление Animator-ом (или Coroutine-заглушки для idle-анимации: плавное покачивание через `Mathf.Sin`).
- `Gameplay/Ghost/GhostEmotionController.cs` — подписывается на SO-события (`OnStarCollected → Happy`, `OnStarRejected → Sad`, `OnLevelCompleted → Excited`). Вызывает `GhostEntity.SetEmotion()`.
- `Gameplay/Ghost/GhostPositioner.cs` — позиционирование: на уровне стоит рядом с полем (фиксированная позиция или привязка к UI). Позиция хаба — заглушка.

**Зависимости:** 1.1 (события), 1.2 (GhostEmotion enum).

**Критерий завершения:** призрак отображается рядом с координатной плоскостью, меняет визуал при смене эмоции.

---

### ~~Задача 1.9 — LevelController и AnswerSystem (ChooseCoordinate)~~ (Done)

**Суть:** реализовать центральный контроллер уровня и первый режим ответов — выбор координаты.

**Файлы:**

- `Gameplay/Level/LevelController.cs` — MonoBehaviour. Состояния: `Initialize → ShowTask → AwaitInput → ValidateAnswer → (CalculateResult → ShowResult → Complete)`. Хранит ссылки на `CoordinatePlane`, `StarManager`, `AnswerSystem`, `ValidationSystem`. Получает `LevelData` SO при инициализации. Методы:
  - `Initialize(LevelData)` — настраивает плоскость, спавнит звёзды, настраивает систему ответов
  - `OnAnswerSubmitted(PlayerAnswer)` — вызывается при подтверждении ответа
  - `CompleteStar(StarConfig)` — помечает звезду как Placed
  - `FailAttempt()` — обработка ошибки
  - **Дополнение (Architecture.md §5.2, API.md §6.7):**
    - Реализовать **Memory Mode**: если `LevelData.UseMemoryMode == true`, показать эталонный график/сзвездие на `MemoryDisplayDuration` секунд, затем скрыть. Игрок должен восстановить по памяти.
    - Реализовать **GraphVisibilityConfig**: если `GraphVisibility.PartialReveal == true`, показать только `InitialVisibleSegments` сегментов графика, раскрывать по `RevealPerCorrectAction` за каждое правильное действие.
    - Реализовать **MaxAttempts**: если `MaxAttempts > 0` и `attempt >= MaxAttempts` → уровень провален (`failReason: "max_attempts_reached"`).
    - Реализовать **Провал уровня**: при `currentLives == 0` после списания → `failReason: "no_lives"`. Вызывать SO-событие `OnLevelFailed`.
    - **Жизни списываются один раз за попытку** (подтверждение Confirm), не за каждую локальную ошибку до подтверждения (Architecture.md §7.3).
    - При входе на уровень проверять `ILivesService.HasLives()` — при 0 жизней вход заблокирован, показать `NoLivesPopup`.
- `Gameplay/Level/AnswerSystem.cs` — адаптивная система ответов. На этом этапе реализовать только режим `ChooseCoordinate`:
  - Получает `AnswerOption[]` из `LevelData`
  - Создаёт варианты ответов (генерирует UI через AnswerPanel)
  - При выборе варианта вызывает `OnAnswerSelected` событие
  - Метод `GetCurrentAnswer() : PlayerAnswer`

**Зависимости:** 1.3, 1.6, 1.7.

**Критерий завершения:** уровень загружается из `LevelData` SO, показывает звёзды и варианты ответов, игрок может выбрать вариант и получить обратную связь.

---

### ~~Задача 1.10 — ValidationSystem и LevelResultCalculator~~ (Done)

**Суть:** реализовать проверку правильности ответов и подсчёт результата уровня.

**Файлы:**

- `Gameplay/Level/ValidationSystem.cs` — класс (не MonoBehaviour). Методы:
  - `ValidateCoordinate(Vector2 selected, Vector2 expected, float threshold) : bool`
  - `ValidateFunction(FunctionDefinition player, FunctionDefinition reference, float threshold) : bool` — заглушка (реализуется в Фазе 2)
  - `ValidateControlPoints(StarConfig[] placed, StarConfig[] reference) : ValidationResult`
  - `ValidateLevel(LevelData level, PlayerAnswer answer) : LevelResult`
- `Gameplay/Level/LevelResultCalculator.cs` — подсчёт результата:
  - `Calculate(LevelData level, int errors, float time) : LevelResult` — определяет количество звёзд по порогам из `StarRatingConfig`, вычисляет заработанные фрагменты.
  - **Дополнение (API.md §6.7):**
    - При повторном прохождении уровня: обновить `bestStars = max(old, new)`, `bestTime = min(old, new)`, начислить бонусные фрагменты за улучшение (`improvementBonusPerStar` из баланс-конфига × кол-во новых звёзд)
    - 0 звёзд = уровень не пройден (все попытки исчерпаны или ошибок > `OneStarMaxErrors`)
    - Правила провала уровня: `failReason: "no_lives"` (после списания жизни `currentLives == 0`) или `failReason: "max_attempts_reached"` (`maxAttempts > 0` и `attempt >= maxAttempts`)
    - `matchPercentage` (float 0–1) для режимов AdjustGraph/BuildFunction — среднеквадратичное отклонение, нормированное в [0, 1]
- `Gameplay/Level/ActionHistory.cs` — стек действий для Undo/Reset:
  - `Push(PlayerAction)`, `Undo() : PlayerAction`, `Reset()`, `CanUndo : bool`
- `Gameplay/Level/LevelTimer.cs` — обёртка таймера уровня (запуск/пауза/стоп, `GetElapsedTime()`). На данном этапе — автономная реализация. Позже будет делегировать в `TimerService`.

**Зависимости:** 1.2, 1.3, 1.4.

**Критерий завершения:** правильный выбор координаты → звезда Placed; неправильный → Incorrect + счётчик ошибок; после выполнения всех заданий → подсчёт звёзд.

---

### ~~Задача 1.11 — Минимальный LevelHUD~~ (Done)

**Суть:** создать базовый HUD уровня: таймер, жизни, варианты ответов, кнопки Undo/Reset/Confirm.

**Файлы:**

- `UI/Base/UIScreen.cs` — абстрактный базовый класс: `Show()`, `Hide()`, `IsVisible`, CanvasGroup для fade.
- `UI/Base/UIPopup.cs` — абстрактный базовый класс попапа: `Show(PopupData)`, `Hide()`, затемнение фона.
- `UI/Screens/LevelHUD.cs` — наследник `UIScreen`. Содержит ссылки на:
  - `TimerDisplay` — отображение таймера
  - `LivesDisplay` — отображение жизней (на этом этапе — статическое число)
  - `AnswerPanel` — панель вариантов ответа
  - Кнопки: Pause, Confirm, Undo, Reset, Hint
  - Привязка кнопок к `LevelController` и `ActionHistory`
  - `HintButton` — кнопка подсказки (на этом этапе неактивна, функциональность реализуется в Задаче 3.4)
- `UI/Widgets/TimerDisplay.cs` — TextMeshPro, обновляется каждый кадр из `LevelTimer`.
- `UI/Widgets/LivesDisplay.cs` — TextMeshPro/иконки, отображает текущее число жизней.
- `UI/Widgets/AnswerPanel.cs` — генерирует кнопки-варианты ответа из `AnswerOption[]`. При нажатии вызывает `OnAnswerSelected`.

**Зависимости:** 1.9, 1.10.

**Критерий завершения:** на Level-сцене видны таймер, жизни, варианты ответов, кнопки. Игрок может пройти простой уровень от начала до конца.

---

### ~~Задача 1.12 — ApiClient, NetworkMonitor, TokenManager (NEW — API.md §10, §11)~~ (Done)

**Суть:** реализовать сетевую инфраструктуру — HTTP-клиент, отслеживание состояния сети, управление JWT-токенами.

**Файлы:**

- `Infrastructure/Network/ApiClient.cs` — обёртка над `UnityWebRequest`:
  - Методы: `Get<T>(string endpoint)`, `Post<T>(string endpoint, object body)`, `Put<T>(string endpoint, object body)`
  - Автоматическая подстановка заголовков: `Authorization: Bearer <token>`, `Content-Type: application/json`, `Accept-Encoding: gzip`, `X-Client-Version`, `X-Platform`
  - Поддержка `Idempotency-Key` (UUID) для мутирующих запросов (защита от replay-атак, API.md §9.5)
  - Десериализация стандартного ответа `{ status, data, serverTime }` и ошибки `{ status, error: { code, message, details } }`
  - Стратегия retry по HTTP-кодам (API.md §4.3): 401 → refresh token → retry; 429 → exponential backoff (1s, 2s, 4s), макс. 3; 500/503 → backoff, макс. 3 → offline; timeout → 1 retry → offline
  - Таймауты: 10 сек hard, 5 сек soft (переход в offline)
  - Обработка gzip (Content-Encoding)
- `Infrastructure/Network/ApiEndpoints.cs` — статический класс с константами URL:
  - `BaseUrl = "https://api.starfunc.app/api/v1"`
  - Все эндпоинты из API.md §6: `/auth/register`, `/auth/refresh`, `/auth/link`, `/save`, `/economy/balance`, `/economy/transaction`, `/lives`, `/lives/restore`, `/lives/restore-all`, `/shop/items`, `/shop/purchase`, `/content/manifest`, `/content/sectors`, `/content/levels/{id}`, `/content/balance`, `/check/level`, `/analytics/events`, `/health`
- `Infrastructure/Network/NetworkMonitor.cs` — отслеживание состояния сети:
  - Свойство `IsOnline : bool`
  - Событие `OnConnectivityChanged(bool isOnline)`
  - Используется всеми Hybrid-сервисами для принятия решения: отправлять запрос или ставить в очередь
  - Периодическая проверка + реакция на `Application.internetReachability`
- `Infrastructure/Network/TokenManager.cs` — хранение и обновление JWT:
  - Access Token: в памяти (не persisted), TTL 1 час
  - Refresh Token: в `Application.persistentDataPath` зашифрованный (AES-256, ключ = `deviceId` + hardware fingerprint)
  - Метод `GetAccessToken()` — возвращает текущий token или null
  - Метод `RefreshAccessToken()` — вызывает `POST /auth/refresh`, ротирует refresh token (одноразовый)
  - Метод `SetTokens(accessToken, refreshToken)` — после успешной авторизации
  - Метод `ClearTokens()` — при логауте / ошибке авторизации

**Зависимости:** 0.1, 1.1.

**Критерий завершения:** `ApiClient` отправляет HTTP-запросы с правильными заголовками, обрабатывает retry-стратегию; `NetworkMonitor` корректно определяет онлайн/оффлайн; `TokenManager` шифрует/дешифрует refresh token.

---

### ~~Задача 1.13 — AuthService (NEW — API.md §6.1, §10, §11)~~ (Done)

**Суть:** реализовать сервис авторизации — анонимная регистрация устройства, обновление токена, привязка аккаунта.

**Файлы:**

- `Infrastructure/Auth/AuthService.cs` — реализация:
  - `Register()` — при первом запуске генерирует `deviceId` (UUID v7), отправляет `POST /auth/register` с `{ deviceId, platform, clientVersion }`. Получает `playerId`, `accessToken`, `refreshToken`. Сохраняет через `TokenManager`. Идемпотентно: если `deviceId` уже зарегистрирован, возвращает существующего игрока.
  - `RefreshToken()` — вызывает `POST /auth/refresh`. При 401 → переход в offline-режим (не блокировать игру).
  - `LinkAccount(provider, providerToken)` — `POST /auth/link` для Google Play Games / Apple Game Center. Обработка ошибки `409 ACCOUNT_ALREADY_LINKED`.
  - `deviceId` сохраняется в `Application.persistentDataPath` (зашифрован, переживает переустановку только если данные приложения не очищены)
  - При отсутствии сети — пропуск регистрации, работа в offline-режиме

**Зависимости:** 1.12.

**Критерий завершения:** при первом запуске происходит анонимная регистрация, токены сохраняются, при следующем запуске используется refresh token.

---

## Фаза 2 — Основные системы

### Задача 2.1 — SaveService (Local + подготовка к Hybrid)

**Суть:** реализовать систему сохранения и загрузки прогресса игрока. **Имя класса: `LocalSaveService`** (не `SaveService`) — это локальная реализация, позже обернутая `HybridSaveService` (задача 2.1a).

**Файлы:**

- `Infrastructure/Save/ISaveService.cs` — интерфейс: `Load() : PlayerSaveData`, `Save(PlayerSaveData)`, `Delete()`, `HasSave() : bool`.
- `Infrastructure/Save/LocalSaveService.cs` — реализация:
  - JSON-сериализация через `Newtonsoft.Json` (необходим для поддержки `Dictionary<string, SectorProgress>` в `PlayerSaveData`)
  - Путь: `Application.persistentDataPath + "/save.json"`
  - Контрольная сумма (SHA-256 хэш содержимого, хранится рядом) для базовой защиты от ручного редактирования
  - Автосохранение в `OnApplicationPause(true)` и `OnApplicationQuit()`
  - Версионирование формата: поле `SaveVersion` в `PlayerSaveData`
  - **Дополнение (API.md §5.1, §8):** При каждом сохранении инкрементировать `PlayerSaveData.Version` и обновлять `LastModified`. Поле `Version` используется как `expectedVersion` при облачной синхронизации (optimistic lock).

**Зависимости:** 1.4.

**Критерий завершения:** сохранение создаётся в persistentDataPath, переживает перезапуск приложения, некорректный файл определяется по контрольной сумме.

---

### Задача 2.2 — ProgressionService

**Суть:** реализовать систему прогрессии — отслеживание состояния секторов, уровней, звёзд.

**Файлы:**

- `Meta/Progression/IProgressionService.cs` — интерфейс (см. Architecture.md раздел 7.1): состояние секторов, уровней, звёзды, разблокировка, пропуск.
- `Meta/Progression/ProgressionService.cs` — реализация:
  - Хранит `PlayerSaveData` в памяти, читает/пишет через `ISaveService`
  - Разблокировка сектора: пройден контрольный уровень (индекс 18) предыдущего сектора + набран порог звёзд
  - **Дополнение (API.md §5.2):** Звёзды бонусных уровней (`type=Bonus`, индексы 6, 11) **НЕ учитываются** в пороге разблокировки следующего сектора (`RequiredStarsToUnlock`)
  - Разблокировка уровня: пройден предыдущий уровень
  - Бонусные уровни (индексы 6, 11) — опциональны, не влияют на разблокировку
  - `SkipLevel()` — ставит 1 звезду, без фрагментов, списывает стоимость (`skipLevelCostFragments` из баланс-конфига, см. API.md §6.6 `GET /content/balance`)
  - **Повторное прохождение уровня (API.md §6.7):** `CompleteLevel()` должен обновлять `bestStars = max(old, new)`, `bestTime = min(old, new)`, инкрементировать `Attempts`. Бонусные фрагменты за улучшение начисляются через `IEconomyService`.
  - Вызывает SO-события: `OnSectorUnlocked`, `OnSectorCompleted`

**Зависимости:** 2.1, 1.1, 1.3.

**Критерий завершения:** после прохождения уровня прогресс сохраняется, следующий уровень разблокируется, звёзды считаются корректно.

---

### Задача 2.3 — EconomyService (Local + подготовка к Hybrid)

**Суть:** реализовать систему внутриигровой валюты — фрагментов. **Имя класса: `LocalEconomyService`** — локальная реализация, позже обернутая `HybridEconomyService` (задача 2.3a).

**Файлы:**

- `Meta/Economy/IEconomyService.cs` — интерфейс: `GetFragments()`, `AddFragments(int)`, `SpendFragments(int) : bool`, `CanAfford(int) : bool`.
- `Meta/Economy/LocalEconomyService.cs` — реализация:
  - Работает с полем `TotalFragments` в `PlayerSaveData`
  - `SpendFragments` возвращает `false` если баланс недостаточен
  - При изменении баланса вызывает SO-событие `OnFragmentsChanged`
  - Бонус за улучшение результата: при повторном прохождении уровня с бо́льшим количеством звёзд начисляются дополнительные фрагменты (разница)
  - **Дополнение (API.md §6.6 `GET /content/balance`):** Бонус за улучшение = `improvementBonusPerStar` (из баланс-конфига, default 5) × количество новых звёзд. Стоимость подсказки = `hintCostFragments` (default 10). Стоимость восстановления жизни = `restoreCostFragments` (default 20). Эти значения должны читаться из конфига, а не быть захардкожены.
  - Автосохранение через `ISaveService` после каждой транзакции
  - **Дополнение (API.md §6.3):** Начисление фрагментов за уровень происходит атомарно внутри `POST /check/level` (не через `/economy/transaction`). Эндпойнт `/economy/transaction` используется только для `shop_purchase` и `skip_level`.

**Зависимости:** 2.1, 1.1.

**Критерий завершения:** фрагменты начисляются при прохождении уровня, баланс корректен после перезапуска.

---

### Задача 2.4 — LivesService (Local + подготовка к Hybrid)

**Суть:** реализовать систему жизней с автовосстановлением по таймеру. **Имя класса: `LocalLivesService`** — локальная реализация, позже обернутая `HybridLivesService` (задача 2.4a).

**Файлы:**

- `Meta/Lives/ILivesService.cs` — интерфейс: `GetCurrentLives()`, `GetMaxLives()`, `HasLives()`, `DeductLife()`, `RestoreLife()`, `RestoreAllLives()`, `GetTimeUntilNextRestore()`.
  - **Дополнение (Architecture.md §7.3):** Отдельного метода `DeductLife()` в интерфейсе нет — списание происходит внутри flow проверки ответа (LevelController / `POST /check/level`). Клиент не списывает жизни самостоятельно, а получает обновлённое значение от сервера (или из локальной валидации при offline). В локальной реализации можно оставить internal-метод `DeductLife()` для использования из LevelController.
- `Meta/Lives/LocalLivesService.cs` — реализация:
  - Максимум жизней: из баланс-конфига (`maxLives`, default 5)
  - Восстановление: 1 жизнь каждые `restoreIntervalSeconds` сек (default 1800 = 30 мин)
  - Стоимость восстановления за фрагменты: `restoreCostFragments` из баланс-конфига (default 20)
  - **`RestoreAllLives()`** (API.md §6.4): восстановить все до максимума, стоимость = `restoreCostFragments × (maxLives - currentLives)`
  - При инициализации: вычислить, сколько жизней восстановилось с момента `LastLifeRestoreTimestamp`
  - `DeductLife()` — списание 1 жизни + событие `OnLivesChanged`
  - При 0 жизней — `HasLives()` возвращает `false`, вход на уровень блокируется
  - Таймер восстановления работает в Update (или через Coroutine)
  - **Все константы (`maxLives`, `restoreIntervalSeconds`, `restoreCostFragments`) должны читаться из баланс-конфига**, а не быть захардкожены (см. задачу 2.13 — ContentService)

**Зависимости:** 2.1, 1.1.

**Критерий завершения:** жизни списываются при ошибке, восстанавливаются по таймеру, корректно пересчитываются после перезапуска.

---

### Задача 2.5 — TimerService и FeedbackService

**Суть:** реализовать сервис таймера уровня и объединённую систему обратной связи.

**Файлы:**

- `Meta/Timer/ITimerService.cs` — интерфейс: `StartTimer()`, `StopTimer()`, `PauseTimer()`, `ResumeTimer()`, `GetElapsedTime()`.
- `Meta/Timer/TimerService.cs` — реализация: `Time.realtimeSinceStartup`-based, поддержка паузы.
- `Meta/Feedback/IFeedbackService.cs` — интерфейс: `PlayFeedback(FeedbackType)`, `SetHapticsEnabled(bool)`.
- `Meta/Feedback/FeedbackService.cs` — реализация:
  - При вызове `PlayFeedback()` — вызывает соответствующий метод `AudioService` (если зарегистрирован) + вибрацию (`Handheld.Vibrate()`)
  - На данном этапе: аудио — заглушка (Debug.Log), вибрация — реальная
  - Настройка haptics сохраняется в `PlayerPrefs`

**Зависимости:** 1.1.

**Критерий завершения:** таймер корректно считает время с поддержкой паузы; FeedbackService вызывает вибрацию при ошибке (на устройстве).

---

### Задача 2.6 — UIService и LevelResultScreen

**Суть:** реализовать менеджер UI-экранов и экран результата уровня.

**Файлы:**

- `UI/Service/IUIService.cs` — интерфейс: `ShowScreen<T>()`, `HideScreen<T>()`, `ShowPopup<T>(PopupData)`, `HideAllPopups()`, `GetScreen<T>()`.
- `UI/Service/UIService.cs` — реализация:
  - Хранит ссылки на все экраны (через `GetComponentsInChildren` или ручная регистрация)
  - Стек экранов: показывает один экран, скрывает предыдущий
  - Попапы — поверх текущего экрана, могут быть несколько
- `UI/Screens/LevelResultScreen.cs` — наследник `UIScreen`:
  - Показывает: рейтинг звёзд (анимированное появление 1-3 звёзд), время, фрагменты, превью созвездия
  - Кнопки: «Далее» (следующий уровень), «Повторить», «В хаб»
  - Принимает `LevelResult` для заполнения данных
- `UI/Widgets/StarRatingDisplay.cs` — виджет отображения 0-3 звёзд (переиспользуется на экране результата и на карте).
- `UI/Widgets/FragmentsDisplay.cs` — счётчик фрагментов в TopBar.

**Зависимости:** 1.11, 1.10.

**Критерий завершения:** после прохождения уровня показывается экран результата с данными из `LevelResult`, кнопки работают.

---

### Задача 2.7 — HubScreen: карта галактики

**Суть:** реализовать экран хаба — визуальную карту секторов с прогрессией.

**Файлы:**

- `UI/Screens/HubScreen.cs` — наследник `UIScreen`:
  - Отображает 5 секторов + хаб, соединённых линиями
  - Каждый сектор: иконка + состояние (Locked/Available/InProgress/Completed)
  - При входе в сектор применяются его цвета (`AccentColor`, `StarColor` из `SectorData`)
  - При нажатии на Available/InProgress сектор → переход на `SectorScreen`
  - Счётчики: общие звёзды, фрагменты, жизни (TopBar)
  - Персонаж (Ghost) привязан к текущему сектору
  - Бэйджи уведомлений на секторах (заглушка, заполнится в Фазе 3)
  - Кнопки: магазин, настройки
  - Читает данные из `IProgressionService`

**Зависимости:** 2.2, 2.6, 1.8.

**Критерий завершения:** хаб показывает 5 секторов с правильными состояниями, можно перейти в сектор.

---

### Задача 2.8 — SectorScreen: карта уровней

**Суть:** реализовать экран сектора — последовательный список/путь уровней.

**Файлы:**

- `UI/Screens/SectorScreen.cs` — наследник `UIScreen`:
  - Получает `SectorData` SO
  - Отображает 20 уровней как точки на вертикальном/горизонтальном пути
  - Каждый уровень: номер + иконка состояния (locked/available/completed + звёзды)
  - Нажатие на доступный уровень → аддитивная загрузка Level-сцены с передачей `LevelData`
  - Прокрутка (ScrollRect) для навигации по уровням
  - Кнопка «Назад» → возврат на Hub

**Зависимости:** 2.7, 2.2.

**Критерий завершения:** экран сектора показывает 20 уровней с правильными состояниями, можно войти в доступный уровень.

---

### Задача 2.9 — GraphRenderer: отрисовка линейных функций

**Суть:** реализовать систему рендеринга графиков математических функций (начать с линейных).

**Файлы:**

- `Gameplay/Graph/GraphRenderer.cs` — главный MonoBehaviour. Хранит ссылки на подкомпоненты. Методы: `DrawFunction(FunctionDefinition)`, `Clear()`, `SetComparison(FunctionDefinition)`.
- `Gameplay/Graph/FunctionEvaluator.cs` — статический класс. Метод `Evaluate(FunctionDefinition, float x) : float`:
  - `Linear: y = a*x + b`
  - `Quadratic`, `Sinusoidal`, `Mixed` — заглушки (реализуются в Фазе 3)
- `Gameplay/Graph/CurveRenderer.cs` — отрисовка кривой через LineRenderer. Параметры: `FunctionDefinition`, диапазон X (`DomainRange`), количество сэмплов (50-100). Цвет: `LINE_PRIMARY`.
- `Gameplay/Graph/ControlPointsRenderer.cs` — отображение контрольных точек на графике (маркеры-кружки в позициях `StarConfig.Coordinate`).
- `Gameplay/Graph/ComparisonOverlay.cs` — отрисовка второго графика (эталонного) полупрозрачным цветом для сравнения.

**Зависимости:** 1.6, 1.3.

**Критерий завершения:** на координатной плоскости рисуется линейная функция `y = kx + b` по данным из `FunctionDefinition`, контрольные точки отображаются.

---

### Задача 2.10 — AnswerSystem: режим ChooseFunction

**Суть:** добавить второй режим ответов — выбор правильной функции из предложенных вариантов.

**Что сделать:**

- Расширить `Gameplay/Level/AnswerSystem.cs`:
  - Режим `ChooseFunction`: показывает 3-5 вариантов функций (текстовое описание ИЛИ мини-превью графика)
  - При выборе варианта — отрисовывает соответствующий график на `GraphRenderer` для визуального сравнения
  - Подтверждение → `ValidationSystem.ValidateFunction()` (на этом этапе — сравнение коэффициентов с эталоном)
- Расширить `AnswerPanel.cs` — поддержка отображения формул функций в вариантах ответа.
- Расширить `ValidationSystem.cs` — реализовать `ValidateFunction()` для линейных функций.

**Зависимости:** 2.9, 1.9.

**Критерий завершения:** уровень с `TaskType = ChooseFunction` работает: варианты отображаются, при выборе рисуется график, проверка корректна.

---

### Задача 2.11 — Создание SO-ассетов событий

**Суть:** создать экземпляры ScriptableObject-событий (`.asset`-файлы), описанных в Architecture.md §4.2.

**Что сделать:**

- Создать `.asset`-файлы в `Assets/ScriptableObjects/Events/` для всех ключевых событий:
  - `OnLevelStarted.asset` — `GameEvent<LevelData>`
  - `OnLevelCompleted.asset` — `GameEvent<LevelResult>`
  - `OnLevelFailed.asset` — `GameEvent`
  - `OnStarCollected.asset` — `GameEvent<StarData>`
  - `OnStarRejected.asset` — `GameEvent<StarData>`
  - `OnAnswerSelected.asset` — `GameEvent<AnswerData>`
  - `OnAnswerConfirmed.asset` — `GameEvent<bool>`
  - `OnFunctionChanged.asset` — `GameEvent<FunctionParams>`
  - `OnGraphUpdated.asset` — `GameEvent`
  - `OnSectorUnlocked.asset` — `GameEvent<SectorData>`
  - `OnSectorCompleted.asset` — `GameEvent<SectorData>`
  - `OnConstellationRestored.asset` — `GameEvent<SectorData>`
  - `OnLivesChanged.asset` — `GameEvent<int>`
  - `OnFragmentsChanged.asset` — `GameEvent<int>`
  - `OnActionUndone.asset` — `GameEvent`
  - `OnLevelReset.asset` — `GameEvent`
  - `OnLevelSkipped.asset` — `GameEvent<LevelData>`
  - `OnGhostEmotionChanged.asset` — `GameEvent<GhostEmotion>`

**Зависимости:** 1.1 (классы GameEvent/GameEvent<T> должны существовать).

**Критерий завершения:** все 18 SO-событий созданы в папке `ScriptableObjects/Events/`, видны в инспекторе.

---

### Задача 2.12 — Регистрация сервисов в BootInitializer

**Суть:** обновить `BootInitializer` для регистрации всех реализованных сервисов в ServiceLocator.

**Что сделать:**

- Обновить `Infrastructure/Boot/BootInitializer.cs`:
  - Создать и зарегистрировать: `SaveService`, `ProgressionService`, `EconomyService`, `LivesService`, `TimerService`, `FeedbackService`, `UIService`
  - Порядок инициализации: SaveService → ProgressionService → EconomyService → LivesService → остальные
  - Загрузить `PlayerSaveData` через `SaveService.Load()` и передать в сервисы прогрессии/экономики/жизней
  - После инициализации: `SceneFlowManager.LoadScene("Hub")`
  - **Дополнение (API.md §10.5):** В итоговом виде (после задач 1.12–1.13 и 2.1a–2.15) порядок инициализации должен включать: 1) NetworkMonitor 2) AuthService 3) LocalSaveService 4) CloudSaveClient + мерж 5) HybridSaveService 6) ContentService 7) HybridEconomyService 8) HybridLivesService 9) HybridShopService 10) Остальные сервисы. На текущем этапе — регистрация Local-сервисов, Hybrid-обёртки добавятся в задачах 2.1a–2.15.

**Зависимости:** 2.1–2.6, 2.11.

**Критерий завершения:** все сервисы доступны через `ServiceLocator.Get<IService>()` после загрузки Boot-сцены.

---

### Задача 2.1a — CloudSaveClient, HybridSaveService, SaveMerger (NEW — API.md §6.2, §8, §10, §11)

**Суть:** реализовать облачные сохранения и гибридный сервис с мержем конфликтов.

**Файлы:**

- `Infrastructure/Save/CloudSaveClient.cs` — REST-клиент облачных сохранений:
  - `LoadFromCloud() : PlayerSaveData` — `GET /save`. Если `exists == false` → null.
  - `SaveToCloud(PlayerSaveData, int expectedVersion)` — `PUT /save` с optimistic lock.
  - При `409 SAVE_CONFLICT` → вернуть серверное сохранение из `details.serverSave` для мержа.
- `Infrastructure/Save/SaveMerger.cs` — логика мержа конфликтов (API.md §8.2):
  - `Merge(PlayerSaveData local, PlayerSaveData server) : PlayerSaveData`
  - Для каждого `levelProgress`: `isCompleted = local || server`, `bestStars = max`, `bestTime` = min (исключая 0), `attempts = max`
  - Для каждого `sectorProgress`: `state = max` по порядку Locked < Available < InProgress < Completed, `starsCollected = max`, `controlLevelPassed = local || server`
  - `totalFragments = server.totalFragments` (сервер — source of truth для транзакционных ресурсов)
  - `currentLives = server.currentLives` (сервер пересчитывает авторитетно)
  - `ownedItems = union(local, server)`
  - `consumables = server` (сервер — source of truth)
  - `version = server.version + 1`
- `Infrastructure/Save/HybridSaveService.cs` — составной сервис, реализует `ISaveService`:
  - `Load()`: 1) загрузить локальное 2) попробовать загрузить серверное (async, не блокировать) 3) при наличии обоих — мерж через `SaveMerger` 4) при отсутствии сети — вернуть локальное
  - `Save()`: 1) сохранить локально (мгновенно) 2) поставить в очередь на облачную синхронизацию (`SyncQueue`)
  - Использует `NetworkMonitor.IsOnline` для принятия решения

**Зависимости:** 2.1, 1.12, 1.13.

**Критерий завершения:** при наличии сети сохранение синхронизируется с облаком, конфликты версий мержатся корректно, при отсутствии сети — fallback на локальное.

---

### Задача 2.3a — ServerEconomyService, HybridEconomyService (NEW — API.md §6.3, §10, §11)

**Суть:** реализовать серверную экономику и гибридный сервис.

**Файлы:**

- `Meta/Economy/ServerEconomyService.cs` — REST-обёртка:
  - `GetBalance()` — `GET /economy/balance`
  - `PostTransaction(type, amount, reason, referenceId)` — `POST /economy/transaction` с `Idempotency-Key`
  - Обработка ошибок: `422 INSUFFICIENT_FUNDS`, `400 INVALID_TRANSACTION`
  - Для `reason: "skip_level"` — обработка ответа с `progressUpdate` и `newSaveVersion`
- `Meta/Economy/HybridEconomyService.cs` — составной сервис, реализует `IEconomyService`:
  - Online: проксирует через `ServerEconomyService`, обновляет локальный баланс по ответу сервера
  - Offline: выполняет через `LocalEconomyService`, ставит транзакцию в `SyncQueue`
  - Серверный баланс — source of truth (при расхождении после sync → принять серверный)

**Зависимости:** 2.3, 1.12.

**Критерий завершения:** транзакции проходят через сервер при online, при offline — локально с очередью на синхронизацию.

---

### Задача 2.4a — ServerLivesService, HybridLivesService (NEW — API.md §6.4, §10, §11)

**Суть:** реализовать серверные жизни и гибридный сервис.

**Файлы:**

- `Meta/Lives/ServerLivesService.cs` — REST-обёртка:
  - `GetLivesState()` — `GET /lives` (сервер пересчитывает жизни по `lastLifeRestoreTimestamp`)
  - `RestoreOne()` — `POST /lives/restore` (за фрагменты)
  - `RestoreAll()` — `POST /lives/restore-all` (за фрагменты, стоимость = `restoreCostFragments × (maxLives - currentLives)`)
  - Обработка ошибок: `422 INSUFFICIENT_FUNDS`, `400 INVALID_REQUEST` (жизни уже полные)
- `Meta/Lives/HybridLivesService.cs` — составной сервис, реализует `ILivesService`:
  - Online: делегирует серверу, обновляет локальное состояние по ответу
  - Offline: таймер по локальным часам, небольшая погрешность допустима
  - Серверное время — source of truth для таймера

**Зависимости:** 2.4, 1.12.

**Критерий завершения:** жизни синхронизируются с сервером при online, при offline — таймер по локальным часам.

---

### Задача 2.13 — ContentService (Remote Config) (NEW — API.md §6.6, §7.3, §11)

**Суть:** реализовать загрузчик удалённой конфигурации — секторы, уровни, баланс. С fallback на bundled JSON.

**Файлы:**

- `Infrastructure/Network/ContentService.cs`:
  - `CheckManifest()` — `GET /content/manifest`, сравнить `contentVersion` с локальным
  - `FetchSectors()` — `GET /content/sectors`
  - `FetchSectorLevels(sectorId)` — `GET /content/sectors/{sectorId}/levels`
  - `FetchBalanceConfig()` — `GET /content/balance` → `{ maxLives, restoreIntervalSeconds, restoreCostFragments, skipLevelCostFragments, improvementBonusPerStar, hintCostFragments }`
  - Поддержка `ETag` / `If-None-Match` для условных запросов (304 Not Modified)
  - Diff-обновление: скачивать только изменённые секторы (по `version` в манифесте)
  - Кэширование в `Application.persistentDataPath/content/`
  - Конвертация серверного `LevelDefinition` (JSON) → рантайм-объект (или заполнение SO полей)
- Bundled fallback (API.md §7.3):
  - В билд включить JSON с полным набором всех 100 уровней и 5 секторов:
    - `content/sectors.json` — все 5 секторов (`SectorDefinition[]`)
    - `content/levels/` — все 100 уровней (`LevelDefinition[]`), по файлу на сектор
    - `content/balance.json` — глобальные настройки баланса
    - `content/shop_catalog.json` — каталог магазина (`ShopItem[]`)
  - При первом запуске без сети — использовать bundled
  - При наличии сети — обновить кеш, заменить bundled

**Зависимости:** 1.12, 1.3.

**Критерий завершения:** при запуске контент загружается с сервера (или из кеша/bundled), баланс-конфиг доступен всем сервисам.

---

### Задача 2.14 — Reconciliation System (POST /check/level) (NEW — API.md §6.7)

**Суть:** реализовать клиентскую часть серверной проверки ответов (reconciliation) и атомарные операции прохождения уровня.

**Файлы:**

- `Infrastructure/Network/LevelCheckClient.cs`:
  - `CheckLevel(levelId, PlayerAnswer, elapsedTime, errorsBeforeSubmit, attempt)` — `POST /check/level`
  - Обработка ответа:
    - Верный ответ: `result` + `progressUpdate` + `newSaveVersion`
    - Неверный ответ: `result` + `livesUpdate` + `levelFailed` + `failReason` + `newSaveVersion`
  - Обработка ошибки `422 NO_LIVES` (у игрока 0 жизней, попытка не засчитана)
- `Gameplay/Level/ReconciliationHandler.cs`:
  - Интегрируется в `LevelController`
  - Flow: 1) Локальная проверка (мгновенный фидбек) → 2) Серверная проверка (reconciliation) → 3) Если расхождение — серверный результат авторитетен
  - При offline: результат ставится в `SyncQueue` как `check_level`, обрабатывается при reconnect
  - Сервер **не отклоняет** запросы для заблокированных уровней (для корректной обработки offline-очереди)
  - При повторной отправке завершённого уровня: сервер возвращает лучший результат или обновляет при улучшении
  - Использовать `newSaveVersion` как `expectedVersion` для последующего `PUT /save`

**Зависимости:** 1.9, 1.10, 1.12, 2.1a.

**Критерий завершения:** после прохождения уровня отправляется `POST /check/level`, при расхождении с локальной проверкой — применяется серверный результат.

---

### Задача 2.15 — SyncQueue и Offline-first инфраструктура (NEW — API.md §7.4)

**Суть:** реализовать очередь отложенных мутаций для offline-режима и её обработку при восстановлении сети.

**Файлы:**

- `Infrastructure/Network/SyncQueue.cs`:
  - Хранение: `Application.persistentDataPath/sync_queue.json`
  - Формат: массив `PendingOperation` с полями `id` (UUID), `type` (check_level / shop_purchase / economy_transaction), `endpoint`, `payload`, `createdAt`, `retries`
  - Методы: `Enqueue(PendingOperation)`, `Dequeue() : PendingOperation`, `Peek()`, `Count`, `Clear()`
  - Персистентность: переживает перезапуск приложения
  - **Результаты прохождения уровней** синхронизируются через `check_level`, а **не** через `economy_transaction`
- `Infrastructure/Network/SyncProcessor.cs`:
  - Подписывается на `NetworkMonitor.OnConnectivityChanged(true)` → начинает обработку
  - Обработка FIFO, каждая операция независимо
  - Для `check_level`: если сервер отклоняет (reconciliation mismatch) → принять серверное решение, продолжить остальные
  - Каскадных откатов нет: если уровень 5 отклонён, уровни 6 и 7 обрабатываются нормально
  - Принцип «прогресс всегда вперёд»: в спорных ситуациях сервер принимает прогрессию клиента
  - После обработки всей очереди: `PUT /save` с финальным состоянием + `POST /analytics/events` (буферизированные)
  - Flow при reconnect (API.md Appendix A.4): 1) `POST /auth/refresh` 2) Обработка sync queue 3) `PUT /save` 4) `POST /analytics/events`

**Зависимости:** 1.12, 2.1a.

**Критерий завершения:** при offline мутации складываются в очередь, при reconnect — обрабатываются последовательно, конфликты разрешаются корректно.

---

## Фаза 3 — Расширение механик

### Задача 3.1 — AnswerSystem: режим AdjustGraph (корректировка графика)

**Суть:** реализовать режим, в котором игрок корректирует параметры функции слайдерами или перетаскиванием кривой.

**Файлы:**

- `Gameplay/FunctionEditor/FunctionEditor.cs` — MonoBehaviour:
  - Создаёт UI-слайдеры для коэффициентов функции (k, b для линейной; a, h, k для квадратичной и т.д.)
  - При изменении слайдера → пересчитывает коэффициенты → вызывает SO-событие `OnFunctionChanged`
  - Поддержка drag-точек: перетаскивание контрольных точек на графике → пересчёт коэффициентов
  - `GetCurrentFunction() : FunctionDefinition` — текущее состояние функции
  - **Дополнение (Architecture.md §5.2):** Ограничение `MaxAdjustments` — если `LevelData.MaxAdjustments > 0`, блокировать дальнейшие изменения после достижения лимита перестроек графика за попытку. Счётчик сбрасывается при `Undo`, `Reset` или новой попытке.

**Что ещё сделать:**

- Обновить `AnswerSystem.cs` — режим `AdjustGraph`: вместо выбора из вариантов → показать `FunctionEditor` + эталонный график (ComparisonOverlay)
- Обновить `GraphRenderer` — подписка на `OnFunctionChanged`, перерисовка в реальном времени
- Обновить `ValidationSystem` — сравнение графика игрока с эталоном по среднеквадратичному отклонению на контрольных точках

**Зависимости:** 2.9, 2.10.

**Критерий завершения:** игрок двигает слайдеры → график обновляется → при подтверждении сравнивается с эталоном.

---

### Задача 3.2 — GraphRenderer: параболы и синусоиды

**Суть:** расширить рендеринг графиков для квадратичных и тригонометрических функций.

**Что сделать:**

- Обновить `FunctionEvaluator.cs`:
  - `Quadratic: y = a*(x - h)² + k` — (Coefficients: [a, h, k])
  - `Sinusoidal: y = a*sin(b*x + c) + d` — (Coefficients: [a, b, c, d])
  - `Mixed` — комбинация (оценка по составным выражениям)
- Обновить `CurveRenderer.cs` — увеличить количество сэмплов для плавных кривых (80-100 для парабол, 100-150 для синусоид)
- Обновить `FunctionEditor.cs` — генерация слайдеров в зависимости от `FunctionType` (разное количество и имена коэффициентов)

**Зависимости:** 3.1.

**Критерий завершения:** параболы и синусоиды корректно рисуются, слайдеры работают для всех типов функций.

---

### Задача 3.3 — AnswerSystem: режимы BuildFunction, IdentifyError, RestoreConstellation

**Суть:** реализовать оставшиеся режимы заданий.

**Что сделать:**

- `BuildFunction` — полный ввод функции: игрок выбирает тип функции + задаёт все коэффициенты через `FunctionEditor`. Нет эталонного графика для сравнения (только контрольные точки).
- `IdentifyError` — tap на звёзды для выбора «лишней» (дистрактора). `StarInteraction` расширить: режим «выбор ошибочной звезды». Валидация через `IsDistractor` из `StarConfig`.
- `RestoreConstellation` — пошаговое размещение звёзд: игрок должен разместить звёзды в правильном порядке. Каждое правильное размещение раскрывает часть созвездия.

**Дополнение (Architecture.md §5.8 — GraphVisibilityConfig):**

- Реализовать поддержку `GraphVisibility.PartialReveal` для режимов AdjustGraph и BuildFunction: если `PartialReveal == true`, показать только `InitialVisibleSegments` сегментов графика в начале, раскрывать по `RevealPerCorrectAction` сегментов за каждое правильное действие.
- Обновить `CurveRenderer` — добавить метод `SetVisibleSegments(int count)` для управления частичной видимостью графика.

**Зависимости:** 3.2, 1.7.

**Критерий завершения:** все 6 типов заданий (`TaskType`) работают корректно.

---

### Задача 3.4 — HintSystem: подсказки (авто + покупные)

**Суть:** реализовать систему подсказок на уровнях. **Важно:** существует два независимых типа подсказок (Architecture.md §5.7).

**Файлы:**

- `Gameplay/Level/HintSystem.cs` — MonoBehaviour:
  - **Авто-подсказки (бесплатные):**
    - Получает `HintConfig[]` из `LevelData` (управляется флагом `LevelData.ShowHints`)
    - Отслеживает триггеры: `OnLevelStart`, `AfterErrors(N)`, `OnFirstInteraction`
    - При срабатывании — показывает UI-подсказку: текст + выделение позиции (`HighlightPosition`)
    - Подсказка исчезает по tap или через таймаут
    - Авто-подсказки **НЕ расходуют** покупные подсказки — это две независимые системы
  - **Покупные подсказки (расходуемые):**
    - Хранятся в `PlayerSaveData.Consumables["hints"]`
    - Кнопка `HintButton` в HUD — при нажатии расходуется одна единица
    - Если подсказок 0 — показать предложение купить в магазине
    - Покупаются за фрагменты (`hintCostFragments` из баланс-конфига)
    - Стоимость одной покупной подсказки: `hintCostFragments` (API.md §6.6 `GET /content/balance`, default 10)
  - Аналитика: отправлять событие `hint_used` (API.md §6.8) с `levelId` и `hintIndex`

**Зависимости:** 1.9, 2.3 (если подсказки платные).

**Критерий завершения:** подсказки появляются по триггерам, текст отображается корректно.

---

### Задача 3.5 — Анимации звёзд и визуальное восстановление созвездий

**Суть:** заменить заглушки анимаций звёзд на полноценные, реализовать восстановление созвездия.

**Что сделать:**

- Обновить `StarAnimator.cs`:
  - `PlayAppear()` — fade-in (CanvasGroup/SpriteRenderer.color alpha 0→1) + scale (0.5→1.0) с easing
  - `PlayPlace()` — flash (белый спрайт поверх на 0.1 сек) + glow pulse (увеличение/уменьшение интенсивности glow)
  - `PlayError()` — shake (смещение position ±0.05 единиц, 3-4 цикла) + красный flash
  - `PlayRestore()` — золотой glow + линия к соседней звезде (LineRenderer между точками)
- Реализовать анимацию восстановления созвездия:
  - В `StarManager.cs` добавить `PlayConstellationRestore()` — последовательно анимирует все звёзды через `PlayRestore()`, рисует линии между ними
  - `LevelController` вызывает это после финального уровня (тип `Final` / `RestoreConstellation`)
- Использовать DOTween для анимаций (установить пакет DOTween через Package Manager или `.unitypackage` в `Plugins/`).

**Зависимости:** 1.7, 3.3.

**Критерий завершения:** анимации воспроизводятся плавно, восстановление созвездия визуально впечатляет.

---

### Задача 3.6 — Попапы: Pause, NoLives, SkipLevel, SectorUnlock

**Суть:** реализовать основные попапы, возникающие в ходе игры.

**Файлы:**

- `UI/Popups/PausePopup.cs` — кнопки: Продолжить, Настройки, Выход в хаб. Тормозит `TimerService`.
- `UI/Popups/NoLivesPopup.cs` — показывает таймер до следующего восстановления жизни + кнопка «Восстановить за фрагменты» (одну жизнь) + **кнопка «Восстановить все»** (API.md §6.4 `POST /lives/restore-all`, стоимость = `restoreCostFragments × (maxLives - currentLives)`) + кнопка «Ждать».
- `UI/Popups/SkipLevelPopup.cs` — подтверждение пропуска: стоимость в фрагментах, предупреждение (1 звезда, без награды). Кнопки: Пропустить, Отмена.
- `UI/Popups/SectorUnlockPopup.cs` — анимированное сообщение о разблокировке нового сектора.

**Зависимости:** 2.6 (UIService), 2.3, 2.4.

**Критерий завершения:** все попапы открываются/закрываются корректно, кнопки вызывают правильные действия сервисов.

---

### Задача 3.7 — NotificationService

**Суть:** реализовать систему бэйджей и уведомлений.

**Файлы:**

- `Meta/Notifications/INotificationService.cs` — интерфейс: `HasNewContent(sectorId)`, `HasUnclaimedRewards()`, `MarkSeen(contentId)`, `GetBadgeCount(context)`.
- `Meta/Notifications/NotificationService.cs` — реализация:
  - Отслеживает: новые разблокированные секторы, восстановленные жизни, доступный контент
  - Бэйджи на UI-элементах хаба (красные точки с числом)
  - `MarkSeen()` убирает бэйдж после просмотра

**Зависимости:** 2.2, 2.4, 2.7.

**Критерий завершения:** на хабе появляются бэйджи при разблокировке сектора, бэйджи исчезают после просмотра.

---

### Задача 3.8 — LoadingOverlay и TransitionOverlay

**Суть:** реализовать UI-оверлей загрузки (на DontDestroyOnLoad) и визуальные переходы между экранами.

**Файлы:**

- `UI/Overlays/LoadingOverlay.cs` — наследник `UIScreen`. Полноэкранное затемнение/анимация загрузки. Методы: `Show()`, `Hide()`, `SetProgress(float)` (если нужен прогресс-бар). Применяется как страховочный экран при аддитивной загрузке Level-сцены (если загрузка занимает > N мс) и при первом переходе Boot → Hub.
- `UI/Overlays/TransitionOverlay.cs` — переходы: fade-in/fade-out (CanvasGroup alpha), slide. Методы: `TransitionIn(Action onComplete)`, `TransitionOut(Action onComplete)`.
- Интеграция с `SceneFlowManager` — использовать `LoadingOverlay` при аддитивной загрузке сцен, `TransitionOverlay` при переключении экранов.

**Зависимости:** 1.5, 2.6.

**Критерий завершения:** переход Hub ⇄ Level сопровождается плавным переходом, переключение между экранами — fade.

---

### Задача 3.9 — Создание SO-ассетов для уровней (контент)

**Суть:** создать ScriptableObject-ассеты для всех 100 уровней (5 секторов × 20 уровней).

**Что сделать:**

- Для каждого сектора создать `SectorData` SO-ассет в `Assets/ScriptableObjects/Sectors/`
- Для каждого уровня создать `LevelData` SO-ассет в `Assets/ScriptableObjects/Levels/Sector_N/`
- Для каждой эталонной функции создать `FunctionDefinition` SO-ассет в `Assets/ScriptableObjects/Functions/`
- Заполнить данные согласно шаблону сектора (Architecture.md раздел 12.2):
  - Уровни 1-2: Tutorial
  - Уровни 3-6: Normal
  - Уровень 7: Bonus
  - Уровни 8-11: Normal
  - Уровень 12: Bonus
  - Уровни 13-18: Normal
  - Уровень 19: Control
  - Уровень 20: Final
- Настроить для каждого уровня: `TaskType`, `Stars[]`, `AnswerOptions[]`, `ReferenceFunctions[]`, `StarRating`, `FragmentReward`
- Рекомендация: написать Editor-скрипт для массовой генерации шаблонов SO-ассетов
- **Дополнение (API.md §7.3):** Параллельно создать **bundled JSON fallback** для offline-первого запуска:
  - `Assets/Resources/content/sectors.json` — все 5 секторов (`SectorDefinition[]`)
  - `Assets/Resources/content/levels/sector_N.json` — все 100 уровней (`LevelDefinition[]`), по файлу на сектор
  - `Assets/Resources/content/balance.json` — глобальные настройки баланса (`maxLives`, `restoreIntervalSeconds`, `restoreCostFragments`, `skipLevelCostFragments`, `improvementBonusPerStar`, `hintCostFragments`)
  - `Assets/Resources/content/shop_catalog.json` — каталог магазина (`ShopItem[]`)
  - Рекомендация: Editor-скрипт для экспорта SO-ассетов в JSON (серверный формат `LevelDefinition` / `SectorDefinition`)

**Зависимости:** 3.1-3.3 (все механики должны быть готовы для заполнения).

**Критерий завершения:** все 100 уровней имеют SO-ассеты, каждый уровень проходим.

---

## Фаза 4 — Полировка и контент

### Задача 4.1 — AudioService: музыка и SFX

**Суть:** реализовать полноценную аудио-систему.

**Файлы:**

- `Meta/Audio/IAudioService.cs` — интерфейс: `PlayMusic(AudioClip)`, `StopMusic()`, `PlaySFX(AudioClip)`, `SetMusicVolume(float)`, `SetSFXVolume(float)`.
- `Meta/Audio/AudioService.cs` — реализация: хранит ссылки на `MusicPlayer` и `SFXPlayer`, делегирует вызовы.
- `Meta/Audio/MusicPlayer.cs` — фоновая музыка: два AudioSource для crossfade, метод `CrossfadeTo(AudioClip, float duration)`.
- `Meta/Audio/SFXPlayer.cs` — пул из 8-12 AudioSource. Метод `Play(AudioClip)` — берёт свободный Source, воспроизводит, возвращает в пул.
- Интеграция с `FeedbackService` — заменить Debug.Log заглушки на реальные вызовы `AudioService`.

**Зависимости:** 2.5.

**Критерий завершения:** музыка играет на хабе и уровнях с crossfade, SFX воспроизводятся при действиях игрока.

---

### Задача 4.2 — Сюжетные вставки (CutscenePopup)

**Суть:** реализовать попап для сюжетных вставок между секторами.

**Файлы:**

- `UI/Popups/CutscenePopup.cs` — наследник `UIPopup`:
  - Получает `CutsceneData` SO
  - Итерирует по `CutsceneFrame[]`: показывает фон, спрайт персонажа, текст, эмоцию
  - Переход между кадрами: по tap (если `Duration == 0`) или по таймеру (`Duration > 0`)
  - Анимация текста (typewriter-эффект)
  - Кнопка «Пропустить» — закрывает катсцену
  - По завершении — вызывает callback

**Зависимости:** 1.3, 2.6.

**Критерий завершения:** катсцена показывается при входе в сектор и при его завершении, кадры сменяются корректно.

---

### Задача 4.3 — ShopService и ShopScreen (Local + подготовка к Hybrid)

**Суть:** реализовать магазин для покупки предметов за фрагменты. **Имя класса: `LocalShopService`** — локальная реализация, позже обернутая `HybridShopService` (задача 4.3a).

**Файлы:**

- `Meta/Shop/IShopService.cs` — интерфейс: `GetAvailableItems()`, `PurchaseItem(string itemId) : bool`, `IsItemOwned(string itemId)`.
- `Meta/Shop/LocalShopService.cs` — реализация:
  - Хранит список `ShopItem[]` (из конфига SO или bundled JSON)
  - `PurchaseItem` вызывает `EconomyService.SpendFragments()`
  - При покупке consumable: обновляет `PlayerSaveData.Consumables` (например `hints += 5`)
  - При покупке перманентного: добавляет в `PlayerSaveData.OwnedItems`
  - **Дополнение (API.md §6.5):** Хранить `catalogVersion` и проверять периодически. Категории товаров: `Hints`, `Lives`, `Skip`, `Customization`. Каждый ShopItem: `itemId`, `category`, `price`, `displayName`, `description`, `iconId`, `isConsumable`, `isAvailable`.
- `UI/Screens/ShopScreen.cs` — наследник `UIScreen`. Отображение товаров по категориям (подсказки, жизни, кастомизация). Кнопка покупки + подтверждение.

**Зависимости:** 2.3, 2.6.

**Критерий завершения:** магазин отображает товары, покупка списывает фрагменты, купленные предметы отмечаются.

---

### Задача 4.3a — ServerShopService, HybridShopService (NEW — API.md §6.5, §10, §11)

**Суть:** реализовать серверный магазин и гибридный сервис с поддержкой offline-покупок.

**Файлы:**

- `Meta/Shop/ServerShopService.cs` — REST-обёртка:
  - `FetchCatalog()` — `GET /shop/items`, получает список товаров + `catalogVersion`
  - `Purchase(itemId, cachedPrice)` — `POST /shop/purchase` с `cachedPrice` (цена, которую видел клиент)
  - Обработка ошибок: `422 INSUFFICIENT_FUNDS`, `404 NOT_FOUND`, `400 INVALID_REQUEST` (товар уже куплен для не-consumable)
  - Обработка `consumablesUpdate` в ответе (обновление `PlayerSaveData.Consumables`)
- `Meta/Shop/HybridShopService.cs` — составной сервис, реализует `IShopService`:
  - Online: проксирует через `ServerShopService`, обновляет локальный каталог и баланс
  - **Offline-покупки (API.md §6.5):** при offline покупка выполняется локально, `cachedPrice` сохраняется в `SyncQueue`. При reconnect сервер принимает покупку по `cachedPrice`, даже если цена уже изменилась. После синхронизации клиент обновляет кеш каталога через `GET /shop/items`.
  - Периодическая проверка `catalogVersion` при online (через `GET /content/manifest`)

**Зависимости:** 4.3, 1.12.

**Критерий завершения:** покупки проходят через сервер при online, при offline — локально с cachedPrice, синхронизация после reconnect корректна.

---

### Задача 4.4 — SettingsPopup: настройки звука и вибрации

**Суть:** реализовать попап настроек.

**Файлы:**

- `UI/Popups/SettingsPopup.cs` — наследник `UIPopup`:
  - Слайдер громкости музыки → `AudioService.SetMusicVolume()`
  - Слайдер громкости SFX → `AudioService.SetSFXVolume()`
  - Переключатель вибрации → `FeedbackService.SetHapticsEnabled()`
  - Все настройки сохраняются в `PlayerPrefs`
  - Кнопка «Закрыть»

**Зависимости:** 4.1, 3.6.

**Критерий завершения:** настройки применяются в реальном времени, сохраняются между сессиями.

---

### Задача 4.5 — Онбординг первых уровней

**Суть:** настроить обучающие уровни (Tutorial) с пошаговыми подсказками.

**Что сделать:**

- Настроить `HintConfig[]` для первых 2 уровней каждого сектора:
  - Сектор 1, уровень 1: «Нажми на звезду», «Выбери правильную координату», «Подтверди ответ»
  - Сектор 1, уровень 2: «Обрати внимание на оси», «Координата (x, y)»
  - Секторы 2-5, уровни 1-2: введение в новую механику (прямые, параболы, синусоиды, смешанные)
- Реализовать затемнение экрана с подсветкой нужного элемента (mask + highlight):
  - Полноэкранный затемняющий overlay
  - «Дырка» в overlay в позиции `HighlightPosition`
  - Текст подсказки рядом
- Интеграция с `HintSystem` — туториальные подсказки обязательны (нельзя пропустить на первом прохождении)

**Зависимости:** 3.4, 3.9.

**Критерий завершения:** новый игрок проходит первые 2 уровня с пошаговым обучением без затруднений.

---

### Задача 4.6 — VFX и Particle Systems

**Суть:** добавить визуальные эффекты для ключевых игровых моментов.

**Что сделать:**

- Particle System для сбора звезды: золотые частицы, расходящиеся от точки (burst, 0.5 сек)
- Particle System для ошибки: красные частицы, shake-подобное (burst, 0.3 сек)
- Particle System для завершения уровня: фейерверк/конфетти из звёзд (burst, 2 сек)
- Particle System для восстановления созвездия: цепная реакция glow по линиям (trail + emission по LineRenderer)
- Particle System для разблокировки сектора: свечение + раскрытие
- Создать префабы в `Assets/Prefabs/Effects/`
- Интеграция с `FeedbackService` — при `PlayFeedback()` инстанцировать соответствующий VFX

**Зависимости:** 3.5, 4.1.

**Критерий завершения:** все ключевые действия сопровождаются визуальными эффектами.

---

### Задача 4.7 — Оптимизация для мобильных устройств

**Суть:** профилирование и оптимизация производительности.

**Что сделать:**

- **Sprite Atlas**: собрать все UI-спрайты и игровые объекты в атласы (`SpriteAtlas`)
- **Object Pooling**: реализовать пулинг для `StarEntity`, VFX-частиц, элементов UI-списков (варианты ответов)
- **Canvas splitting**: разделить Canvas на статичный (фон, рамки) и динамичный (таймер, анимации)
- **LineRenderer**: ограничить сэмплы (50-100 для линейных, до 150 для синусоид)
- **GC-оптимизация**: убрать аллокации в Update (кэширование, StringBuilder, пулинг)
- **Batching**: настроить ОрдерслOrder in Layer и Sorting Layers для минимизации draw calls
- **Profiling**: прогнать Unity Profiler на целевом устройстве, убедиться: 60 FPS стабильно, < 300 MB RAM, загрузка сцены < 2 сек.

**Зависимости:** все предыдущие задачи.

**Критерий завершения:** целевые метрики (60 FPS, < 300 MB, < 2 сек. загрузка) достигнуты на среднем Android-устройстве.

---

### Задача 4.8 — AnalyticsService

**Суть:** реализовать отправку аналитических событий для отслеживания поведения игроков.

**Файлы:**

- `Infrastructure/Analytics/IAnalyticsService.cs` — интерфейс: `TrackEvent(string eventName, Dictionary<string, object> parameters)`.
- `Infrastructure/Analytics/AnalyticsService.cs` — реализация:
  - На данном этапе: логирование в консоль (или Firebase Analytics, если подключён)
  - Ключевые события (API.md §6.8):
    - `session_start`, `session_end` (duration)
    - `level_start` (levelId, sectorId, attempt)
    - `level_complete` (levelId, sectorId, stars, time, errors, attempt)
    - `level_fail` (levelId, sectorId, reason, attempt)
    - `level_skip` (levelId, sectorId, cost)
    - `sector_unlock` (sectorId)
    - `purchase` (itemId, cost, currency)
    - `hint_used` (levelId, hintIndex)
    - `life_lost` (levelId, remainingLives)
    - `life_restored` (method: timer | fragments)
    - `action_undo` (levelId, actionType)
    - `level_reset` (levelId, sectorId)
  - Регистрация в `BootInitializer`
  - **Дополнение (API.md §6.8, §7.2):** Локальный буфер событий на диске (`persistentDataPath`). Отправка батчами (API.md `POST /analytics/events`): каждые 30 сек или при `OnApplicationPause`. Неотправленные события переживают перезапуск приложения. Каждое событие: `{ eventName, timestamp, sessionId, params }`. Ответ сервера `202 Accepted`.

**Зависимости:** 1.1.

**Критерий завершения:** ключевые события логируются при прохождении уровней.

---

### Задача 4.8a — REST Analytics Sender + Offline Queue (NEW — API.md §6.8, §11)

**Суть:** реализовать полноценную отправку аналитики через REST API с offline-буферизацией.

**Файлы:**

- `Infrastructure/Analytics/AnalyticsSender.cs`:
  - Батчевая отправка: `POST /analytics/events` с массивом `AnalyticsEvent[]`
  - Периодичность: каждые 30 секунд или при `OnApplicationPause`
  - Ответ сервера: `202 Accepted` с `{ accepted, rejected }`
  - При offline: события буферизуются на диск (`Application.persistentDataPath/analytics_queue.json`)
  - При reconnect: все буферизированные события отправляются одним (или несколькими) батчем
  - Неотправленные события переживают перезапуск приложения
  - Формат каждого события: `{ eventName, timestamp, sessionId, params }`
- Интеграция с `AnalyticsService` — заменить Debug.Log-заглушки на реальную отправку через `AnalyticsSender`
- Интеграция с `SyncProcessor` — при reconnect отправка буферизированной аналитики (шаг 4 flow из API.md App. A.4)

**Зависимости:** 4.8, 1.12.

**Критерий завершения:** аналитические события отправляются батчами на сервер, при offline буферизуются и отправляются при reconnect.

---

### Задача 4.11 — Безопасность: certificate pinning, шифрование, защита от replay (NEW — API.md §9)

**Суть:** реализовать меры безопасности клиент-серверного взаимодействия.

**Что сделать:**

- **Certificate Pinning** (API.md §9.1): в production-билдах подключить certificate pinning для `api.starfunc.app`. Реализация через `CertificateHandler` в `UnityWebRequest` или через нативный плагин.
- **Шифрование Refresh Token** (API.md §9.2): AES-256, ключ = комбинация `deviceId` + hardware fingerprint. Хранение в `Application.persistentDataPath`. Обновить `TokenManager` (задача 1.12).
- **Idempotency-Key** (API.md §9.5): все мутирующие запросы (`POST`, `PUT`) должны отправлять заголовок `Idempotency-Key: <uuid>`. Сервер хранит использованные ключи 24 часа. Повторный запрос с тем же ключом возвращает сохранённый результат. Обновить `ApiClient` (задача 1.12).
- **Rate Limiting обработка** (API.md §9.4): при `429 Rate Limited` — exponential backoff (1s, 2s, 4s), макс. 3 попытки. Обновить retry-стратегию в `ApiClient`.
- **Серверная валидация** (API.md §9.3): клиент не должен доверять себе в вопросах: количество заработанных фрагментов, статус жизней, стоимость покупки, правильность ответа — всё проверяется сервером.

**Зависимости:** 1.12, 1.13.

**Критерий завершения:** certificate pinning работает в production-билде, refresh token зашифрован, Idempotency-Key отправляется на всех мутирующих запросах, rate limiting обрабатывается корректно.

---

### Задача 4.9 — Локализация (если необходима)

**Суть:** подготовить инфраструктуру для мультиязычности.

**Что сделать:**

- Определить необходимость: если игра только на русском — можно отложить
- Если нужна: создать систему ключей локализации (`LocalizationService`)
- Хранить строки в JSON-файлах в `Assets/Resources/Localization/`
- Все тексты в UI заменить на ключи локализации

**Зависимости:** все UI-задачи.

**Критерий завершения:** если реализована — переключение языка работает корректно.

---

### Задача 4.10 — Финальное тестирование и сборка

**Суть:** полное сквозное тестирование и подготовка релизной сборки.

**Что сделать:**

- Пройти все 100 уровней от начала до конца
- Проверить: сохранения, прогрессию, экономику, жизни, таймеры
- Проверить: все 6 типов заданий, все типы функций
- Проверить: катсцены, попапы, магазин, настройки
- Проверить: edge cases (0 жизней, 0 фрагментов, максимум звёзд, повторное прохождение)
- Тестирование на целевых устройствах (минимум 3 Android-устройства разных уровней)
- Подготовить APK/AAB для Google Play
- Заполнить страницу в Google Play Console (описание, скриншоты, иконка)

**Зависимости:** все предыдущие задачи.

**Критерий завершения:** стабильная сборка, все уровни проходимы, нет критических багов.

---

## Диаграмма зависимостей задач

```
Фаза 0:   0.1 → 0.2 → 0.3

Фаза 1:   0.1 → 1.1 → 1.2 → 1.3 → 1.4
                  │            │       │
                  │      1.6 ←─┘   1.5 (+ 0.2)
                  │       │
                  ├── 1.8 │
                  │       ↓
                  │  1.7 (+ 1.3, 1.6)
                  │       │
                  │       ↓
                  └─ 1.9 (+ 1.3, 1.6, 1.7)
                          │
                     1.10 (+ 1.2, 1.3, 1.4)
                          │
                     1.11 (+ 1.9, 1.10)

                  0.1, 1.1 → 1.12 (ApiClient, NetworkMonitor, TokenManager)
                                │
                               1.13 (AuthService)

Фаза 2:   1.4 → 2.1 → 2.2
                  │       │
                  ├── 2.3 (+ 2.1, 1.1)
                  ├── 2.4 (+ 2.1, 1.1)
                  ├── 2.5 (+ 2.1)
                  │       │
                  │  2.6 (+ 1.11, 1.10)
                  │   │
                  │  2.7 (+ 2.2, 1.8)
                  │   │
                  │  2.8 (+ 2.7, 2.2)
                  │
                  │  2.9 (+ 1.6, 1.3)
                  │   │
                  │  2.10 (+ 2.9, 1.9)
                  │
                  │  2.11 (+ 1.1)
                  │
                  └─ 2.12 (+ 2.1–2.6, 2.11)

           --- Новые задачи Hybrid / Network ---
           2.1, 1.12, 1.13 → 2.1a (CloudSaveClient, HybridSaveService, SaveMerger)
           2.3, 1.12       → 2.3a (ServerEconomyService, HybridEconomyService)
           2.4, 1.12       → 2.4a (ServerLivesService, HybridLivesService)
           1.12, 1.3       → 2.13 (ContentService — remote config + bundled fallback)
           1.9, 1.10, 1.12, 2.1a → 2.14 (Reconciliation — POST /check/level)
           1.12, 2.1a      → 2.15 (SyncQueue + offline-first инфраструктура)

Фаза 3:   2.9, 2.10 → 3.1 → 3.2 → 3.3
                       3.4 (+ 1.9, 2.3)
                       3.5 (+ 1.7, 3.3)
                       3.6 (+ 2.6, 2.3, 2.4)
                       3.7 (+ 2.2, 2.4, 2.7)
                       3.8 (+ 1.5, 2.6)
                       3.9 (+ 3.1–3.3) ← контент + bundled JSON fallback

Фаза 4:   4.1 (+ 2.5)
           4.2 (+ 1.3, 2.6)
           4.3 (+ 2.3, 2.6)
           4.3a (+ 4.3, 1.12) ← ServerShopService, HybridShopService
           4.4 (+ 4.1, 3.6)
           4.5 (+ 3.4, 3.9)
           4.6 (+ 3.5, 4.1)
           4.7 (+ все)
           4.8 (+ 1.1)
           4.8a (+ 4.8, 1.12) ← REST analytics sender + offline queue
           4.9 (+ все UI)
           4.10 (+ все)
           4.11 (+ 1.12, 1.13) ← Безопасность
```

---

## Метрики проекта

| Метрика                   | Значение                                  |
| ------------------------- | ----------------------------------------- |
| Всего файлов C#           | ~130+                                     |
| Всего задач               | 53                                        |
| SO-ассетов для контента   | ~110 (5 секторов + 100 уровней + функции) |
| Сцен                      | 3                                         |
| Сервисов в ServiceLocator | 11                                        |
