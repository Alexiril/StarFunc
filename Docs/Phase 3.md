# Фаза 3 — Расширение механик

## ~~Задача 3.1 — AnswerSystem: режим AdjustGraph (корректировка графика)~~ (Done)

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

## ~~Задача 3.2 — GraphRenderer: параболы и синусоиды~~ (Done)

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

## Задача 3.3 — AnswerSystem: режимы BuildFunction, IdentifyError, RestoreConstellation

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

## Задача 3.4 — HintSystem: подсказки (авто + покупные)

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

## Задача 3.5 — Анимации звёзд и визуальное восстановление созвездий

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

## Задача 3.6 — Попапы: Pause, NoLives, SkipLevel, SectorUnlock

**Суть:** реализовать основные попапы, возникающие в ходе игры.

**Файлы:**

- `UI/Popups/PausePopup.cs` — кнопки: Продолжить, Настройки, Выход в хаб. Тормозит `TimerService`.
- `UI/Popups/NoLivesPopup.cs` — показывает таймер до следующего восстановления жизни + кнопка «Восстановить за фрагменты» (одну жизнь) + **кнопка «Восстановить все»** (API.md §6.4 `POST /lives/restore-all`, стоимость = `restoreCostFragments × (maxLives - currentLives)`) + кнопка «Ждать».
- `UI/Popups/SkipLevelPopup.cs` — подтверждение пропуска: стоимость в фрагментах, предупреждение (1 звезда, без награды). Кнопки: Пропустить, Отмена.
- `UI/Popups/SectorUnlockPopup.cs` — анимированное сообщение о разблокировке нового сектора.

**Зависимости:** 2.6 (UIService), 2.3, 2.4.

**Критерий завершения:** все попапы открываются/закрываются корректно, кнопки вызывают правильные действия сервисов.

---

## Задача 3.7 — NotificationService

**Суть:** реализовать систему значков и уведомлений.

**Файлы:**

- `Meta/Notifications/INotificationService.cs` — интерфейс: `HasNewContent(sectorId)`, `HasUnclaimedRewards()`, `MarkSeen(contentId)`, `GetBadgeCount(context)`.
- `Meta/Notifications/NotificationService.cs` — реализация:
  - Отслеживает: новые разблокированные секторы, восстановленные жизни, доступный контент
  - Значки на UI-элементах хаба (красные точки с числом)
  - `MarkSeen()` убирает значок после просмотра

**Зависимости:** 2.2, 2.4, 2.7.

**Критерий завершения:** на хабе появляются значки при разблокировке сектора, значки исчезают после просмотра.

---

## Задача 3.8 — LoadingOverlay и TransitionOverlay

**Суть:** реализовать UI-оверлей загрузки (на DontDestroyOnLoad) и визуальные переходы между экранами.

**Файлы:**

- `UI/Overlays/LoadingOverlay.cs` — наследник `UIScreen`. Полноэкранное затемнение/анимация загрузки. Методы: `Show()`, `Hide()`, `SetProgress(float)` (если нужен прогресс-бар). Применяется как страховочный экран при аддитивной загрузке Level-сцены (если загрузка занимает > N мс) и при первом переходе Boot → Hub.
- `UI/Overlays/TransitionOverlay.cs` — переходы: fade-in/fade-out (CanvasGroup alpha), slide. Методы: `TransitionIn(Action onComplete)`, `TransitionOut(Action onComplete)`.
- Интеграция с `SceneFlowManager` — использовать `LoadingOverlay` при аддитивной загрузке сцен, `TransitionOverlay` при переключении экранов.

**Зависимости:** 1.5, 2.6.

**Критерий завершения:** переход Hub ⇄ Level сопровождается плавным переходом, переключение между экранами — fade.

---

## Задача 3.9 — Создание SO-ассетов для уровней (контент)

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
