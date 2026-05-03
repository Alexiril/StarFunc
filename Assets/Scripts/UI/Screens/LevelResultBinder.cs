using StarFunc.Core;
using StarFunc.Data;
using StarFunc.Infrastructure;
using UnityEngine;

namespace StarFunc.UI
{
    public class LevelResultBinder : MonoBehaviour
    {
        [SerializeField] private UIService _uiService;
        [SerializeField] private LevelResultEvent _onLevelCompleted;
        [SerializeField] private GameEvent _onLevelFailed;
        [SerializeField] private LevelResultScreen _screen;

        private SceneFlowManager _sceneFlowManager;

        private void OnEnable()
        {
            _sceneFlowManager = ServiceLocator.Contains<SceneFlowManager>()
                ? ServiceLocator.Get<SceneFlowManager>()
                : null;

            if (_sceneFlowManager == null)
                Debug.LogWarning("[LevelResultBinder] SceneFlowManager not registered — Next/Retry/Hub buttons will be no-ops. Did you boot from Boot.unity?");

            if (_onLevelCompleted) _onLevelCompleted.AddListener(HandleCompleted);
            if (_onLevelFailed) _onLevelFailed.AddListener(HandleFailed);

            if (_screen)
            {
                _screen.OnNextClicked += HandleNext;
                _screen.OnRetryClicked += HandleRetry;
                _screen.OnHubClicked += HandleHub;
            }
        }

        private void OnDisable()
        {
            if (_onLevelCompleted) _onLevelCompleted.RemoveListener(HandleCompleted);
            if (_onLevelFailed) _onLevelFailed.RemoveListener(HandleFailed);

            if (_screen)
            {
                _screen.OnNextClicked -= HandleNext;
                _screen.OnRetryClicked -= HandleRetry;
                _screen.OnHubClicked -= HandleHub;
            }
        }

        private void HandleCompleted(LevelResult result)
        {
            if (!_screen || !_uiService) return;

            _screen.Setup(result);
            _uiService.ShowScreen<LevelResultScreen>();
        }

        private void HandleFailed()
        {
            // LevelController also raises OnLevelCompleted with LevelFailed=true on failure,
            // so the screen is shown via HandleCompleted. This hook stays for audio/analytics.
        }

        private void HandleNext()
        {
            if (_sceneFlowManager) _sceneFlowManager.LoadNextLevel();
        }

        private void HandleRetry()
        {
            if (_sceneFlowManager) _sceneFlowManager.RetryLevel();
        }

        private void HandleHub()
        {
            if (_sceneFlowManager) _sceneFlowManager.ReturnToHub();
        }
    }
}
