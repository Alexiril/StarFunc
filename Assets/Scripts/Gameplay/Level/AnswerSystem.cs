using System;
using System.Collections.Generic;
using System.Linq;
using StarFunc.Core;
using StarFunc.Data;
using UnityEngine;

namespace StarFunc.Gameplay
{
    /// <summary>
    /// Manages answer selection for the current level task.
    /// Supports ChooseCoordinate, ChooseFunction, and AdjustGraph modes.
    /// UI widgets (AnswerPanel) subscribe to <see cref="OnOptionsChanged"/>
    /// and call <see cref="SelectOption(int)"/> / <see cref="ConfirmAnswer"/>.
    /// AdjustGraph drives a <see cref="FunctionEditor"/> instead of options.
    /// </summary>
    public class AnswerSystem : MonoBehaviour
    {
        [Header("SO Events")]
        [SerializeField] GameEvent<AnswerData> _onAnswerSelected;

        [Header("Graph (ChooseFunction / AdjustGraph / BuildFunction)")]
        [SerializeField] GraphRenderer _graphRenderer;
        [SerializeField] FunctionEditor _functionEditor;

        [Header("Stars (IdentifyError / RestoreConstellation)")]
        [SerializeField] StarManager _starManager;

        AnswerOption[] _currentOptions;
        TaskType _currentTaskType;
        int _selectedIndex = -1;
        bool _isActive;
        bool _functionAnswerReady = false;

        // IdentifyError state.
        readonly HashSet<string> _selectedStarIds = new();
        StarEntity[] _identifyStars;

        // RestoreConstellation state — one target star at a time, auto-confirms on tap.
        StarConfig _restoreTarget;
        float _restoreThreshold;
        bool _restoreActive;

        /// <summary>Fired when the player confirms their selection.</summary>
        public event Action<PlayerAnswer> OnAnswerConfirmed;

        /// <summary>
        /// Fired when new answer options are set up.
        /// UI (AnswerPanel, Task 1.11) subscribes to rebuild buttons.
        /// </summary>
        public event Action<AnswerOption[], TaskType> OnOptionsChanged;

        public AnswerOption[] CurrentOptions => _currentOptions;
        public TaskType CurrentTaskType => _currentTaskType;
        public bool HasSelection => _selectedIndex >= 0 || _functionAnswerReady || _selectedStarIds.Count > 0;
        public bool IsActive => _isActive;
        public FunctionEditor FunctionEditor => _functionEditor;
        public IReadOnlyCollection<string> SelectedStarIds => _selectedStarIds;

        /// <summary>The most recently confirmed PlayerAnswer (for reconciliation).</summary>
        public PlayerAnswer LastConfirmedAnswer { get; private set; }

        /// <summary>
        /// Configure the answer system for a new task.
        /// </summary>
        public void Setup(AnswerOption[] options, TaskType taskType)
        {
            DetachModeListeners();
            _currentOptions = options;
            _currentTaskType = taskType;
            _selectedIndex = -1;
            _functionAnswerReady = false;
            _isActive = true;

            if (_functionEditor) _functionEditor.gameObject.SetActive(false);

            OnOptionsChanged?.Invoke(options, taskType);
            Debug.Log($"[AnswerSystem] Setup: {options.Length} options, taskType={taskType}");
        }

        /// <summary>
        /// Configure the answer system for an AdjustGraph task. Builds sliders on the
        /// <see cref="FunctionEditor"/> seeded at zeros, shows the reference curve as
        /// a comparison overlay, and treats the editor's current state as the answer.
        /// </summary>
        public void SetupFunctionEdit(FunctionDefinition reference, int maxAdjustments)
        {
            if (reference == null)
            {
                Debug.LogError("[AnswerSystem] SetupFunctionEdit called with null reference function.");
                return;
            }

            DetachModeListeners();
            _currentOptions = null;
            _currentTaskType = TaskType.AdjustGraph;
            _selectedIndex = -1;
            _isActive = true;

            // Player starts at zeros so the editor isn't already at the answer.
            int n = FunctionEditor.CoefficientCountFor(reference.Type);
            var initial = new float[n];

            if (_functionEditor)
            {
                _functionEditor.gameObject.SetActive(true);
                _functionEditor.OnFunctionChanged -= OnEditorFunctionChanged;
                _functionEditor.OnFunctionChanged += OnEditorFunctionChanged;
                _functionEditor.Setup(reference.Type, initial, reference.DomainRange, maxAdjustments);
            }
            else
            {
                Debug.LogWarning("[AnswerSystem] SetupFunctionEdit: _functionEditor is not assigned.");
            }

            if (_graphRenderer)
            {
                _graphRenderer.Clear();
                _graphRenderer.SetComparison(reference);
            }

            // Editor is touchable from the start; consider the answer "ready" so Confirm works.
            _functionAnswerReady = true;

            OnOptionsChanged?.Invoke(System.Array.Empty<AnswerOption>(), TaskType.AdjustGraph);
            Debug.Log($"[AnswerSystem] SetupFunctionEdit: type={reference.Type}, " +
                      $"maxAdjustments={maxAdjustments}");
        }

