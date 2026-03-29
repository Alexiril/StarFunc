using System.Collections;
using System.Collections.Generic;
using System.Linq;
using StarFunc.Core;
using StarFunc.Data;
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

            if (_currentStarIndex >= _solutionStars.Count)
            {
                // All stars solved — calculate result.
                CalculateResult();
                return;
            }

            var targetStar = _solutionStars[_currentStarIndex];

            // Set up answer system with the level's options for ChooseCoordinate.
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

            var result = _resultCalculator.Calculate(
                _levelData, _errorCount, _levelTimer?.GetElapsedTime() ?? 0f,
                null, 0, failReason);
            _ = result; // Will be forwarded to fail screen (Task 2.6).

            Debug.Log($"[LevelController] Level failed: {failReason}");

            if (_onLevelFailed) _onLevelFailed.Raise();
            SetState(LevelState.Failed);
        }

        /// <summary>
        /// Compute level result via LevelResultCalculator.
        /// </summary>
        void CalculateResult()
        {
            SetState(LevelState.CalculateResult);

            _levelTimer.Stop();
            var result = _resultCalculator.Calculate(
                _levelData, _errorCount, _levelTimer.GetElapsedTime(),
                null, 0, _failReason);

            Debug.Log($"[LevelController] Result: {result.Stars} stars, {result.Time:F1}s, " +
                      $"{result.ErrorCount} errors, {result.FragmentsEarned} fragments" +
                      (result.ImprovementBonus > 0 ? $" (+{result.ImprovementBonus} improvement)" : ""));

            ShowResult(result);
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
