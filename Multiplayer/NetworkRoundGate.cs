using Unity.Netcode;
using UnityEngine;

namespace ZombieTown.Multiplayer
{
    public static class NetworkRoundGate
    {
        static bool hasCachedValue;
        static bool cachedIsOpen;

        public static bool IsOpen
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening) return true;
                if (hasCachedValue) return cachedIsOpen;

                hasCachedValue = true;
                cachedIsOpen = false;
                // Rely only on the replicated NetworkVariable (works identically on server and
                // clients) instead of a static field, which risked staying stale across scenes.
                foreach (PlayerClassController player in Object.FindObjectsByType<PlayerClassController>())
                    if (player != null && player.RoundStarted.Value)
                    {
                        cachedIsOpen = true;
                        break;
                    }
                return cachedIsOpen;
            }
        }

        public static void Invalidate() => hasCachedValue = false;
    }
}