        /// <summary>
        /// Configure the answer system for BuildFunction. Same editor flow as
        /// AdjustGraph but no reference graph is shown — the player builds the
        /// function from scratch using control-point stars as guidance. The
        /// function type is dictated by <paramref name="type"/> (currently
        /// taken from <see cref="LevelData.ReferenceFunctions"/>[0].Type by
        /// the caller).
        /// </summary>
        public void SetupBuildFunction(FunctionType type, Vector2 domainRange, int maxAdjustments)
        {
            DetachModeListeners();
            _currentOptions = null;
            _currentTaskType = TaskType.BuildFunction;
            _selectedIndex = -1;
            _isActive = true;

            int n = FunctionEditor.CoefficientCountFor(type);
            var initial = new float[n];

            if (_functionEditor)
            {
                _functionEditor.gameObject.SetActive(true);
                _functionEditor.OnFunctionChanged -= OnEditorFunctionChanged;
                _functionEditor.OnFunctionChanged += OnEditorFunctionChanged;
                _functionEditor.Setup(type, initial, domainRange, maxAdjustments);
            }
            else
            {
                Debug.LogWarning("[AnswerSystem] SetupBuildFunction: _functionEditor is not assigned.");
            }

            if (_graphRenderer)
            {
                _graphRenderer.Clear();
                _graphRenderer.ClearComparison();
            }

            _functionAnswerReady = true;

            OnOptionsChanged?.Invoke(System.Array.Empty<AnswerOption>(), TaskType.BuildFunction);
            Debug.Log($"[AnswerSystem] SetupBuildFunction: type={type}, maxAdjustments={maxAdjustments}");
        }

        /// <summary>
        /// Configure the answer system for IdentifyError. Player taps stars to
        /// mark them as suspected distractors; tapping again toggles them off.
        /// Multiple selections allowed; <see cref="ConfirmAnswer"/> commits the set.
        /// </summary>
        public void SetupIdentifyError(StarEntity[] tappableStars)
        {
            DetachModeListeners();
            _currentOptions = null;
            _currentTaskType = TaskType.IdentifyError;
            _selectedIndex = -1;
            _functionAnswerReady = false;
            _isActive = true;

            if (_functionEditor) _functionEditor.gameObject.SetActive(false);

            _identifyStars = tappableStars ?? Array.Empty<StarEntity>();
            _selectedStarIds.Clear();

            foreach (var star in _identifyStars)
            {
                if (star == null) continue;
                star.OnTapped += HandleIdentifyStarTapped;
            }

            OnOptionsChanged?.Invoke(System.Array.Empty<AnswerOption>(), TaskType.IdentifyError);
            Debug.Log($"[AnswerSystem] SetupIdentifyError: {_identifyStars.Length} tappable stars");
        }

