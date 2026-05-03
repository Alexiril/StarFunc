using System;
using System.Collections.Generic;
using StarFunc.Data;
using UnityEngine;

namespace StarFunc.Gameplay
{
    /// <summary>
    /// Live editor for function coefficients. Drives sliders, raises
    /// <c>OnFunctionChanged</c> on every change, and enforces the
    /// per-attempt <c>MaxAdjustments</c> limit from <see cref="LevelData"/>.
    /// </summary>
    public class FunctionEditor : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] RectTransform _slidersContainer;
        [SerializeField] FunctionEditorSliderRow _sliderRowPrefab;

        [Header("Slider Range")]
        [SerializeField] float _defaultMin = -5f;
        [SerializeField] float _defaultMax = 5f;

        [Header("Events")]
        [SerializeField] FunctionParamsEvent _onFunctionChanged;

        FunctionType _currentType;
        float[] _coefficients = Array.Empty<float>();
        Vector2 _domainRange = new(-5f, 5f);
        int _maxAdjustments;
        int _adjustmentCount;
        readonly List<FunctionEditorSliderRow> _rows = new();

        /// <summary>Fires after every coefficient mutation (slider drag, drag-point, programmatic).</summary>
        public event Action<FunctionParams> OnFunctionChanged;

        public FunctionType CurrentType => _currentType;
        public int AdjustmentCount => _adjustmentCount;
        public int MaxAdjustments => _maxAdjustments;
        public bool IsExhausted => _maxAdjustments > 0 && _adjustmentCount >= _maxAdjustments;
        public bool IsActive { get; private set; }

        /// <summary>
        /// Configure the editor for a new task. Builds slider rows for the
        /// chosen function type and seeds them with <paramref name="initialCoefficients"/>.
        /// </summary>
        public void Setup(FunctionType type, float[] initialCoefficients,
                          Vector2 domainRange, int maxAdjustments)
        {
            int expected = CoefficientCountFor(type);
            _currentType = type;
            _coefficients = new float[expected];
            if (initialCoefficients != null)
                Array.Copy(initialCoefficients, _coefficients,
                           Mathf.Min(initialCoefficients.Length, expected));
            _domainRange = domainRange;
            _maxAdjustments = maxAdjustments;
            _adjustmentCount = 0;
            IsActive = true;

            BuildSliders();
            RaiseChanged();
        }

        /// <summary>Reset the adjustment counter (called on Undo / Reset / new attempt).</summary>
        public void ResetAdjustments()
        {
            _adjustmentCount = 0;
            foreach (var row in _rows)
                if (row) row.SetInteractable(true);
        }

        /// <summary>Disable/enable slider interaction without touching the counter.</summary>
        public void SetActive(bool active)
        {
            IsActive = active;
            foreach (var row in _rows)
                if (row) row.SetInteractable(active && !IsExhausted);
        }

        public FunctionParams GetCurrentParams()
        {
            return new FunctionParams
            {
                Type = _currentType,
                Coefficients = (float[])_coefficients.Clone()
            };
        }

        /// <summary>Allocates a transient FunctionDefinition for one-shot use (e.g. preview rendering).</summary>
        public FunctionDefinition GetCurrentFunction()
        {
            var fd = ScriptableObject.CreateInstance<FunctionDefinition>();
            fd.Type = _currentType;
            fd.Coefficients = (float[])_coefficients.Clone();
            fd.DomainRange = _domainRange;
            return fd;
        }

        /// <summary>
        /// Programmatic coefficient set (e.g. from drag-point handler). Counts as an adjustment.
        /// </summary>
        public void SetCoefficient(int index, float value)
        {
            if (!IsActive || IsExhausted) return;
            if (index < 0 || index >= _coefficients.Length) return;

            _coefficients[index] = value;
            if (index < _rows.Count && _rows[index])
                _rows[index].SetValueWithoutNotify(value);

            RegisterAdjustment();
        }

        /// <summary>
        /// Set every coefficient at once (e.g. when a drag of a control point
        /// solves for all coefficients simultaneously). Counts as ONE adjustment.
        /// </summary>
        public void SetCoefficients(float[] values)
        {
            if (!IsActive || IsExhausted || values == null) return;

            int n = Mathf.Min(values.Length, _coefficients.Length);
            for (int i = 0; i < n; i++)
            {
                _coefficients[i] = values[i];
                if (i < _rows.Count && _rows[i])
                    _rows[i].SetValueWithoutNotify(values[i]);
            }

            RegisterAdjustment();
        }

        void BuildSliders()
        {
            // Tear down old rows.
            foreach (var row in _rows)
                if (row) Destroy(row.gameObject);
            _rows.Clear();

            if (_slidersContainer == null || _sliderRowPrefab == null)
            {
                Debug.LogWarning("[FunctionEditor] Missing _slidersContainer or _sliderRowPrefab; sliders not built.");
                return;
            }

            string[] labels = LabelsFor(_currentType);
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                var row = Instantiate(_sliderRowPrefab, _slidersContainer);
                row.gameObject.SetActive(true);
                row.Initialize(labels[i], _defaultMin, _defaultMax, _coefficients[i]);
                row.OnValueChanged += value => HandleSliderChanged(captured, value);
                _rows.Add(row);
            }
        }

        void HandleSliderChanged(int index, float value)
        {
            if (!IsActive || IsExhausted) return;
            _coefficients[index] = value;
            RegisterAdjustment();
        }

        void RegisterAdjustment()
        {
            _adjustmentCount++;
            RaiseChanged();

            if (IsExhausted)
            {
                foreach (var row in _rows)
                    if (row) row.SetInteractable(false);
                Debug.Log($"[FunctionEditor] MaxAdjustments ({_maxAdjustments}) reached — sliders locked.");
            }
        }

        void RaiseChanged()
        {
            var p = GetCurrentParams();
            OnFunctionChanged?.Invoke(p);
            if (_onFunctionChanged) _onFunctionChanged.Raise(p);
        }

        /// <summary>Coefficient labels per function type. Extended in 3.2.</summary>
        static string[] LabelsFor(FunctionType type) => type switch
        {
            FunctionType.Linear => new[] { "k", "b" },
            FunctionType.Quadratic => new[] { "a", "h", "k" },          // 3.2
            FunctionType.Sinusoidal => new[] { "a", "b", "c", "d" },    // 3.2
            _ => Array.Empty<string>()
        };

        /// <summary>Coefficient count per function type. Mirrors LabelsFor.</summary>
        public static int CoefficientCountFor(FunctionType type) => LabelsFor(type).Length;
    }
}
