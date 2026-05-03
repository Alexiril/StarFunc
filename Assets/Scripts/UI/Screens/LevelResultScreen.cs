using StarFunc.Data;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarFunc.UI
{
    public class LevelResultScreen : UIScreen
    {
        [Header("Widgets")]
        [SerializeField] StarRatingDisplay _starRating;
        [SerializeField] FragmentsDisplay _fragmentsDisplay;

        [Header("Labels")]
        [SerializeField] TMP_Text _timeText;
        [SerializeField] TMP_Text _titleText;

        [Header("Constellation Preview")]
        [SerializeField] Image _constellationPreview;

        [Header("Buttons")]
        [SerializeField] Button _nextButton;
        [SerializeField] Button _retryButton;
        [SerializeField] Button _hubButton;

        /// <summary>Raised when the player taps "Next Level".</summary>
        public event Action OnNextClicked;

        /// <summary>Raised when the player taps "Retry".</summary>
        public event Action OnRetryClicked;

        /// <summary>Raised when the player taps "To Hub".</summary>
        public event Action OnHubClicked;

        void Awake()
        {
            if (_nextButton)
                _nextButton.onClick.AddListener(() => OnNextClicked?.Invoke());

            if (_retryButton)
                _retryButton.onClick.AddListener(() => OnRetryClicked?.Invoke());

            if (_hubButton)
                _hubButton.onClick.AddListener(() => OnHubClicked?.Invoke());
        }

        public void Setup(LevelResult result)
        {
            if (result == null) return;

            if (_starRating)
                _starRating.SetStars(result.Stars, animate: true);

            if (_fragmentsDisplay)
                _fragmentsDisplay.SetFragments(result.FragmentsEarned);

            if (_timeText)
            {
                int minutes = Mathf.FloorToInt(result.Time / 60f);
                int seconds = Mathf.FloorToInt(result.Time % 60f);
                _timeText.text = $"{minutes:00}:{seconds:00}";
            }

            if (_titleText)
                _titleText.text = result.LevelFailed ? "Уровень не пройден" : "Уровень пройден!";

            // Disable "Next" when the level was failed
            if (_nextButton)
                _nextButton.interactable = !result.LevelFailed;
        }

        public void SetConstellationPreview(Sprite preview)
        {
            if (_constellationPreview && preview)
            {
                _constellationPreview.sprite = preview;
                _constellationPreview.gameObject.SetActive(true);
            }
        }
    }
}
