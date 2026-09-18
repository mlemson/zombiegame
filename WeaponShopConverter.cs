using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ZombieTown.Multiplayer;

namespace ZombieTown.Progression
{
    [DefaultExecutionOrder(-850)]
    public sealed class WeaponShopConverter : MonoBehaviour
    {
        PlayerClassController localPlayer;
        Canvas promptCanvas;
        Text promptText;
        float nextScan;
        float nextPlayerSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<WeaponShopConverter>() != null) return;
            GameObject root = new("Weapon Shop System");
            DontDestroyOnLoad(root);
            root.AddComponent<WeaponShopConverter>();
        }

        public static WeaponShopTerminal Convert(WeaponPickup pickup)
        {
            if (pickup == null || pickup.WeaponPrefab == null) return null;
            WeaponShopTerminal terminal = pickup.GetComponent<WeaponShopTerminal>() ??
                                          pickup.gameObject.AddComponent<WeaponShopTerminal>();
            terminal.Configure(pickup.WeaponPrefab);
            // Conversion disables Start, so do the pickup-only visibility cleanup here.
            pickup.HideFirstPersonHandsFromPickupVisual();
            pickup.enabled = false;
            foreach (Transform child in pickup.GetComponentsInChildren<Transform>(true))
                if (child != pickup.transform) child.gameObject.layer = 0;

            Rigidbody body = pickup.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
            Collider trigger = pickup.GetComponent<Collider>();
            if (trigger != null) trigger.isTrigger = true;
            return terminal;
        }

        void Awake() => ConvertAllPickups();

        static void ConvertAllPickups()
        {
            // Conversion removes the pickup from Active. Walk backwards and process it once.
            // The old scene scan rebuilt every converted hierarchy twice per second.
            using var sample = ConvertMarker.Auto();
            for(int i=WeaponPickup.Active.Count-1;i>=0;i--)
                Convert(WeaponPickup.Active[i]);
        }
        static readonly Unity.Profiling.ProfilerMarker ConvertMarker = new("ZombieTown.ConvertWeaponPickups");

        void Update()
        {
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + .5f;
                ConvertAllPickups();
            }

            if (localPlayer == null && Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + .5f;
                foreach (PlayerClassController player in
                         FindObjectsByType<PlayerClassController>(FindObjectsInactive.Exclude))
                {
                    if (!player.IsOwner || !player.IsSpawned) continue;
                    localPlayer = player;
                    break;
                }
            }

            WeaponShopTerminal nearest = FindNearestTerminal(localPlayer, 3.1f);
            if (nearest == null || localPlayer == null || Time.timeScale <= 0f || GameplayInteraction.Carrying || localPlayer.IsUsingMountedGun)
            {
                HidePrompt();
                return;
            }

            bool owned = localPlayer.OwnsWeapon(nearest.WeaponPrefab);
            int balance = localPlayer.CurrentPoints;
            if (owned)
            {
                int refillPrice = nearest.AmmoPrice;
                bool full = localPlayer.IsWeaponAmmoFull(nearest.WeaponPrefab);
                string refillMessage = full
                    ? GameLocalization.Text($"{nearest.DisplayName}  -  AMMO FULL", $"{nearest.DisplayName}  -  AMMO VOL")
                    : balance >= refillPrice
                        ? GameLocalization.Text($"F  REFILL AMMO  -  ${refillPrice}", $"F  AMMO BIJVULLEN  -  ${refillPrice}")
                        : GameLocalization.Text($"REFILL AMMO  -  ${refillPrice}  (NEED ${refillPrice - balance})",
                            $"AMMO BIJVULLEN  -  ${refillPrice}  (NOG ${refillPrice - balance})");
                ShowPrompt(refillMessage);

                if (!full && balance >= refillPrice && Keyboard.current != null &&
                    Unity.FPS.Game.GameplayInteraction.Pressed)
                { GameplayInteraction.Consume(); localPlayer.RequestAmmoRefill(nearest.PurchaseKey); }
                return;
            }

            string message = balance >= nearest.Price
                ? GameLocalization.Text($"F  BUY {nearest.DisplayName}  -  ${nearest.Price}",
                    $"F  KOOP {nearest.DisplayName}  -  ${nearest.Price}")
                : GameLocalization.Text($"{nearest.DisplayName}  -  ${nearest.Price}  (NEED ${nearest.Price - balance})",
                    $"{nearest.DisplayName}  -  ${nearest.Price}  (NOG ${nearest.Price - balance})");
            ShowPrompt(message);

            if (balance >= nearest.Price && Keyboard.current != null &&
                Unity.FPS.Game.GameplayInteraction.Pressed)
            { GameplayInteraction.Consume(); localPlayer.RequestWeaponPurchase(nearest.PurchaseKey, nearest.WeaponPrefab); }
        }

        static WeaponShopTerminal FindNearestTerminal(PlayerClassController player, float range)
        {
            if (player == null) return null;
            WeaponShopTerminal result = null;
            float closest = range * range;
            IReadOnlyList<WeaponShopTerminal> terminals = WeaponShopTerminal.ActiveTerminals;
            for (int i = terminals.Count - 1; i >= 0; i--)
            {
                WeaponShopTerminal terminal = terminals[i];
                if (terminal == null || !terminal.isActiveAndEnabled || terminal.WeaponPrefab == null || !terminal.IsAvailable) continue;
                float sqr = (terminal.transform.position - player.transform.position).sqrMagnitude;
                if (sqr >= closest) continue;
                closest = sqr;
                result = terminal;
            }
            return result;
        }

        void ShowPrompt(string message)
        {
            if (promptCanvas == null)
            {
                promptCanvas = RuntimeMenuUI.CreateCanvas("Weapon Shop Prompt", 865);
                DontDestroyOnLoad(promptCanvas.gameObject);
                RectTransform panel = RuntimeMenuUI.Block("Prompt", promptCanvas.transform,
                    new Color(.025f, .04f, .055f, .92f));
                panel.anchorMin = new Vector2(.25f, .11f);
                panel.anchorMax = new Vector2(.75f, .18f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                promptText = RuntimeMenuUI.Label("Text", panel, string.Empty, 18,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(promptText.rectTransform, 12, 12, 4, 4);
            }
            promptCanvas.gameObject.SetActive(true);
            promptText.text = message;
        }

        void HidePrompt()
        {
            if (promptCanvas != null) promptCanvas.gameObject.SetActive(false);
        }
    }
}
