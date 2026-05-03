using System.Collections;
using UnityEngine;
using StarFunc.Core;
using StarFunc.Data;

namespace StarFunc.Gameplay
{
    public class GhostEmotionController : MonoBehaviour
    {
        [SerializeField] GhostEntity _ghostEntity;

        [Header("SO Events")]
        [SerializeField] StarDataEvent _onStarCollected;
        [SerializeField] StarDataEvent _onStarRejected;
        [SerializeField] LevelResultEvent _onLevelCompleted;

        [Header("Settings")]
        [SerializeField] float _emotionDuration = 3f;

        Coroutine _revertCoroutine;

        void OnEnable()
        {
            if (_onStarCollected) _onStarCollected.AddListener(OnStarCollected);
            if (_onStarRejected) _onStarRejected.AddListener(OnStarRejected);
            if (_onLevelCompleted) _onLevelCompleted.AddListener(OnLevelCompleted);
        }

        void OnDisable()
        {
            if (_onStarCollected) _onStarCollected.RemoveListener(OnStarCollected);
            if (_onStarRejected) _onStarRejected.RemoveListener(OnStarRejected);
            if (_onLevelCompleted) _onLevelCompleted.RemoveListener(OnLevelCompleted);
        }

        void OnStarCollected(StarData _) => ApplyEmotion(GhostEmotion.Happy);
        void OnStarRejected(StarData _) => ApplyEmotion(GhostEmotion.Sad);
        void OnLevelCompleted(LevelResult _) => ApplyEmotion(GhostEmotion.Excited);

        void ApplyEmotion(GhostEmotion emotion)
        {
            if (_revertCoroutine != null)
                StopCoroutine(_revertCoroutine);

            _ghostEntity.SetEmotion(emotion);
            _revertCoroutine = StartCoroutine(RevertToIdleRoutine());
        }

        IEnumerator RevertToIdleRoutine()
        {
            yield return new WaitForSeconds(_emotionDuration);
            _ghostEntity.SetEmotion(GhostEmotion.Idle);
            _revertCoroutine = null;
        }
    }
}
