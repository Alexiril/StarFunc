using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarFunc.Gameplay
{
    /// <summary>
    /// One slider row inside <see cref="FunctionEditor"/> — label + slider + numeric readout.
    /// </summary>
    public class FunctionEditorSliderRow : MonoBehaviour
    {
        [SerializeField] TMP_Text _label;
        [SerializeField] Slider _slider;
        [SerializeField] TMP_Text _valueText;
        [SerializeField] string _valueFormat = "0.00";

        public event Action<float> OnValueChanged;

        public float Value => _slider ? _slider.value : 0f;

        void Awake()
        {
            if (_slider) _slider.onValueChanged.AddListener(HandleSliderChanged);
        }

        void OnDestroy()
        {
            if (_slider) _slider.onValueChanged.RemoveListener(HandleSliderChanged);
        }

        public void Initialize(string label, float min, float max, float initial)
        {
            if (_label) _label.text = label;
            if (_slider)
            {
                _slider.minValue = min;
                _slider.maxValue = max;
                _slider.SetValueWithoutNotify(initial);
            }
            UpdateReadout(initial);
        }

        public void SetValueWithoutNotify(float value)
        {
            if (_slider) _slider.SetValueWithoutNotify(value);
            UpdateReadout(value);
        }

        public void SetInteractable(bool interactable)
        {
            if (_slider) _slider.interactable = interactable;
        }

        void HandleSliderChanged(float value)
        {
            UpdateReadout(value);
            OnValueChanged?.Invoke(value);
        }

        void UpdateReadout(float value)
        {
            if (_valueText) _valueText.text = value.ToString(_valueFormat);
        }
    }
}
