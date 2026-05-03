using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using StarFunc.Data;

namespace StarFunc.Gameplay
{
    public class StarManager : MonoBehaviour
    {
        [SerializeField] StarEntity _starPrefab;
        [SerializeField] CoordinatePlane _plane;
        [SerializeField] Camera _camera;

        readonly Dictionary<string, StarEntity> _stars = new();

        /// <summary>
        /// Fires when any managed star is tapped.
        /// </summary>
        public event Action<StarEntity> OnStarTapped;

        /// <summary>
        /// Fires when the player taps the plane and the tap doesn't land on any
        /// interactable star. Coordinate is in plane-space.
        /// Used by RestoreConstellation to capture placement attempts.
        /// </summary>
        public event Action<Vector2> OnPlaneTapped;

        void Update()
        {
            if (_stars.Count == 0) return;

            bool pressed = false;
            Vector2 screenPos = default;

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                pressed = true;
                screenPos = mouse.position.ReadValue();
            }

            if (!pressed)
            {
                var touch = Touchscreen.current;
                if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                {
                    pressed = true;
                    screenPos = touch.primaryTouch.position.ReadValue();
                }
            }

            if (!pressed) return;

            var cam = _camera ? _camera : Camera.main;
            if (!cam) return;

            Vector3 worldPos3 = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            Vector2 worldPos = new(worldPos3.x, worldPos3.y);

            var hits = Physics2D.OverlapPointAll(worldPos);
            foreach (var hit in hits)
            {
                var interaction = hit.GetComponent<StarInteraction>();
                if (interaction)
                {
                    interaction.RaiseTapped();
                    return;
                }
            }

            // No star hit — surface as a plane-level tap for modes like RestoreConstellation.
            if (OnPlaneTapped != null && _plane != null)
            {
                Vector2 planePos = _plane.WorldToPlane(worldPos);
                OnPlaneTapped.Invoke(planePos);
            }
        }

        public void SpawnStars(StarConfig[] configs)
        {
            ClearAll();

            foreach (var config in configs)
            {
                var worldPos = _plane.PlaneToWorld(config.Coordinate);
                var instance = Instantiate(_starPrefab, worldPos, Quaternion.identity, transform);
                instance.Initialize(config);
                instance.OnTapped += HandleStarTapped;
                _stars[config.StarId] = instance;
            }
        }

        public StarEntity GetStar(string starId)
        {
            _stars.TryGetValue(starId, out var star);
            return star;
        }

        /// <summary>Enumerate every spawned star. Used by IdentifyError to wire taps.</summary>
        public IEnumerable<StarEntity> GetAllStars() => _stars.Values;

        public List<StarEntity> GetAllPlaced()
        {
            return _stars.Values
                .Where(s => s.CurrentState == StarState.Placed)
                .ToList();
        }

        public void ResetAll()
        {
            foreach (var star in _stars.Values)
                star.SetState(star.Config.InitialState);
        }

        /// <summary>
        /// Set all stars to Hidden state (used by Memory Mode after preview expires).
        /// </summary>
        public void HideAll()
        {
            foreach (var star in _stars.Values)
                star.SetState(StarState.Hidden);
        }

        /// <summary>
        /// Set all stars to their initial states (used by Memory Mode to show reference).
        /// </summary>
        public void ShowAll()
        {
            foreach (var star in _stars.Values)
                star.SetState(star.Config.InitialState);
        }

        public void ClearAll()
        {
            foreach (var star in _stars.Values)
            {
                star.OnTapped -= HandleStarTapped;
                Destroy(star.gameObject);
            }
            _stars.Clear();
        }

        void HandleStarTapped(StarEntity star)
        {
            Debug.Log($"[StarManager] Star tapped: {star.StarId} at ({star.GetCoordinate().x:F1}, {star.GetCoordinate().y:F1}), state={star.CurrentState}");
            OnStarTapped?.Invoke(star);
        }

        void OnDestroy()
        {
            foreach (var star in _stars.Values)
                star.OnTapped -= HandleStarTapped;
        }
    }
}