        /// <summary>
        /// Configure the answer system for one step of RestoreConstellation.
        /// Listens for plane taps; when a tap lands within
        /// <paramref name="threshold"/> of <paramref name="target"/>'s coordinate,
        /// auto-confirms the answer with <see cref="LastConfirmedAnswer"/> set
        /// to a coordinate-typed PlayerAnswer.
        /// </summary>
        public void SetupRestoreConstellationStep(StarConfig target, float threshold)
        {
            DetachModeListeners();
            _currentOptions = null;
            _currentTaskType = TaskType.RestoreConstellation;
            _selectedIndex = -1;
            _functionAnswerReady = false;
            _isActive = true;

            if (_functionEditor) _functionEditor.gameObject.SetActive(false);

            _restoreTarget = target;
            _restoreThreshold = threshold;
            _restoreActive = true;

            if (_starManager)
            {
                _starManager.OnPlaneTapped -= HandleRestorePlaneTapped;
                _starManager.OnPlaneTapped += HandleRestorePlaneTapped;
            }
            else
            {
                Debug.LogWarning("[AnswerSystem] SetupRestoreConstellationStep: _starManager not assigned.");
            }

            OnOptionsChanged?.Invoke(System.Array.Empty<AnswerOption>(), TaskType.RestoreConstellation);
            Debug.Log($"[AnswerSystem] SetupRestoreConstellationStep: target={target.StarId} " +
                      $"at ({target.Coordinate.x:F2}, {target.Coordinate.y:F2}), threshold={threshold}");
        }

        void HandleIdentifyStarTapped(StarEntity star)
        {
            if (!_isActive || _currentTaskType != TaskType.IdentifyError || star == null) return;

            string id = star.StarId;
            if (_selectedStarIds.Remove(id))
            {
                star.SetState(StarState.Active);
                Debug.Log($"[AnswerSystem] IdentifyError: unmarked '{id}'");
            }
            else
            {
                _selectedStarIds.Add(id);
                star.SetState(StarState.Placed);
                Debug.Log($"[AnswerSystem] IdentifyError: marked '{id}'");
            }
        }

        void HandleRestorePlaneTapped(Vector2 planeCoord)
        {
            if (!_isActive || !_restoreActive || _currentTaskType != TaskType.RestoreConstellation)
                return;

            // Auto-confirm: each plane tap is a final answer for this step.
            _restoreActive = false;

            var answer = new PlayerAnswer
            {
                TaskType = TaskType.RestoreConstellation,
                AnswerType = AnswerType.PlaceStars,
                SelectedCoordinate = planeCoord,
                Placements = new List<StarPlacement>
                {
                    new() { StarId = _restoreTarget.StarId, Coordinate = planeCoord }
                }
            };

            _isActive = false;
            LastConfirmedAnswer = answer;

            Debug.Log($"[AnswerSystem] RestoreConstellation tap at " +
                      $"({planeCoord.x:F2}, {planeCoord.y:F2}) for target '{_restoreTarget.StarId}'");
            OnAnswerConfirmed?.Invoke(answer);
        }

        void DetachModeListeners()
        {
            if (_identifyStars != null)
            {
                foreach (var star in _identifyStars)
                    if (star != null) star.OnTapped -= HandleIdentifyStarTapped;
                _identifyStars = null;
            }
            _selectedStarIds.Clear();

            if (_starManager) _starManager.OnPlaneTapped -= HandleRestorePlaneTapped;
            _restoreActive = false;
        }

        void OnEditorFunctionChanged(FunctionParams paramsArg)
        {
            _ = paramsArg;
            _functionAnswerReady = true;
        }

        /// <summary>Select an option by array index.</summary>
        public void SelectOption(int index)
        {
            if (!_isActive) return;
            if (_currentOptions == null || index < 0 || index >= _currentOptions.Length) return;

            _selectedIndex = index;
            var option = _currentOptions[index];

            // ChooseFunction: draw the selected function on the graph for preview.
            if (_currentTaskType == TaskType.ChooseFunction && _graphRenderer && option.Function)
            {
                _graphRenderer.Clear();
                _graphRenderer.DrawFunction(option.Function);
            }

            if (_onAnswerSelected)
            {
                _onAnswerSelected.Raise(new AnswerData
                {
                    OptionId = option.OptionId,
                    DisplayText = option.Text,
                    Value = option.Value,
                    IsCorrect = option.IsCorrect
                });
            }

            Debug.Log($"[AnswerSystem] Selected option {index}: \"{option.Text}\" (id={option.OptionId})");
        }

        /// <summary>Select an option by its OptionId.</summary>
        public void SelectOption(string optionId)
        {
            if (_currentOptions == null) return;

            for (int i = 0; i < _currentOptions.Length; i++)
            {
                if (_currentOptions[i].OptionId == optionId)
                {
                    SelectOption(i);
                    return;
                }
            }

            Debug.LogWarning($"[AnswerSystem] Option with id '{optionId}' not found.");
        }

