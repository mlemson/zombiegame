using System;
using UnityEngine.SceneManagement;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Lets the network layer safely take over scene changes without making the
    /// reusable FPS Game/UI assemblies depend directly on Netcode.
    /// </summary>
    public static class SceneLoadCoordinator
    {
        public static Func<string, bool> LoadOverride;

        public static void LoadScene(string sceneName)
        {
            if (LoadOverride != null && LoadOverride(sceneName)) return;
            SceneManager.LoadScene(sceneName);
        }
    }
}
