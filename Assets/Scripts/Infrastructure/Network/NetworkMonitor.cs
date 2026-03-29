using System;
using UnityEngine;

namespace StarFunc.Infrastructure
{
    /// <summary>
    /// Tracks network connectivity via Application.internetReachability.
    /// Periodic polling + immediate checks.
    /// </summary>
    public class NetworkMonitor : MonoBehaviour
    {
        const float PollIntervalSeconds = 5f;

        bool _isOnline;
        float _pollTimer;

        public bool IsOnline => _isOnline;

        /// <summary>
        /// Fired when connectivity state changes. Argument: true = online, false = offline.
        /// </summary>
        public event Action<bool> OnConnectivityChanged;

        void Awake()
        {
            _isOnline = CheckReachability();
        }

        void Update()
        {
            _pollTimer += Time.unscaledDeltaTime;
            if (_pollTimer < PollIntervalSeconds)
                return;

            _pollTimer = 0f;
            UpdateConnectivity();
        }

        /// <summary>
        /// Force an immediate connectivity check (e.g. after a failed request).
        /// </summary>
        public void ForceCheck()
        {
            UpdateConnectivity();
        }

        /// <summary>
        /// Externally signal that the network is down (e.g. after repeated request failures).
        /// </summary>
        public void SetOffline()
        {
            if (!_isOnline) return;
            _isOnline = false;
            OnConnectivityChanged?.Invoke(false);
        }

        void UpdateConnectivity()
        {
            bool reachable = CheckReachability();
            if (reachable == _isOnline) return;

            _isOnline = reachable;
            OnConnectivityChanged?.Invoke(_isOnline);
        }

        static bool CheckReachability()
        {
            return Application.internetReachability != NetworkReachability.NotReachable;
        }
    }
}
