using StarFunc.Core;
using UnityEngine;

namespace StarFunc.Infrastructure
{
    public class BootInitializer : MonoBehaviour
    {
        void Awake()
        {
            var sfmObject = new GameObject("[SceneFlowManager]");
            DontDestroyOnLoad(sfmObject);
            var sceneFlowManager = sfmObject.AddComponent<SceneFlowManager>();
            ServiceLocator.Register<SceneFlowManager>(sceneFlowManager);
        }

        async void Start()
        {
            // §10.5 step 1 — NetworkMonitor
            var networkObject = new GameObject("[NetworkMonitor]");
            DontDestroyOnLoad(networkObject);
            var networkMonitor = networkObject.AddComponent<NetworkMonitor>();

            // §10.5 step 2 — AuthService (register / refresh token)
            var tokenManager = new TokenManager();
            var apiClient = new ApiClient(tokenManager, networkMonitor);
            var authService = new AuthService(apiClient, tokenManager, networkMonitor);

            ServiceLocator.Register(networkMonitor);
            ServiceLocator.Register(tokenManager);
            ServiceLocator.Register(apiClient);
            ServiceLocator.Register(authService);

            await authService.InitializeAsync();

            // §10.5 steps 3–11 — remaining services will be added by subsequent tasks
            var sfm = ServiceLocator.Get<SceneFlowManager>();
            sfm.LoadScene("Hub");
        }
    }
}
