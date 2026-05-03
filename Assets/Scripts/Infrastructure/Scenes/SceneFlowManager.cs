using System.Collections;
using StarFunc.Core;
using StarFunc.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarFunc.Infrastructure
{
    public class SceneFlowManager : MonoBehaviour
    {
        bool _isLevelLoaded;

        public LevelData CurrentLevel { get; private set; }

        /// <summary>
        /// Loads Level scene additively on top of Hub and hides Hub UI.
        /// </summary>
        public void LoadLevel(LevelData level)
        {
            if (_isLevelLoaded) return;

            CurrentLevel = level;
            LevelData.ActiveLevel = level;
            StartCoroutine(LoadSceneRoutine("Level", LoadSceneMode.Additive, onLoaded: () =>
            {
                SetHubUIActive(false);
                _isLevelLoaded = true;
            }));
        }

        /// <summary>
        /// Unloads the Level scene and restores Hub UI.
        /// </summary>
        public void UnloadLevel()
        {
            if (!_isLevelLoaded) return;

            StartCoroutine(UnloadLevelRoutine());
        }

        /// <summary>Reload the currently active level (Retry).</summary>
        public void RetryLevel()
        {
            if (!_isLevelLoaded || CurrentLevel == null) return;

            StartCoroutine(RetryLevelRoutine(CurrentLevel));
        }

        /// <summary>Unload the level and return to the Hub scene underneath.</summary>
        public void ReturnToHub() => UnloadLevel();

        // Sector traversal isn't wired up yet, so "Next" falls back to Hub for now.
        // Replace the body once HubScreen / SectorData navigation lands (Task 2.7).
        public void LoadNextLevel() => UnloadLevel();

        /// <summary>
        /// Full scene replacement (used for Boot → Hub).
        /// </summary>
        public void LoadScene(string sceneName)
        {
            StartCoroutine(LoadSceneRoutine(sceneName, LoadSceneMode.Single));
        }

        IEnumerator LoadSceneRoutine(string sceneName, LoadSceneMode mode,
            System.Action onLoaded = null)
        {
            var overlay = GetOverlay();
            overlay?.Show();

            var op = SceneManager.LoadSceneAsync(sceneName, mode);
            while (!op.isDone)
                yield return null;

            overlay?.Hide();
            onLoaded?.Invoke();
        }

        IEnumerator RetryLevelRoutine(LevelData level)
        {
            var overlay = GetOverlay();
            overlay?.Show();

            var unload = SceneManager.UnloadSceneAsync("Level");
            while (!unload.isDone)
                yield return null;

            _isLevelLoaded = false;
            CurrentLevel = level;
            LevelData.ActiveLevel = level;

            var load = SceneManager.LoadSceneAsync("Level", LoadSceneMode.Additive);
            while (!load.isDone)
                yield return null;

            SetHubUIActive(false);
            _isLevelLoaded = true;
            overlay?.Hide();
        }

        IEnumerator UnloadLevelRoutine()
        {
            var overlay = GetOverlay();
            overlay?.Show();

            var op = SceneManager.UnloadSceneAsync("Level");
            while (!op.isDone)
                yield return null;

            SetHubUIActive(true);
            _isLevelLoaded = false;
            CurrentLevel = null;
            LevelData.ActiveLevel = null;

            overlay?.Hide();
        }

        static ILoadingOverlay GetOverlay()
        {
            return ServiceLocator.Contains<ILoadingOverlay>()
                ? ServiceLocator.Get<ILoadingOverlay>()
                : null;
        }

        static void SetHubUIActive(bool active)
        {
            // Hub UI visibility will be managed when HubScreen is implemented.
            // For now this is a no-op placeholder.
        }
    }
}
