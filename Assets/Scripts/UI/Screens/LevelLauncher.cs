using StarFunc.Core;
using StarFunc.Data;
using StarFunc.Infrastructure;
using UnityEngine;

namespace StarFunc.UI
{
    public class LevelLauncher : MonoBehaviour
    {
        [SerializeField] LevelDataEvent _onLevelSelected;

        SceneFlowManager _sceneFlowManager;

        void OnEnable()
        {
            _sceneFlowManager = ServiceLocator.Contains<SceneFlowManager>()
                ? ServiceLocator.Get<SceneFlowManager>()
                : null;

            if (_sceneFlowManager == null)
                Debug.LogWarning("[LevelLauncher] SceneFlowManager not registered — level select will be a no-op. Did you boot from Boot.unity?");

            if (_onLevelSelected) _onLevelSelected.AddListener(HandleLevelSelected);
        }

        void OnDisable()
        {
            if (_onLevelSelected) _onLevelSelected.RemoveListener(HandleLevelSelected);
        }

        void HandleLevelSelected(LevelData level)
        {
            if (level == null) return;
            if (_sceneFlowManager) _sceneFlowManager.LoadLevel(level);
        }
    }
}