        /// <summary>Build a PlayerAnswer from the current selection.</summary>
        public PlayerAnswer GetCurrentAnswer()
        {
            // AdjustGraph / BuildFunction read from the FunctionEditor instead of the options array.
            if (_currentTaskType == TaskType.AdjustGraph || _currentTaskType == TaskType.BuildFunction)
            {
                if (_functionEditor == null) return null;
                var p = _functionEditor.GetCurrentParams();
                return new PlayerAnswer
                {
                    TaskType = _currentTaskType,
                    AnswerType = AnswerType.Function,
                    FunctionType = p.Type,
                    Coefficients = p.Coefficients
                };
            }

            // IdentifyError reads the multi-select set.
            if (_currentTaskType == TaskType.IdentifyError)
            {
                return new PlayerAnswer
                {
                    TaskType = TaskType.IdentifyError,
                    AnswerType = AnswerType.IdentifyStars,
                    SelectedStarIds = _selectedStarIds.ToList()
                };
            }

            // RestoreConstellation auto-confirms per tap; LastConfirmedAnswer holds the result.
            if (_currentTaskType == TaskType.RestoreConstellation)
                return LastConfirmedAnswer;

            if (_selectedIndex < 0 || _currentOptions == null)
                return null;

            var option = _currentOptions[_selectedIndex];

            var answer = new PlayerAnswer
            {
                TaskType = _currentTaskType,
                SelectedOptionId = option.OptionId
            };

            switch (_currentTaskType)
            {
                case TaskType.ChooseFunction:
                    answer.AnswerType = AnswerType.ChooseOption;
                    if (option.Function)
                    {
                        answer.FunctionType = option.Function.Type;
                        answer.Coefficients = option.Function.Coefficients;
                    }
                    break;

                default:
                    answer.AnswerType = AnswerType.ChooseOption;
                    answer.SelectedCoordinate = new Vector2(option.Value, 0f);
                    break;
            }

            return answer;
        }

        /// <summary>Confirm the current selection and notify LevelController.</summary>
        public void ConfirmAnswer()
        {
            if (!_isActive) return;
            if (!HasSelection)
            {
                Debug.LogWarning("[AnswerSystem] Cannot confirm — no option selected and no function answer ready.");
                return;
            }

            var answer = GetCurrentAnswer();
            if (answer == null)
            {
                Debug.LogWarning("[AnswerSystem] Cannot confirm — GetCurrentAnswer returned null.");
                return;
            }

            _isActive = false;
            LastConfirmedAnswer = answer;

            string identity;
            if (_currentTaskType == TaskType.AdjustGraph)
            {
                string coeffStr = answer.Coefficients == null ? "" : string.Join(",", answer.Coefficients);
                identity = $"AdjustGraph type={answer.FunctionType}, coeffs=[{coeffStr}]";
            }
            else
            {
                identity = $"optionId={answer.SelectedOptionId}";
            }
            Debug.Log($"[AnswerSystem] Answer confirmed: {identity}");

            OnAnswerConfirmed?.Invoke(answer);
        }

        /// <summary>Clear the current selection.</summary>
        public void ResetSelection()
        {
            _selectedIndex = -1;
            bool functionMode = _currentTaskType == TaskType.AdjustGraph || _currentTaskType == TaskType.BuildFunction;
            _functionAnswerReady = functionMode && _functionEditor != null;
            if (_currentTaskType == TaskType.RestoreConstellation)
                _restoreActive = true;
            // IdentifyError selections are deliberately preserved across retries
            // so the player can adjust their guesses without starting from scratch.
        }

        /// <summary>Enable or disable interaction.</summary>
        public void SetActive(bool active)
        {
            _isActive = active;
            if (_functionEditor && _currentTaskType == TaskType.AdjustGraph)
                _functionEditor.SetActive(active);
        }

        void OnDestroy()
        {
            if (_functionEditor)
                _functionEditor.OnFunctionChanged -= OnEditorFunctionChanged;
            DetachModeListeners();
        }

        /// <summary>
        /// Find the AnswerOption that matches a given optionId.
        /// Returns the option or default if not found.
        /// </summary>
        public AnswerOption GetOption(string optionId)
        {
            if (_currentOptions == null)
                return default;

            return _currentOptions.FirstOrDefault(o => o.OptionId == optionId);
        }
    }
}
