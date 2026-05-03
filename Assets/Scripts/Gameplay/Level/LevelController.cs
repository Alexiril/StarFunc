using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StarFunc.Core;
using StarFunc.Data;
using StarFunc.Infrastructure;
using UnityEngine;

namespace StarFunc.Gameplay
{
    public enum LevelState
    {
        None,
        Initialize,
        ShowTask,
        MemoryPreview,
        AwaitInput,
        ValidateAnswer,
        CalculateResult,
        ShowResult,
        Complete,
        Failed
    }

    /// <summary>
    /// Central level controller — drives the level lifecycle as a state machine.
    /// Coordinates CoordinatePlane, StarManager, and AnswerSystem.
    /// </summary>
    public class LevelController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] CoordinatePlane _plane;
        [SerializeField] StarManager _starManager;
        [SerializeField] AnswerSystem _answerSystem;
        [SerializeField] GraphRenderer _graphRenderer;

        [Header("SO Events")]
        [SerializeField] GameEvent<LevelData> _onLevelStarted;
        [SerializeField] GameEvent<LevelResult> _onLevelCompleted;
        [SerializeField] GameEvent _onLevelFailed;
        [SerializeField] GameEvent<StarData> _onStarCollected;
        [SerializeField] GameEvent<StarData> _onStarRejected;
        [SerializeField] GameEvent<bool> _onAnswerConfirmed;

        [Header("Testing")]
        [SerializeField] LevelData _levelData;

        LevelState _currentState = LevelState.None;
        List<StarConfig> _solutionStars;
        int _currentStarIndex;
        int _errorCount;
        int _attemptCount;
        string _failReason;
        int _visibleSegments;

        ValidationSystem _validationSystem;
        LevelResultCalculator _resultCalculator;
        ActionHistory _actionHistory;
        LevelTimer _levelTimer;
        ILivesService _livesService;
        ReconciliationHandler _reconciliation;

        public LevelState CurrentState => _currentState;
        public LevelData LevelData => _levelData;
        public int ErrorCount => _errorCount;
        public int AttemptCount => _attemptCount;
        public string FailReason => _failReason;
        public int VisibleSegments => _visibleSegments;
        public float ElapsedTime => _levelTimer?.GetElapsedTime() ?? 0f;
        public ActionHistory ActionHistory => _actionHistory;
        public LevelTimer Timer => _levelTimer;
        public AnswerSystem AnswerSystem => _answerSystem;

        void Start()
        {
            // Read level data set by SceneFlowManager before additive load.
            var activeLevel = LevelData.ActiveLevel;
            if (activeLevel != null)
            {
                Initialize(activeLevel);
                return;
            }

            // Fallback: inspector-assigned LevelData for testing.
            if (_levelData != null)
                Initialize(_levelData);
        }

        /// <summary>
        /// Set up the level from a LevelData SO.
        /// </summary>
        public void Initialize(LevelData data)
        {
            _levelData = data;
            _errorCount = 0;
            _attemptCount = 0;
            _failReason = null;

            // Resolve lives service if registered.
            _livesService = ServiceLocator.Contains<ILivesService>()
                ? ServiceLocator.Get<ILivesService>()
                : null;

            // Resolve reconciliation handler if registered.
            _reconciliation = ServiceLocator.Contains<ReconciliationHandler>()
                ? ServiceLocator.Get<ReconciliationHandler>()
                : null;

            // Block entry when the player has no lives.
            if (_livesService != null && !_livesService.HasLives())
            {
                Debug.LogWarning("[LevelController] No lives remaining — level entry blocked.");
                FailLevel("no_lives");
                return;
            }

            _validationSystem = new ValidationSystem();
            _resultCalculator = new LevelResultCalculator();
            _actionHistory = new ActionHistory();
            _levelTimer = new LevelTimer();

            SetState(LevelState.Initialize);

            // Configure coordinate plane.
            _plane.Initialize(data.PlaneMin, data.PlaneMax, data.GridStep);

            // Spawn stars.
            _starManager.SpawnStars(data.Stars);

            // Collect solution stars (player must solve these in order).
            _solutionStars = data.Stars
                .Where(s => s.BelongsToSolution)
                .ToList();

            _currentStarIndex = 0;

            // Initialize partial graph reveal.
            if (data.GraphVisibility.PartialReveal)
                _visibleSegments = data.GraphVisibility.InitialVisibleSegments;

            // Subscribe to answer confirmations.
            _answerSystem.OnAnswerConfirmed += OnAnswerSubmitted;

            // Raise level started event.
            if (_onLevelStarted) _onLevelStarted.Raise(data);

            Debug.Log($"[LevelController] Initialized level '{data.LevelId}': " +
                      $"{_solutionStars.Count} solution stars, taskType={data.TaskType}");

            // Memory Mode: show reference, then hide after duration.
            if (data.UseMemoryMode && data.MemoryDisplayDuration > 0f)
            {
                StartCoroutine(RunMemoryPreview(data.MemoryDisplayDuration));
            }
            else
            {
                _levelTimer.Start();
                ShowTask();
            }
        }

        /// <summary>
        /// Show the reference constellation for a set duration, then hide and begin play.
        /// </summary>
        IEnumerator RunMemoryPreview(float duration)
        {
            SetState(LevelState.MemoryPreview);

            // Stars are already visible from SpawnStars (InitialState).
            _starManager.ShowAll();
            _answerSystem.SetActive(false);

            Debug.Log($"[LevelController] Memory Mode: showing reference for {duration}s");

            yield return new WaitForSeconds(duration);

            // Hide all stars — player must restore from memory.
            _starManager.HideAll();

            _levelTimer.Start();
            ShowTask();
        }

        void OnDestroy()
        {
            if (_answerSystem)
                _answerSystem.OnAnswerConfirmed -= OnAnswerSubmitted;
        }

        /// <summary>
        /// Present the next star's task to the player.
        /// </summary>
        void ShowTask()
        {
            SetState(LevelState.ShowTask);

            if (_levelData.TaskType == TaskType.ChooseFunction)
            {
                // ChooseFunction: single task per level, no star iteration.
                _answerSystem.Setup(_levelData.AnswerOptions, _levelData.TaskType);
                Debug.Log("[LevelController] ShowTask: ChooseFunction mode");
                AwaitInput();
                return;
            }

            if (_levelData.TaskType == TaskType.AdjustGraph)
            {
                // AdjustGraph: single task per level, no star iteration.
                if (_levelData.ReferenceFunctions == null || _levelData.ReferenceFunctions.Length == 0)
                {
                    Debug.LogError("[LevelController] AdjustGraph requires a ReferenceFunctions[0]; aborting task.");
                    FailLevel("missing_reference_function");
                    return;
                }
                _answerSystem.SetupFunctionEdit(
                    _levelData.ReferenceFunctions[0],
                    _levelData.MaxAdjustments);
                ApplyInitialPartialReveal();
                Debug.Log("[LevelController] ShowTask: AdjustGraph mode");
                AwaitInput();
                return;
            }

            if (_levelData.TaskType == TaskType.BuildFunction)
            {
                ShowBuildFunctionTask();
                return;
            }

            if (_levelData.TaskType == TaskType.IdentifyError)
            {
                ShowIdentifyErrorTask();
                return;
            }

            if (_levelData.TaskType == TaskType.RestoreConstellation)
            {
                ShowRestoreConstellationTask();
                return;
            }

            if (_currentStarIndex >= _solutionStars.Count)
            {
                // All stars solved — calculate result.
                CalculateResult();
                return;
            }

            var targetStar = _solutionStars[_currentStarIndex];

            // Set up answer system with the level's options.
            _answerSystem.Setup(_levelData.AnswerOptions, _levelData.TaskType);

            Debug.Log($"[LevelController] ShowTask: star '{targetStar.StarId}' " +
                      $"at ({targetStar.Coordinate.x}, {targetStar.Coordinate.y})");

            AwaitInput();
        }

        void AwaitInput()
        {
            SetState(LevelState.AwaitInput);
            _answerSystem.SetActive(true);
        }

        /// <summary>
        /// Called by AnswerSystem when the player confirms an answer.
        /// </summary>
        void OnAnswerSubmitted(PlayerAnswer answer)
        {
            if (_currentState != LevelState.AwaitInput) return;

            SetState(LevelState.ValidateAnswer);

            if (_levelData.TaskType == TaskType.ChooseFunction)
            {
                HandleChooseFunctionAnswer(answer);
                return;
            }

            if (_levelData.TaskType == TaskType.AdjustGraph)
            {
                HandleAdjustGraphAnswer(answer);
                return;
            }

            if (_levelData.TaskType == TaskType.BuildFunction)
            {
                HandleBuildFunctionAnswer(answer);
                return;
            }

            if (_levelData.TaskType == TaskType.IdentifyError)
            {
                HandleIdentifyErrorAnswer(answer);
                return;
            }

            if (_levelData.TaskType == TaskType.RestoreConstellation)
            {
                HandleRestoreConstellationAnswer(answer);
                return;
            }

            bool isCorrect = ValidateChooseCoordinate(answer);

            if (_onAnswerConfirmed) _onAnswerConfirmed.Raise(isCorrect);

            var targetStar = _solutionStars[_currentStarIndex];

            _actionHistory.Push(new PlayerAction
            {
                ActionType = PlayerActionType.SelectAnswer,
                TargetId = targetStar.StarId,
                PreviousState = StarState.Active.ToString(),
                NewState = isCorrect ? StarState.Placed.ToString() : StarState.Incorrect.ToString()
            });

            if (isCorrect)
            {
                CompleteStar(targetStar);
            }
            else
            {
                FailAttempt(targetStar);
            }
        }

        void HandleChooseFunctionAnswer(PlayerAnswer answer)
        {
            var option = _answerSystem.GetOption(answer.SelectedOptionId);
            bool isCorrect = option.IsCorrect;

            // Secondary validation: compare coefficients against reference.
            if (isCorrect && option.Function && _levelData.ReferenceFunctions is { Length: > 0 })
            {
                isCorrect = _validationSystem.ValidateFunction(
                    option.Function,
                    _levelData.ReferenceFunctions[0],
                    _levelData.AccuracyThreshold);
            }

            if (_onAnswerConfirmed) _onAnswerConfirmed.Raise(isCorrect);

            _actionHistory.Push(new PlayerAction
            {
                ActionType = PlayerActionType.SelectAnswer,
                TargetId = _levelData.LevelId,
                PreviousState = LevelState.AwaitInput.ToString(),
                NewState = isCorrect ? LevelState.CalculateResult.ToString() : LevelState.AwaitInput.ToString()
            });

            if (isCorrect)
            {
                Debug.Log("[LevelController] ChooseFunction: correct answer.");
                CalculateResult();
            }
            else
            {
                _errorCount++;
                _attemptCount++;
                Debug.Log($"[LevelController] ChooseFunction: incorrect. Errors: {_errorCount}");

                if (_levelData.MaxAttempts > 0 && _attemptCount >= _levelData.MaxAttempts)
                {
                    FailLevel("max_attempts_reached");
                    return;
                }

                if (_livesService != null && !_livesService.HasLives())
                {
                    FailLevel("no_lives");
                    return;
                }

                _answerSystem.ResetSelection();
                AwaitInput();
            }
        }

        /// <summary>
        /// BuildFunction: type taken from <see cref="LevelData.ReferenceFunctions"/>[0].Type when
        /// available; no reference graph is shown — control-point stars define the target curve.
        /// </summary>
        void ShowBuildFunctionTask()
        {
            FunctionType type = (_levelData.ReferenceFunctions != null && _levelData.ReferenceFunctions.Length > 0)
                ? _levelData.ReferenceFunctions[0].Type
                : FunctionType.Linear;
            Vector2 domain = new(_levelData.PlaneMin.x, _levelData.PlaneMax.x);
            _answerSystem.SetupBuildFunction(type, domain, _levelData.MaxAdjustments);
            ApplyInitialPartialReveal();
            Debug.Log($"[LevelController] ShowTask: BuildFunction mode ({type})");
            AwaitInput();
        }

        /// <summary>
        /// IdentifyError: all spawned stars are tappable; player marks suspected distractors.
        /// </summary>
        void ShowIdentifyErrorTask()
        {
            var allStars = _starManager.GetAllStars().ToArray();
            _answerSystem.SetupIdentifyError(allStars);
            Debug.Log($"[LevelController] ShowTask: IdentifyError mode ({allStars.Length} stars)");
            AwaitInput();
        }

        /// <summary>
        /// RestoreConstellation: per-star iteration via plane taps. Each tap auto-confirms;
        /// the controller validates by distance to the next solution star's coordinate.
        /// </summary>
        void ShowRestoreConstellationTask()
        {
            if (_currentStarIndex >= _solutionStars.Count)
            {
                CalculateResult();
                return;
            }
            var nextStar = _solutionStars[_currentStarIndex];
            float threshold = _levelData.AccuracyThreshold > 0f ? _levelData.AccuracyThreshold : 0.5f;
            _answerSystem.SetupRestoreConstellationStep(nextStar, threshold);
            Debug.Log($"[LevelController] ShowTask: RestoreConstellation step " +
                      $"{_currentStarIndex + 1}/{_solutionStars.Count} (target '{nextStar.StarId}')");
            AwaitInput();
        }

        /// <summary>
        /// If the level has PartialReveal enabled, hide everything beyond the initial
        /// segment count on the comparison overlay (the reference curve shown by
        /// AdjustGraph). BuildFunction has no reference to clip — the call is harmless
        /// (no-op when no overlay is drawn). Per-correct-action increments aren't wired
        /// for these modes since "correct action" is ambiguous when the answer is a
        /// single confirm; designers can drive further reveals via custom hooks.
        /// </summary>
        void ApplyInitialPartialReveal()
        {
            if (_graphRenderer == null) return;
            if (!_levelData.GraphVisibility.PartialReveal) return;

            int initial = Mathf.Max(0, _levelData.GraphVisibility.InitialVisibleSegments);
            _graphRenderer.SetComparisonVisibleSegments(initial);
        }

        void HandleAdjustGraphAnswer(PlayerAnswer answer)
        {
            // Delegate to ValidationSystem (RMS over control points).
            var validationResult = _validationSystem.ValidateLevel(_levelData, answer);
            bool isCorrect = validationResult.IsValid;

            if (_onAnswerConfirmed) _onAnswerConfirmed.Raise(isCorrect);

            _actionHistory.Push(new PlayerAction
            {
                ActionType = PlayerActionType.SelectAnswer,
                TargetId = _levelData.LevelId,
                PreviousState = LevelState.AwaitInput.ToString(),
                NewState = isCorrect ? LevelState.CalculateResult.ToString() : LevelState.AwaitInput.ToString()
            });

            if (isCorrect)
            {
                Debug.Log("[LevelController] AdjustGraph: correct answer.");
                CalculateResult();
                return;
            }

            _errorCount++;
            _attemptCount++;
            Debug.Log($"[LevelController] AdjustGraph: incorrect. Errors: {_errorCount}");

            if (_levelData.MaxAttempts > 0 && _attemptCount >= _levelData.MaxAttempts)
            {
                FailLevel("max_attempts_reached");
                return;
            }

            if (_livesService != null && !_livesService.HasLives())
            {
                FailLevel("no_lives");
                return;
            }

            // Allow the player to keep adjusting; reset adjustment counter for the new attempt.
            _answerSystem.FunctionEditor?.ResetAdjustments();
            _answerSystem.ResetSelection();
            AwaitInput();
        }

        void HandleBuildFunctionAnswer(PlayerAnswer answer)
        {
            // Same shape as AdjustGraph — control-point validation handled by ValidationSystem.
            var validationResult = _validationSystem.ValidateLevel(_levelData, answer);
            bool isCorrect = validationResult.IsValid;

            if (_onAnswerConfirmed) _onAnswerConfirmed.Raise(isCorrect);

            _actionHistory.Push(new PlayerAction
            {
                ActionType = PlayerActionType.SelectAnswer,
                TargetId = _levelData.LevelId,
                PreviousState = LevelState.AwaitInput.ToString(),
                NewState = isCorrect ? LevelState.CalculateResult.ToString() : LevelState.AwaitInput.ToString()
            });

            if (isCorrect)
            {
                Debug.Log("[LevelController] BuildFunction: correct answer.");
                CalculateResult();
                return;
            }

            _errorCount++;
            _attemptCount++;
            Debug.Log($"[LevelController] BuildFunction: incorrect. Errors: {_errorCount}");

            if (_levelData.MaxAttempts > 0 && _attemptCount >= _levelData.MaxAttempts)
            {
                FailLevel("max_attempts_reached");
                return;
            }
            if (_livesService != null && !_livesService.HasLives())
            {
                FailLevel("no_lives");
                return;
            }

            _answerSystem.FunctionEditor?.ResetAdjustments();
            _answerSystem.ResetSelection();
            AwaitInput();
        }

        void HandleIdentifyErrorAnswer(PlayerAnswer answer)
        {
            var validationResult = _validationSystem.ValidateLevel(_levelData, answer);
            bool isCorrect = validationResult.IsValid;

            if (_onAnswerConfirmed) _onAnswerConfirmed.Raise(isCorrect);

            _actionHistory.Push(new PlayerAction
            {
                ActionType = PlayerActionType.SelectAnswer,
                TargetId = _levelData.LevelId,
                PreviousState = LevelState.AwaitInput.ToString(),
                NewState = isCorrect ? LevelState.CalculateResult.ToString() : LevelState.AwaitInput.ToString()
            });

            if (isCorrect)
            {
                Debug.Log($"[LevelController] IdentifyError: correct " +
                          $"({_answerSystem.SelectedStarIds.Count} stars marked).");
                CalculateResult();
                return;
            }

            _errorCount++;
            _attemptCount++;
            Debug.Log($"[LevelController] IdentifyError: incorrect. Errors: {_errorCount}");

            if (_levelData.MaxAttempts > 0 && _attemptCount >= _levelData.MaxAttempts)
            {
                FailLevel("max_attempts_reached");
                return;
            }
            if (_livesService != null && !_livesService.HasLives())
            {
                FailLevel("no_lives");
                return;
            }

            // Marks deliberately persist across retries (player adjusts, doesn't restart).
            _answerSystem.ResetSelection();
            _answerSystem.SetActive(true);
            AwaitInput();
        }

        void HandleRestoreConstellationAnswer(PlayerAnswer answer)
        {
            // Per-step: validate by distance to the next solution star's coordinate.
            if (_currentStarIndex >= _solutionStars.Count)
            {
                CalculateResult();
                return;
            }

            var target = _solutionStars[_currentStarIndex];
            float threshold = _levelData.AccuracyThreshold > 0f ? _levelData.AccuracyThreshold : 0.5f;
            float dist = Vector2.Distance(answer.SelectedCoordinate, target.Coordinate);
            bool isCorrect = dist <= threshold;

            if (_onAnswerConfirmed) _onAnswerConfirmed.Raise(isCorrect);

            _actionHistory.Push(new PlayerAction
            {
                ActionType = PlayerActionType.SelectAnswer,
                TargetId = target.StarId,
                PreviousState = StarState.Hidden.ToString(),
                NewState = isCorrect ? StarState.Restored.ToString() : StarState.Hidden.ToString()
            });

            if (isCorrect)
            {
                var entity = _starManager.GetStar(target.StarId);
                if (entity != null) entity.SetState(StarState.Restored);
                RaiseStarEvent(_onStarCollected, target, StarState.Restored);
                Debug.Log($"[LevelController] RestoreConstellation: '{target.StarId}' placed " +
                          $"(dist={dist:F2}, threshold={threshold:F2}).");
                AdvanceToNextStar(target);
                return;
            }

            _errorCount++;
            _attemptCount++;
            Debug.Log($"[LevelController] RestoreConstellation: tap missed '{target.StarId}' " +
                      $"by {dist:F2} (threshold={threshold:F2}). Errors: {_errorCount}");

            RaiseStarEvent(_onStarRejected, target, StarState.Incorrect);

            if (_levelData.MaxAttempts > 0 && _attemptCount >= _levelData.MaxAttempts)
            {
                FailLevel("max_attempts_reached");
                return;
            }
            if (_livesService != null && !_livesService.HasLives())
            {
                FailLevel("no_lives");
                return;
            }

            // Re-arm the same step. SetupRestoreConstellationStep re-attaches the tap listener.
            _answerSystem.SetActive(true);
            ShowTask();
        }

        /// <summary>
        /// Inline validation for ChooseCoordinate: check the IsCorrect flag
        /// on the selected AnswerOption.
        /// </summary>
        bool ValidateChooseCoordinate(PlayerAnswer answer)
        {
            var option = _answerSystem.GetOption(answer.SelectedOptionId);
            return option.IsCorrect;
        }

        /// <summary>
        /// Mark a star as correctly placed and advance to the next one.
        /// </summary>
        void CompleteStar(StarConfig config)
        {
            var entity = _starManager.GetStar(config.StarId);
            if (entity != null)
            {
                entity.SetState(StarState.Placed);
                StartCoroutine(PlayPlaceAndAdvance(entity, config));
            }
            else
            {
                AdvanceToNextStar(config);
            }
        }

        IEnumerator PlayPlaceAndAdvance(StarEntity entity, StarConfig config)
        {
            yield return entity.PlayPlace();

            // Raise star collected event.
            RaiseStarEvent(_onStarCollected, config, StarState.Placed);

            Debug.Log($"[LevelController] Star '{config.StarId}' placed correctly.");
            AdvanceToNextStar(config);
        }

        void AdvanceToNextStar(StarConfig config)
        {
            _currentStarIndex++;

            // Reveal more graph segments on correct action (GraphVisibilityConfig).
            if (_levelData.GraphVisibility.PartialReveal)
            {
                _visibleSegments += _levelData.GraphVisibility.RevealPerCorrectAction;
                Debug.Log($"[LevelController] Partial reveal: {_visibleSegments} segments visible");
            }

            if (_currentStarIndex >= _solutionStars.Count)
            {
                CalculateResult();
            }
            else
            {
                ShowTask();
            }
        }

        /// <summary>
        /// Handle an incorrect answer.
        /// Lives are deducted once per confirmed attempt, not per local error.
        /// </summary>
        void FailAttempt(StarConfig config)
        {
            _errorCount++;
            _attemptCount++;

            var entity = _starManager.GetStar(config.StarId);
            if (entity != null)
            {
                StartCoroutine(PlayErrorAndResume(entity, config));
            }
            else
            {
                ResumeAfterError(config);
            }
        }

        IEnumerator PlayErrorAndResume(StarEntity entity, StarConfig config)
        {
            entity.SetState(StarState.Incorrect);
            yield return entity.PlayError();
            entity.SetState(StarState.Active);

            RaiseStarEvent(_onStarRejected, config, StarState.Incorrect);

            Debug.Log($"[LevelController] Incorrect answer for star '{config.StarId}'. " +
                      $"Errors: {_errorCount}");

            ResumeAfterError(config);
        }

        void ResumeAfterError(StarConfig config)
        {
            // Check max attempts.
            if (_levelData.MaxAttempts > 0 && _attemptCount >= _levelData.MaxAttempts)
            {
                Debug.Log($"[LevelController] Max attempts ({_levelData.MaxAttempts}) reached.");
                FailLevel("max_attempts_reached");
                return;
            }

            // Check lives (server deducts on POST /check/level; client reconciles).
            if (_livesService != null && !_livesService.HasLives())
            {
                Debug.Log("[LevelController] No lives remaining after attempt.");
                FailLevel("no_lives");
                return;
            }

            // Let the player retry the same star.
            _answerSystem.ResetSelection();
            AwaitInput();
        }

        /// <summary>
        /// End the level as failed with the given reason.
        /// </summary>
        void FailLevel(string failReason)
        {
            _failReason = failReason;
            _levelTimer?.Stop();

            var localResult = _resultCalculator.Calculate(
                _levelData, _errorCount, _levelTimer?.GetElapsedTime() ?? 0f,
                null, 0, failReason);

            Debug.Log($"[LevelController] Level failed: {failReason}");

            if (_onLevelCompleted) _onLevelCompleted.Raise(localResult);
            if (_onLevelFailed) _onLevelFailed.Raise();
            SetState(LevelState.Failed);

            // Fire reconciliation in the background for the failed attempt.
            if (_reconciliation != null)
                _ = ReconcileAsync(localResult);
        }

        /// <summary>
        /// Compute level result via LevelResultCalculator, then run server reconciliation.
        /// </summary>
        void CalculateResult()
        {
            SetState(LevelState.CalculateResult);

            _levelTimer.Stop();
            var localResult = _resultCalculator.Calculate(
                _levelData, _errorCount, _levelTimer.GetElapsedTime(),
                null, 0, _failReason);

            Debug.Log($"[LevelController] Local result: {localResult.Stars} stars, {localResult.Time:F1}s, " +
                      $"{localResult.ErrorCount} errors, {localResult.FragmentsEarned} fragments" +
                      (localResult.ImprovementBonus > 0 ? $" (+{localResult.ImprovementBonus} improvement)" : ""));

            // Show local result immediately for instant feedback.
            ShowResult(localResult);

            // Fire reconciliation in the background — server result is authoritative.
            if (_reconciliation != null)
                _ = ReconcileAsync(localResult);
        }

        /// <summary>
        /// Build the last confirmed <see cref="PlayerAnswer"/> for reconciliation payload.
        /// </summary>
        PlayerAnswer BuildLastAnswer()
        {
            // AnswerSystem holds the most recently confirmed answer.
            return _answerSystem.LastConfirmedAnswer;
        }

        async Task ReconcileAsync(LevelResult localResult)
        {
            try
            {
                var answer = BuildLastAnswer();
                var authoritative = await _reconciliation.Reconcile(
                    _levelData.LevelId,
                    answer,
                    _levelTimer.GetElapsedTime(),
                    _errorCount,
                    _attemptCount,
                    localResult);

                // If the server disagrees, apply its result.
                if (authoritative != localResult)
                    ApplyServerResult(authoritative);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[LevelController] Reconciliation failed: {ex.Message}");
            }
        }

        void ApplyServerResult(LevelResult serverResult)
        {
            Debug.Log($"[LevelController] Applying server-authoritative result: " +
                      $"valid={serverResult.IsValid}, stars={serverResult.Stars}, " +
                      $"failed={serverResult.LevelFailed}");

            if (serverResult.LevelFailed && _currentState != LevelState.Failed)
            {
                _failReason = serverResult.FailReason;
                if (_onLevelFailed) _onLevelFailed.Raise();
                SetState(LevelState.Failed);
                return;
            }

            // Re-raise completed event with the corrected result so UI can update.
            if (_onLevelCompleted) _onLevelCompleted.Raise(serverResult);
        }

        void ShowResult(LevelResult result)
        {
            SetState(LevelState.ShowResult);

            if (_onLevelCompleted) _onLevelCompleted.Raise(result);

            // Actual result screen (LevelResultScreen) is Task 2.6.
            // For now, just transition to Complete.
            SetState(LevelState.Complete);

            Debug.Log("[LevelController] Level complete. Awaiting external navigation.");
        }

        void SetState(LevelState state)
        {
            _currentState = state;
        }

        void RaiseStarEvent(GameEvent<StarData> evt, StarConfig config, StarState state)
        {
            if (!evt) return;

            evt.Raise(new StarData
            {
                StarId = config.StarId,
                Coordinate = config.Coordinate,
                State = state,
                IsControlPoint = config.IsControlPoint
            });
        }
    }
}
