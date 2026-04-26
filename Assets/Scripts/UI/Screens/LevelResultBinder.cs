using StarFunc.Core;
using StarFunc.Data;
using UnityEngine;

namespace StarFunc.UI
{
    public class LevelResultBinder : MonoBehaviour
    {
        [SerializeField] private UIService _uiService;
        [SerializeField] private LevelResultEvent _onLevelCompleted;
        [SerializeField] private GameEvent _onLevelFailed;
        [SerializeField] private LevelResultScreen _screen;

        private void OnEnable()
        {
            _onLevelCompleted.AddListener(HandleCompleted);
            _onLevelFailed.AddListener(HandleFailed);
        }

        private void HandleCompleted(LevelResult result)
        {
            _screen.Setup(result);
            _uiService.ShowScreen<LevelResultScreen>();
        }

        private void HandleFailed()
        {
        }
    }
}
