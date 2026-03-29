using System;
using System.Linq;
using StarFunc.Core;
using StarFunc.Data;
using UnityEngine;

namespace StarFunc.Gameplay
{
    /// <summary>
    /// Manages answer selection for the current level task.
    /// Phase 1: only ChooseCoordinate mode is implemented.
    /// UI widgets (AnswerPanel) subscribe to <see cref="OnOptionsChanged"/>
    /// and call <see cref="SelectOption(int)"/> / <see cref="ConfirmAnswer"/>.
    /// </summary>
    public class AnswerSystem : MonoBehaviour
    {
        [Header("SO Events")]
        [SerializeField] GameEvent<AnswerData> _onAnswerSelected;

        AnswerOption[] _currentOptions;
        TaskType _currentTaskType;
        int _selectedIndex = -1;
        bool _isActive;

        /// <summary>Fired when the player confirms their selection.</summary>
        public event Action<PlayerAnswer> OnAnswerConfirmed;

        /// <summary>
        /// Fired when new answer options are set up.
        /// UI (AnswerPanel, Task 1.11) subscribes to rebuild buttons.
        /// </summary>
        public event Action<AnswerOption[], TaskType> OnOptionsChanged;

        public AnswerOption[] CurrentOptions => _currentOptions;
        public TaskType CurrentTaskType => _currentTaskType;
        public bool HasSelection => _selectedIndex >= 0;
        public bool IsActive => _isActive;

        /// <summary>
        /// Configure the answer system for a new task.
        /// </summary>
        public void Setup(AnswerOption[] options, TaskType taskType)
        {
            _currentOptions = options;
            _currentTaskType = taskType;
            _selectedIndex = -1;
            _isActive = true;

            OnOptionsChanged?.Invoke(options, taskType);
            Debug.Log($"[AnswerSystem] Setup: {options.Length} options, taskType={taskType}");
        }

        /// <summary>Select an option by array index.</summary>
        public void SelectOption(int index)
        {
            if (!_isActive) return;
            if (_currentOptions == null || index < 0 || index >= _currentOptions.Length) return;

            _selectedIndex = index;
            var option = _currentOptions[index];

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
            if (_selectedIndex < 0 || _currentOptions == null)
                return null;

            var option = _currentOptions[_selectedIndex];

            return new PlayerAnswer
            {
                TaskType = _currentTaskType,
                AnswerType = AnswerType.ChooseOption,
                SelectedOptionId = option.OptionId,
                SelectedCoordinate = new Vector2(option.Value, 0f)
            };
        }

        /// <summary>Confirm the current selection and notify LevelController.</summary>
        public void ConfirmAnswer()
        {
            if (!_isActive) return;
            if (_selectedIndex < 0)
            {
                Debug.LogWarning("[AnswerSystem] Cannot confirm — no option selected.");
                return;
            }

            var answer = GetCurrentAnswer();
            _isActive = false;

            Debug.Log($"[AnswerSystem] Answer confirmed: optionId={answer.SelectedOptionId}");
            OnAnswerConfirmed?.Invoke(answer);
        }

        /// <summary>Clear the current selection.</summary>
        public void ResetSelection()
        {
            _selectedIndex = -1;
        }

        /// <summary>Enable or disable interaction.</summary>
        public void SetActive(bool active)
        {
            _isActive = active;
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
