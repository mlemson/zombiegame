using Unity.Netcode;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieTown.Multiplayer
{
    public sealed class NetworkSessionBootstrap : MonoBehaviour
    {
        public static bool RestartAsHost { get; private set; }
        static string restartGameScene = "ZombieTownScene";
        [SerializeField, Range(1, 8)] int maxPlayers = 8;
        NetworkManager manager;
        void Awake()
        {
            EnsureConfigured();
            SceneLoadCoordinator.LoadOverride = HandleSceneLoad;
#if UNITY_SERVER
            manager.StartServer();
#endif
        }
        void Start() => EnsureConfigured();
        public void EnsureConfigured()
        {
            manager = GetComponent<NetworkManager>() ?? NetworkManager.Singleton;
            if (manager != null) manager.ConnectionApprovalCallback = Approve;
        }
        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = manager != null && manager.ConnectedClients.Count < maxPlayers;
            response.CreatePlayerObject = response.Approved;
            response.Pending = false;
            response.Reason = response.Approved ? string.Empty : "Server is full (8 players).";
        }
        public void StartHost() { EnsureConfigured(); manager.StartHost(); }
        public void StartClient() { EnsureConfigured(); manager.StartClient(); }
        public void StartServer() { EnsureConfigured(); manager.StartServer(); }

        public static bool ConsumeRestartAsHost()
        {
            bool value = RestartAsHost;
            RestartAsHost = false;
            return value;
        }

        public static bool ConsumeRestartAsHost(out string gameScene)
        {
            gameScene = restartGameScene;
            return ConsumeRestartAsHost();
        }

        bool HandleSceneLoad(string sceneName)
        {
            EnsureConfigured();
            if (manager == null || !manager.IsListening) return false;

            string activeScene = SceneManager.GetActiveScene().name;
            bool nextGameplayLevel =
                (sceneName == "RadioOutpostScene" && activeScene == "ZombieTownScene") ||
                (sceneName == "HarborEvacuationScene" && activeScene == "RadioOutpostScene") ||
                (sceneName == "RetreatDefenseScene" && activeScene == "HarborEvacuationScene") ||
                (sceneName == "HarborViewCityScene" && activeScene == "RetreatDefenseScene") ||
                (sceneName == "DeadOrbitScene" && activeScene == "HarborViewCityScene") ||
                (sceneName == "BusEscapeFinaleScene" && activeScene == "DeadOrbitScene");
            if (nextGameplayLevel)
            {
                // Preserve the connected player objects (and their point balances)
                // while resetting readiness so every owner sees character selection
                // again in level 2.
                if (manager.IsServer)
                {
                    foreach (PlayerClassController player in
                             FindObjectsByType<PlayerClassController>(FindObjectsInactive.Include))
                        player.PrepareForLevelTransition();
                    manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                }
                return true;
            }

            if (sceneName == "WinScene" || sceneName == "LoseScene")
            {
                // Only the server changes the network scene. Clients receive the
                // synchronized transition and never create a private result scene.
                if (manager.IsServer)
                    manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                return true;
            }

            // Returning to the menu is a real disconnect. Previously this reused the
            // gameplay restart path, so LocalNetworkLauncher immediately hosted the
            // level that the player had just left.
            if (sceneName == "IntroMenu")
            {
                RestartAsHost = false;
                SessionRestartRunner.Begin(manager, "IntroMenu");
                return true;
            }

            if (sceneName == "ZombieTownScene" || sceneName == "RadioOutpostScene" ||
                sceneName == "HarborEvacuationScene" || sceneName == "RetreatDefenseScene" ||
                sceneName == "HarborViewCityScene" || sceneName == "DeadOrbitScene" || sceneName == "BusEscapeFinaleScene")
            {
                RestartAsHost = manager.IsHost;
                restartGameScene = sceneName;
                SessionRestartRunner.Begin(manager, "IntroMenu");
                return true;
            }

            return false;
        }

        void OnDestroy()
        {
            if (SceneLoadCoordinator.LoadOverride == HandleSceneLoad)
                SceneLoadCoordinator.LoadOverride = null;
        }
    }

    /// <summary>Keeps the restart coroutine alive while the old NetworkManager is destroyed.</summary>
    sealed class SessionRestartRunner : MonoBehaviour
    {
        NetworkManager oldManager;
        string targetScene;

        public static void Begin(NetworkManager manager, string sceneName)
        {
            if (FindAnyObjectByType<SessionRestartRunner>() != null) return;
            GameObject runner = new("Session Restart");
            DontDestroyOnLoad(runner);
            SessionRestartRunner component = runner.AddComponent<SessionRestartRunner>();
            component.oldManager = manager;
            component.targetScene = sceneName;
            component.StartCoroutine(component.Restart());
        }

        System.Collections.IEnumerator Restart()
        {
            Time.timeScale = 1f;
            if (oldManager != null)
            {
                oldManager.Shutdown();
                Destroy(oldManager.gameObject);
            }
            // NetworkManager.Singleton is released at the end of the frame.
            yield return null;
            yield return null;
            SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
            Destroy(gameObject);
        }
    }
}
