using System;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZombieTown.Multiplayer;

namespace ZombieTown.Progression
{
    [DisallowMultipleComponent]
    public sealed class WeaponShopTerminal : MonoBehaviour
    {
        static readonly List<WeaponShopTerminal> Terminals = new();
        static readonly Dictionary<string, WeaponController> WeaponsByKey =
            new(StringComparer.OrdinalIgnoreCase);

        [SerializeField] WeaponController weaponPrefab;
        [SerializeField] string purchaseKey;
        [SerializeField, Min(0)] int price;

        public ZombieTown.Foundation.WeaponDefinition definition;
        public ZombieTown.Foundation.EconomyProfile economy;
        public bool useDefinitionDefaults=true, overrideCost;
        public int priceOverride;
        public int requiredStage=1;
        public string shopId;
        public bool IsAvailable => ZombieTown.Foundation.GameplaySceneContext.Active == null ? requiredStage<=1 : ZombieTown.Foundation.GameplaySceneContext.Active.Stage>=requiredStage;
        public WeaponController WeaponPrefab => definition!=null?definition.weaponPrefab:weaponPrefab;
        public string PurchaseKey => definition!=null?definition.weaponKey:purchaseKey;
        public int Price => definition!=null ? Mathf.Max(0,Mathf.RoundToInt((!useDefinitionDefaults || overrideCost?priceOverride:definition.basePurchaseCost)*(economy!=null?economy.weaponPricingMultiplier:1))) : price;
        public int AmmoPrice => Mathf.Max(1,Mathf.RoundToInt(Price*(economy!=null?economy.ammoPricingMultiplier:.5f)));
        public string DisplayName => definition!=null ? definition.displayName : WeaponPrefab != null && !string.IsNullOrWhiteSpace(WeaponPrefab.WeaponName)
            ? WeaponPrefab.WeaponName : PurchaseKey;
        public static IReadOnlyList<WeaponShopTerminal> ActiveTerminals => Terminals;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetRegistry()
        {
            Terminals.Clear();
            WeaponsByKey.Clear();
        }

        public void Configure(WeaponController weapon)
        {
            if (definition!=null) return;
            weaponPrefab = weapon;
            purchaseKey = weapon != null ? weapon.gameObject.name : string.Empty;
            price = ResolvePrice(purchaseKey);
            RegisterWeapon();
            if (!Terminals.Contains(this)) Terminals.Add(this);
        }

        public static bool TryResolveWeapon(string key, out WeaponController weapon) =>
            WeaponsByKey.TryGetValue(key ?? string.Empty, out weapon) && weapon != null;

        public static bool ValidatePurchase(PlayerClassController player,string key,ref int cost,bool ammo=false) {
            foreach(var terminal in Terminals) if(terminal!=null && terminal.PurchaseKey==key && terminal.IsAvailable && Vector3.Distance(player.transform.position,terminal.transform.position)<=3.5f && player.IsReady.Value && player.RoundStarted.Value && !player.IsDowned.Value) { cost=ammo?terminal.AmmoPrice:terminal.Price; return true; }
            return false;
        }
        public static int GetPrice(string weaponKey)
        {
            foreach(var terminal in Terminals) if(terminal!=null && terminal.PurchaseKey==weaponKey) return terminal.Price;
            string key = weaponKey ?? string.Empty;
            int basePrice;
            if (Contains(key, "Launcher") || Contains(key, "Disc")) basePrice = 200;
            else if (Contains(key, "Marksman") || Contains(key, "Rifle")) basePrice = 150;
            else if (Contains(key, "Chainsaw")) basePrice = 130;
            else if (Contains(key, "Shotgun")) basePrice = 110;
            else if (Contains(key, "SMG") || Contains(key, "MP5")) basePrice = 100;
            else if (Contains(key, "Coach")) basePrice = 90;
            else if (Contains(key, "Sword")) basePrice = 80;
            else if (Contains(key, "Blaster")) basePrice = 45;
            else if (Contains(key, "Pistol") || Contains(key, "Revolver")) basePrice = 60;
            else basePrice = 100;
            return ApplyLevelFourPlayerMultiplier(basePrice);
        }

        int ResolvePrice(string weaponKey)
        {
            if (gameObject.scene.IsValid() &&
                (gameObject.scene.name.Equals("RetreatDefenseScene", StringComparison.OrdinalIgnoreCase) ||
                 gameObject.scene.name.Equals("HarborViewCityScene", StringComparison.OrdinalIgnoreCase) ||
                 gameObject.scene.name.Equals("DeadOrbitScene", StringComparison.OrdinalIgnoreCase)))
                return ApplyLevelFourPlayerMultiplier(GetLevelFourPrice(weaponKey));
            return GetPrice(weaponKey);
        }

        static int ApplyLevelFourPlayerMultiplier(int basePrice)
        {
            if (!SceneManager.GetActiveScene().name.Equals("RetreatDefenseScene", StringComparison.OrdinalIgnoreCase))
                return basePrice;

            int activePlayers = 0;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
                if (player != null && player.IsSpawned && player.IsReady.Value && !player.IsDowned.Value)
                    activePlayers++;
            if (activePlayers <= 0) return basePrice;
            float multiplier = 1f;
            return Mathf.Max(0, Mathf.RoundToInt(basePrice * multiplier));
        }

        static int GetLevelFourPrice(string weaponKey)
        {
            string key = weaponKey ?? string.Empty;
            if (Contains(key, "Chainsaw")) return 800;
            if (Contains(key, "Launcher") || Contains(key, "Disc")) return 700;
            if (Contains(key, "Noreen")) return 650;
            if (Contains(key, "SVD")) return 525;
            if (Contains(key, "Shotgun")) return 450;
            if (Contains(key, "Marksman")) return 350;
            if (Contains(key, "SMG") || Contains(key, "MP5")) return 220;
            if (Contains(key, "Coach")) return 160;
            if (Contains(key, "Sword")) return 100;
            if (Contains(key, "Upgraded") || Contains(key, "Revolver")) return 90;
            if (Contains(key, "Blaster") || Contains(key, "Pistol")) return 75;
            if (Contains(key, "Rifle")) return 500;
            return 180;
        }

        void RegisterWeapon()
        {
            if (string.IsNullOrWhiteSpace(PurchaseKey) || WeaponPrefab == null) return;
            WeaponsByKey[PurchaseKey] = WeaponPrefab;
        }

        static bool Contains(string value, string part) =>
            value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        void OnDisable() => Terminals.Remove(this);

        void OnEnable()
        {
            if (WeaponPrefab == null) return;
            if (string.IsNullOrWhiteSpace(purchaseKey)) purchaseKey = weaponPrefab.gameObject.name;
            if (price <= 0) price = ResolvePrice(purchaseKey);
            RegisterWeapon();
            if (!Terminals.Contains(this)) Terminals.Add(this);
        }
    }
}
