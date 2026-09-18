using UnityEngine;
using ZombieTown.Multiplayer;

namespace ZombieTown.Foundation
{
    public static class MultiplayerPriceScaling
    {
        static int cachedPlayerCount = 1;
        static float nextPlayerCountRefresh;

        public static int Scale(int basePrice, int playerCount)
        {
            float multiplier = playerCount <= 1 ? .5f : playerCount >= 4 ? 2f : 1f;
            return Mathf.Max(0, Mathf.RoundToInt(basePrice * multiplier));
        }

        public static int GetActivePlayerCount()
        {
            if (GameplaySceneContext.Active != null)
                return GameplaySceneContext.Active.PlayerCount;

            if (Time.unscaledTime < nextPlayerCountRefresh)
                return cachedPlayerCount;

            int count = 0;
            foreach (PlayerClassController player in Object.FindObjectsByType<PlayerClassController>())
            {
                if (player != null && player.IsSpawned && player.IsReady.Value && !player.IsDowned.Value)
                    count++;
            }

            cachedPlayerCount = Mathf.Max(1, count);
            nextPlayerCountRefresh = Time.unscaledTime + .1f;
            return cachedPlayerCount;
        }
    }
}