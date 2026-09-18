using System.Collections.Generic;
using System.Collections;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.FPS.AI;
using System;
using UnityEngine.UI;
using ZombieTown.LevelTwo;
using ZombieTown.Progression;
using ZombieTown.Traversal;
using ZombieTown.Weapons;

namespace ZombieTown.Multiplayer
{
    [DefaultExecutionOrder(-900)]
    public sealed partial class PlayerClassController : NetworkBehaviour, IDamageAbsorber, IAmmoPickupFilter, IMeleeAttackHandler, IContinuousMeleeAttackHandler, IWeaponNoiseReporter
    {
        [SerializeField] ArchetypeTuning[] archetypes;
        [SerializeField] Transform visualRoot;
        [SerializeField] Transform rightHandSocket;
        [SerializeField] RuntimeAnimatorController locomotionController;
        [Header("First Person Arms")]
        [SerializeField] GameObject firstPersonArmsPrefab;
        [SerializeField] Vector3 firstPersonArmsPosition = new(.34f, -1.2f, 0f);
        [SerializeField] Vector3 firstPersonArmsRotation;
        [SerializeField, Range(.5f, 1.5f)] float firstPersonArmsScale = 1f;
        [SerializeField] float meleeRange = 2.35f;
        [SerializeField, Min(.5f)] float closeMeleeRadius = 1.15f;
        [SerializeField, Range(1f, 1.5f)] float regularMeleeDamageMultiplier = 1.1f;
        [SerializeField, Range(0f, 1f)] float swordHeadshotChance = .35f;
        [SerializeField] LayerMask meleeLayers = -1;
        [Header("Guard Takedown")]
        [SerializeField, Min(1f)] float guardTakedownRange = 2.35f;
        [Header("Striker Special")]
        [SerializeField, Min(.1f)] float specialChargeDuration = 1f;
        [SerializeField, Min(.5f)] float specialRadius = 4f;
        [SerializeField, Range(.5f, 6f)] float specialKnockbackDistance = 4.2f;
        [SerializeField, Range(.1f, 2f)] float specialDamageMultiplier = .5f;
        [Header("Melee Feedback")]
        [SerializeField] AudioClip meleeSwingSfx;
        [SerializeField] AudioClip specialMeleeSwingSfx;
        [Header("Medic Heal")]
        [SerializeField, Min(1f)] float healRadius = 4f;
        // Reinterpreted as a heal-per-second rate applied every tick for the aura's duration.
        [SerializeField, Min(.5f)] float healAuraDuration = 4f;
        [SerializeField, Min(.5f)] float healCooldown = 5f;
        [SerializeField] GameObject healAuraVfxPrefab;
        const float HealAuraTickInterval = .5f;
        [Header("Downed & Revive")]
        [SerializeField, Min(1f)] float reviveRange = 2.4f;
        [SerializeField, Min(1f)] float reviveHoldSeconds = 5f;
        [SerializeField, Min(.5f)] float reviveHoldSecondsMedic = 2.5f;
        [SerializeField, Min(1f)] float downedReviveHealth = 45f;
        const ulong NoReviver = ulong.MaxValue;

        public readonly NetworkVariable<PlayerArchetype> SelectedClass = new(PlayerArchetype.Guardian);
        public readonly NetworkVariable<float> Armor = new();
        public readonly NetworkVariable<bool> IsReady = new(false);
        public readonly NetworkVariable<bool> RoundStarted = new(false);
        public readonly NetworkVariable<bool> IsMeleeEquipped = new(false);
        public readonly NetworkVariable<FixedString64Bytes> EquippedWeaponKey = new();
        public readonly NetworkVariable<bool> ChainsawPowered = new(false);
        public readonly NetworkVariable<float> SyncedHealth = new();
        public readonly NetworkVariable<Vector3> SpawnPosition = new();
        public readonly NetworkVariable<Quaternion> SpawnRotation = new(Quaternion.identity);
        public readonly NetworkVariable<float> StandingHeight = new(2.16f);
        public readonly NetworkVariable<int> Points = new(0);
        public readonly NetworkVariable<bool> HealAuraActive = new(false);
        public readonly NetworkVariable<bool> IsDowned = new(false);
        public readonly NetworkVariable<float> ReviveProgress = new(0f);
        GameObject spawnedVisual;
        float lastArmorDamageTime;
        float nextAbilityTime;
        float serverNextMeleeTime;
        float serverNextChainsawTime;
        float serverNextSpecialTime;
        float serverNextReloadCosmeticTime;
        float serverNextGunshotCosmeticTime;
        float serverNextProjectileBatchTime;
        float serverProjectileBatchUntil;
        int serverProjectileHitsRemaining;
        FixedString64Bytes serverProjectileWeaponKey;
        float serverNextHealTime;
        float specialChargeStarted;
        float nextMeleeAttackTime;
        int localComboIndex;
        int serverComboIndex;
        bool isChargingSpecial;
        bool wasReloading;
        Health health;
        PlayerWeaponsManager weapons;
        PlayerInputHandler input;
        PlayerAudio playerAudio;
        FirstPersonHands firstPersonHands;
        WeaponHoldIK spawnedWeaponHold;
        WeaponController observedOwnerWeapon;
        GameObject spawnedHandProp;
        Transform spawnedHandSocket;
        AudioSource remoteChainsawSource;
        bool lastOwnerChainsawPowered;
        readonly Collider[] takedownHits = new Collider[20];
        OutpostGuardAI nearbyTakedownGuard;
        Canvas takedownPromptCanvas;
        Text takedownPromptText;
        Canvas pointsCanvas;
        Text pointsText;
        int offlinePoints;
        GameObject healAuraVisual;
        Canvas healAuraStatusCanvas;
        Text healAuraStatusText;
        Canvas specialChargeCanvas;
        Image specialChargeFill;
        Text specialChargeText;
        Canvas revivePromptCanvas;
        Text revivePromptText;
        Canvas downedStatusCanvas;
        Text downedStatusText;
        ulong reviverClientId = NoReviver;
        float reviverLastPingTime = float.NegativeInfinity;
        readonly HashSet<string> pendingPurchaseKeys = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> serverPurchasedKeys = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> pendingRefillKeys = new(StringComparer.OrdinalIgnoreCase);
        // Server-only: true once the host has pressed Start Game for the current level.
        // Lets a player who joins mid-round skip waiting for a second host click.
        static bool serverSessionRoundStarted;
        public static bool SessionRoundStarted => serverSessionRoundStarted;
        public event Action ClassConfirmed;
        public bool HasNearbyGuardTakedown => nearbyTakedownGuard != null;
        public int CurrentPoints => IsSpawned ? Points.Value : offlinePoints;

        public void ReportWeaponNoise(bool silenced, Vector3 position)
        {
            if (silenced || !IsFinite(position)) return;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !IsSpawned)
            {
                PublishWeaponNoise(position);
                return;
            }
            if (!IsOwner) return;

            // Host-fired shots already execute on the server. Publishing here avoids
            // delaying or dropping the guard alert behind a host-to-server RPC.
            if (IsServer)
            {
                PublishWeaponNoise(position);
                return;
            }
            ReportWeaponNoiseRpc(position);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        void ReportWeaponNoiseRpc(Vector3 position)
        {
            if (!IsSpawned || IsDowned.Value || !IsFinite(position) ||
                Vector3.SqrMagnitude(position - transform.position) > 16f) return;
            PublishWeaponNoise(position);
        }

        void PublishWeaponNoise(Vector3 position)
        {
            ZombieTown.LevelTwo.CombatNoiseSystem.Report(position,
                ZombieTown.LevelTwo.CombatNoiseSystem.UnsilencedGunshotRadius, gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterDeathBroadcastSuppression()
        {
            // Networked deaths are resolved as downed/revive; only the explicit
            // BroadcastGameOverRpc should ever end the match while networked.
            PlayerCharacterController.SuppressDeathBroadcast = controller =>
                controller != null && controller.GetComponent<PlayerClassController>() is { } owner && owner.IsSpawned;
        }

        void Awake()
        {
            health = GetComponent<Health>();
            if (health != null) health.OnDie += OnPlayerDied;
            weapons = GetComponent<PlayerWeaponsManager>();
            input = GetComponent<PlayerInputHandler>();
            playerAudio = GetComponent<PlayerAudio>();
            if (GetComponent<PlayerLadderClimber>() == null)
                gameObject.AddComponent<PlayerLadderClimber>();
            if (GetComponent<PlayerZiplineRider>() == null)
                gameObject.AddComponent<PlayerZiplineRider>();
            EnsureGameplayServices();
        }

        public void PrepareForLevelTransition()
        {
            if (IsSpawned && !IsServer) return;
            if (IsSpawned) IsReady.Value = false;
            if (IsSpawned) RoundStarted.Value = false;
            if (IsServer) serverSessionRoundStarted = false;
            if (IsSpawned && IsServer)
            {
                bool enteringFirstPlayableLevel =
                    SceneManager.GetActiveScene().name.Equals("ZombieTownScene", StringComparison.OrdinalIgnoreCase);
                Points.Value = enteringFirstPlayableLevel
                    ? 100
                    : Mathf.FloorToInt(Points.Value * (ZombieTown.Foundation.GameplaySceneContext.Active?.balance?.economy?.levelTransitionCarryover ?? .5f));
            }
            pendingPurchaseKeys.Clear();
            serverPurchasedKeys.Clear();

            // A full reset except money: strip weapons/armor/melee state so level 2 starts clean.
            if (weapons != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    WeaponController existing = weapons.GetWeaponAtSlotIndex(i);
                    if (existing != null) weapons.RemoveWeapon(existing);
                }
            }
            if (health != null) health.CurrentHealth = health.MaxHealth;
            // GameFlowManager.FreezeCompletedMission() marks every Health invincible when
            // level 1's objective completes; that flag must not carry into level 2.
            if (health != null) health.Invincible = false;
            if (IsSpawned) { Armor.Value = 0f; PurchasedArmorCapacity.Value = 0f; }
            if (IsSpawned) IsMeleeEquipped.Value = false;
            if (IsSpawned) EquippedWeaponKey.Value = default;
            if (IsSpawned) ChainsawPowered.Value = false;
            if (IsSpawned) IsDowned.Value = false;
            if (IsSpawned) ReviveProgress.Value = 0f;
            localComboIndex = 0;
            serverComboIndex = 0;
            isChargingSpecial = false;
            HideSpecialChargeHud();
            wasReloading = false;

            if (IsOwner) GetComponent<NetworkPlayerOwnership>()?.SetLocalGameplayReady(false);
            NetworkRoundGate.Invalidate();
        }

        void OnPlayerDied()
        {
            if (IsSpawned)
            {
                if (IsServer) ResolveNetworkedDeath();
            }
            else
            {
                pendingPurchaseKeys.Clear();
                serverPurchasedKeys.Clear();
                int previous = offlinePoints;
                offlinePoints = 0;
                OnPointsChanged(previous, 0);
            }
        }

        // Server-only: a single player's death only downs them while a teammate is still
        // standing. The match only truly ends once the last alive player goes down.
        void ResolveNetworkedDeath()
        {
            if (IsDowned.Value) return;
            if (CountAliveOtherPlayers() > 0)
            {
                IsDowned.Value = true;
                ReviveProgress.Value = 0f;
                reviverClientId = NoReviver;
                if (health != null) health.Invincible = true;
            }
            else
            {
                pendingPurchaseKeys.Clear();
                serverPurchasedKeys.Clear();
                Points.Value = 0;
                BroadcastGameOverRpc();
            }
        }

        int CountAliveOtherPlayers()
        {
            int count = 0;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
                if (player != null && player != this && player.IsSpawned && player.IsReady.Value && !player.IsDowned.Value)
                    count++;
            return count;
        }

        // A disconnecting client cannot be revived; re-check the last-player-standing
        // condition so the remaining downed teammates are not left stuck forever.
        void OnAnyClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
                if (player != null && player.IsSpawned && player.IsReady.Value && !player.IsDowned.Value)
                    return;
            BroadcastGameOverRpc();
        }

        [Rpc(SendTo.Everyone)]
        void BroadcastGameOverRpc() => EventManager.Broadcast(Events.PlayerDeathEvent);
        public override void OnNetworkSpawn()
        {
            WeaponUpgrades.OnListChanged += OnUpgradeChanged;
            NetworkRoundGate.Invalidate();
            if (IsServer && Points.Value == 0 &&
                !SceneManager.GetActiveScene().name.Equals("ZombieTownScene", StringComparison.OrdinalIgnoreCase))
                Points.Value = ZombieTown.Foundation.GameplaySceneContext.Active?.balance?.economy?.startingPoints ?? 100;
            SelectedClass.OnValueChanged += OnClassChanged;
            Armor.OnValueChanged += OnArmorChanged;
            IsReady.OnValueChanged += OnReadyChanged;
            RoundStarted.OnValueChanged += OnRoundStartedChanged;
            IsMeleeEquipped.OnValueChanged += OnMeleeEquippedChanged;
            EquippedWeaponKey.OnValueChanged += OnEquippedWeaponKeyChanged;
            ChainsawPowered.OnValueChanged += OnChainsawPoweredChanged;
            StandingHeight.OnValueChanged += OnHeightChanged;
            Points.OnValueChanged += OnPointsChanged;
            SyncedHealth.OnValueChanged += OnSyncedHealthChanged;
            HealAuraActive.OnValueChanged += OnHealAuraActiveChanged;
            IsDowned.OnValueChanged += OnDownedChanged;
            if (health != null) health.OnDamaged += SyncHealth;
            if (health != null) health.OnHealed += SyncHealth;
            if (IsServer && health != null) SyncedHealth.Value = health.CurrentHealth;
            ApplyHealAuraVisual(HealAuraActive.Value);
            ApplyDownedPresentation(IsDowned.Value);
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback += OnAnyClientDisconnected;
            ApplyPresentation(SelectedClass.Value);
            if (IsOwner) ConfigureFirstPersonView(SelectedClass.Value);
            if (IsOwner) EnsurePointsHud();
            if (IsOwner && weapons != null) weapons.OnSwitchedToWeapon += OnOwnerWeaponSwitched;
            if (IsOwner && playerAudio != null) playerAudio.SoundPlayed += OnLocalPlayerSound;
            if (IsServer) ApplyServerClass(SelectedClass.Value);
            if (IsOwner && IsReady.Value)
            {
                PrepareOwnerClass(SelectedClass.Value, StandingHeight.Value);
                ApplyOwnerSpawnAndRelease();
                StartCoroutine(ReplaceOwnerLoadoutNextFrame(SelectedClass.Value));
            }
        }
        public override void OnNetworkDespawn()
        {
            WeaponUpgrades.OnListChanged -= OnUpgradeChanged;
            firstPersonHands?.CancelSpecialMeleeCharge();
            isChargingSpecial = false;
            HideSpecialChargeHud();
            input?.SetWeaponFireEnabled(true);
            SelectedClass.OnValueChanged -= OnClassChanged;
            Armor.OnValueChanged -= OnArmorChanged;
            IsReady.OnValueChanged -= OnReadyChanged;
            RoundStarted.OnValueChanged -= OnRoundStartedChanged;
            IsMeleeEquipped.OnValueChanged -= OnMeleeEquippedChanged;
            EquippedWeaponKey.OnValueChanged -= OnEquippedWeaponKeyChanged;
            ChainsawPowered.OnValueChanged -= OnChainsawPoweredChanged;
            if (weapons != null) weapons.OnSwitchedToWeapon -= OnOwnerWeaponSwitched;
            ObserveOwnerWeapon(null);
            if (playerAudio != null) playerAudio.SoundPlayed -= OnLocalPlayerSound;
            StandingHeight.OnValueChanged -= OnHeightChanged;
            Points.OnValueChanged -= OnPointsChanged;
            SyncedHealth.OnValueChanged -= OnSyncedHealthChanged;
            HealAuraActive.OnValueChanged -= OnHealAuraActiveChanged;
            IsDowned.OnValueChanged -= OnDownedChanged;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnAnyClientDisconnected;
            if (health != null) health.OnDamaged -= SyncHealth;
            if (health != null) health.OnHealed -= SyncHealth;
            if (takedownPromptCanvas != null) Destroy(takedownPromptCanvas.gameObject);
            takedownPromptCanvas = null;
            takedownPromptText = null;
            if (pointsCanvas != null) Destroy(pointsCanvas.gameObject);
            pointsCanvas = null;
            pointsText = null;
            if (revivePromptCanvas != null) Destroy(revivePromptCanvas.gameObject);
            revivePromptCanvas = null;
            revivePromptText = null;
            if (downedStatusCanvas != null) Destroy(downedStatusCanvas.gameObject);
            downedStatusCanvas = null;
            downedStatusText = null;
            if (healAuraStatusCanvas != null) Destroy(healAuraStatusCanvas.gameObject);
            healAuraStatusCanvas = null;
            healAuraStatusText = null;
            if (specialChargeCanvas != null) Destroy(specialChargeCanvas.gameObject);
            specialChargeCanvas = null;
            specialChargeFill = null;
            specialChargeText = null;
            if (healAuraVisual != null) Destroy(healAuraVisual);
            healAuraVisual = null;
            if (remoteChainsawSource != null) Destroy(remoteChainsawSource);
            remoteChainsawSource = null;
            pendingPurchaseKeys.Clear();
            serverPurchasedKeys.Clear();
            NetworkRoundGate.Invalidate();
        }

        public void AwardKillPoints(int amount)
        {
            amount = Mathf.Max(0, amount);
            if (amount == 0) return;
            if (IsSpawned)
            {
                if (!IsServer) return;
                Points.Value = Mathf.Min(999999, Points.Value + amount);
            }
            else
            {
                int previous = offlinePoints;
                offlinePoints = Mathf.Min(999999, offlinePoints + amount);
                OnPointsChanged(previous, offlinePoints);
            }
        }

        public bool OwnsWeapon(WeaponController weaponPrefab) =>
            weaponPrefab != null && weapons != null && weapons.HasWeapon(weaponPrefab) != null;

        public void RequestWeaponPurchase(string purchaseKey, WeaponController weaponPrefab)
        {
            if (!IsOwner || string.IsNullOrWhiteSpace(purchaseKey) || weaponPrefab == null ||
                OwnsWeapon(weaponPrefab) || pendingPurchaseKeys.Contains(purchaseKey))
                return;

            int price = WeaponShopTerminal.GetPrice(purchaseKey);
            if (CurrentPoints < price) return;
            pendingPurchaseKeys.Add(purchaseKey);
            if (IsSpawned)
                RequestWeaponPurchaseRpc(new FixedString64Bytes(purchaseKey));
            else
            {
                offlinePoints -= price;
                CompleteWeaponPurchase(purchaseKey, true);
                OnPointsChanged(offlinePoints + price, offlinePoints);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestWeaponPurchaseRpc(FixedString64Bytes requestedKey)
        {
            string key = requestedKey.ToString();
            int price = WeaponShopTerminal.GetPrice(key);
            bool approved = WeaponShopTerminal.ValidatePurchase(this,key,ref price) && price >= 0 && Points.Value >= price && serverPurchasedKeys.Add(key);
            if (approved) Points.Value -= price;
            WeaponPurchaseResultRpc(requestedKey, approved);
        }

        [Rpc(SendTo.Everyone)]
        void WeaponPurchaseResultRpc(FixedString64Bytes purchasedKey, bool approved)
        {
            if (!IsOwner) return;
            CompleteWeaponPurchase(purchasedKey.ToString(), approved);
        }

        void CompleteWeaponPurchase(string key, bool approved)
        {
            pendingPurchaseKeys.Remove(key);
            if (!approved || weapons == null ||
                !WeaponShopTerminal.TryResolveWeapon(key, out WeaponController weaponPrefab) ||
                OwnsWeapon(weaponPrefab))
                return;

            if (!weapons.AddWeapon(weaponPrefab, equipImmediately: true)) return;
            ApplyOwnedUpgrades();
        }

        public bool IsWeaponAmmoFull(WeaponController weaponPrefab)
        {
            WeaponController owned = weapons != null && weaponPrefab != null ? weapons.HasWeapon(weaponPrefab) : null;
            if (owned == null) return true;
            return owned.HasPhysicalBullets
                ? owned.GetCarriedPhysicalBullets() >= owned.AmmoCapacity
                : owned.GetCurrentAmmo() >= owned.AmmoCapacity;
        }

        public void RequestAmmoRefill(string purchaseKey)
        {
            if (!IsOwner || string.IsNullOrWhiteSpace(purchaseKey) || pendingRefillKeys.Contains(purchaseKey))
                return;

            int price = Mathf.Max(1, WeaponShopTerminal.GetPrice(purchaseKey) / 2);
            if (CurrentPoints < price) return;
            pendingRefillKeys.Add(purchaseKey);
            if (IsSpawned)
                RequestAmmoRefillRpc(new FixedString64Bytes(purchaseKey));
            else
            {
                offlinePoints -= price;
                CompleteAmmoRefill(purchaseKey, true);
                OnPointsChanged(offlinePoints + price, offlinePoints);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestAmmoRefillRpc(FixedString64Bytes requestedKey)
        {
            string key = requestedKey.ToString();
            int price = Mathf.Max(1, WeaponShopTerminal.GetPrice(key) / 2);
            // Refills reuse the purchase ledger: only a weapon already sold to this
            // client is eligible, since the server cannot see their local inventory.
            bool approved = WeaponShopTerminal.ValidatePurchase(this,key,ref price,true) && price > 0 && Points.Value >= price && serverPurchasedKeys.Contains(key);
            if (approved) Points.Value -= price;
            AmmoRefillResultRpc(requestedKey, approved);
        }

        [Rpc(SendTo.Everyone)]
        void AmmoRefillResultRpc(FixedString64Bytes purchasedKey, bool approved)
        {
            if (!IsOwner) return;
            CompleteAmmoRefill(purchasedKey.ToString(), approved);
        }

        void CompleteAmmoRefill(string key, bool approved)
        {
            pendingRefillKeys.Remove(key);
            if (!approved || weapons == null || !WeaponShopTerminal.TryResolveWeapon(key, out WeaponController weaponPrefab))
                return;
            WeaponController owned = weapons.HasWeapon(weaponPrefab);
            owned?.AddCarriablePhysicalBullets(owned.AmmoCapacity);
        }

        void OnPointsChanged(int previous, int current)
        {
            if (!IsOwner && IsSpawned) return;
            EnsurePointsHud();
            if (pointsText != null) pointsText.text = $"$ {current}";
        }

        void SyncHealth(float _)
        {
            if (IsServer && health != null) SyncedHealth.Value = health.CurrentHealth;
        }

        void SyncHealth(float _, GameObject source) => SyncHealth(0f);

        void OnSyncedHealthChanged(float previous, float current)
        {
            if (!IsServer && health != null)
            {
                health.CurrentHealth = current;
                // Direct NetworkVariable assignment does not raise Health.OnDamaged on the
                // owning client. Play it here, then the existing sound relay informs others.
                if (IsOwner && current < previous)
                    playerAudio?.PlayDamage(false);
            }
        }

        void EnsurePointsHud()
        {
            if (pointsCanvas != null) return;
            pointsCanvas = RuntimeMenuUI.CreateCanvas("Player Points HUD", 850);
            DontDestroyOnLoad(pointsCanvas.gameObject);
            RectTransform panel = RuntimeMenuUI.Block("Points", pointsCanvas.transform,
                new Color(.025f, .04f, .055f, .86f));
            panel.anchorMin = new Vector2(.43f, .925f);
            panel.anchorMax = new Vector2(.57f, .98f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            pointsText = RuntimeMenuUI.Label("Value", panel, $"$ {CurrentPoints}", 22,
                TextAnchor.MiddleCenter, new Color(.95f, .82f, .2f));
            RuntimeMenuUI.Stretch(pointsText.rectTransform, 8, 8, 2, 2);
        }
        void Update()
        {
            if (IsServer && IsDowned.Value) TickReviveProgress();

            if (IsOwner && IsReady.Value && !IsDowned.Value)
            {
                WeaponController activeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
                UpdateOwnerChainsawState(activeWeapon);
                bool meleeMode = activeWeapon != null && activeWeapon.IsMeleeWeapon;
                UpdateSpecialMeleeInput(activeWeapon,
                    meleeMode && IsSwordWeapon(activeWeapon) &&
                    activeWeapon.GetComponent<ChainsawWeapon>() == null);
                UpdateGuardTakedown();
                UpdateReviveInteraction();

                if (Keyboard.current != null && Time.time >= nextAbilityTime &&
                         SelectedClass.Value == PlayerArchetype.Medic && Keyboard.current.qKey.wasPressedThisFrame)
                { HealAuraRpc(); nextAbilityTime = Time.time + healAuraDuration + healCooldown; }

                if (SelectedClass.Value == PlayerArchetype.Medic) UpdateHealAuraHud();
                else if (healAuraStatusCanvas != null) healAuraStatusCanvas.gameObject.SetActive(false);
            }
            else
            {
                HideTakedownPrompt();
                HideRevivePrompt();
                if (healAuraStatusCanvas != null) healAuraStatusCanvas.gameObject.SetActive(false);
            }

            if (IsOwner && IsDowned.Value) UpdateDownedOwnerHud();
            else if (downedStatusCanvas != null) downedStatusCanvas.gameObject.SetActive(false);

            UpdateReloadPresentation();
            if (IsServer && RoundStarted.Value && !IsDowned.Value)
            {
                ArchetypeTuning tuning = Get(SelectedClass.Value);
                if (tuning.MaxArmor > 0 && Armor.Value < tuning.MaxArmor && Time.time >= lastArmorDamageTime + tuning.ArmorRechargeDelay)
                    Armor.Value = Mathf.MoveTowards(Armor.Value, tuning.MaxArmor, tuning.ArmorRechargePerSecond * Time.deltaTime);
            }
        }

        void UpdateGuardTakedown()
        {
            nearbyTakedownGuard = FindClosestTakedownGuard();
            if (nearbyTakedownGuard == null)
            {
                HideTakedownPrompt();
                return;
            }

            ShowTakedownPrompt(GameLocalization.Text("F  TAKE DOWN", "F  UITSCHAKELEN"));
            if (Keyboard.current == null || !Unity.FPS.Game.GameplayInteraction.Pressed) return;

            WeaponController activeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            bool usesSword = activeWeapon != null && activeWeapon.IsMeleeWeapon &&
                             activeWeapon.GetComponent<ChainsawWeapon>() == null &&
                             (activeWeapon.WeaponName?.IndexOf("Sword",
                                 StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
            if (usesSword)
                firstPersonHands?.PlaySwordTakedown(activeWeapon.WeaponRoot);

            NetworkObject guardObject = nearbyTakedownGuard.GetComponent<NetworkObject>();
            if (IsSpawned && guardObject != null && guardObject.IsSpawned)
                RequestGuardTakedownRpc(new NetworkObjectReference(guardObject));
            else
                nearbyTakedownGuard.TryTakedown(gameObject);
            nearbyTakedownGuard = null;
            HideTakedownPrompt();
        }

        OutpostGuardAI FindClosestTakedownGuard()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, guardTakedownRange,
                takedownHits, -1, QueryTriggerInteraction.Ignore);
            OutpostGuardAI closest = null;
            float closestSqr = guardTakedownRange * guardTakedownRange;
            for (int i = 0; i < count; i++)
            {
                OutpostGuardAI guard = takedownHits[i].GetComponentInParent<OutpostGuardAI>();
                if (guard == null || !guard.CanBeTakenDown) continue;
                float sqr = (guard.transform.position - transform.position).sqrMagnitude;
                if (sqr >= closestSqr) continue;
                closest = guard;
                closestSqr = sqr;
            }
            return closest;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestGuardTakedownRpc(NetworkObjectReference guardReference)
        {
            if (!guardReference.TryGet(out NetworkObject guardObject)) return;
            OutpostGuardAI guard = guardObject.GetComponent<OutpostGuardAI>();
            guard?.TryTakedown(gameObject);
        }

        void ShowTakedownPrompt(string message)
        {
            if (takedownPromptCanvas == null)
            {
                takedownPromptCanvas = RuntimeMenuUI.CreateCanvas("Guard Takedown Prompt", 860);
                RectTransform panel = RuntimeMenuUI.Block("Prompt", takedownPromptCanvas.transform,
                    new Color(.025f, .04f, .055f, .9f));
                panel.anchorMin = new Vector2(.28f, .115f);
                panel.anchorMax = new Vector2(.72f, .175f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                takedownPromptText = RuntimeMenuUI.Label("Text", panel, string.Empty, 18,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(takedownPromptText.rectTransform, 12, 12, 4, 4);
            }
            takedownPromptCanvas.gameObject.SetActive(true);
            takedownPromptText.text = message;
        }

        void HideTakedownPrompt()
        {
            nearbyTakedownGuard = null;
            if (takedownPromptCanvas != null) takedownPromptCanvas.gameObject.SetActive(false);
        }
        [Rpc(SendTo.Server)] public void SelectClassRpc(PlayerArchetype requested)
        {
            if ((byte)requested > (byte)PlayerArchetype.Medic) return;
            SelectedClass.Value = requested;
            ApplyServerClass(requested);
            StandingHeight.Value = DetermineZombieHeight();
            AssignSafeSpawn();
            IsReady.Value = true;
            // A late joiner shouldn't wait for a second host click once the round is already running.
            if (serverSessionRoundStarted) RoundStarted.Value = true;
            ConfirmClassRpc(requested, StandingHeight.Value, SpawnPosition.Value, SpawnRotation.Value,
                RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
            NetworkRoundGate.Invalidate();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SubmitProjectileHitRpc(NetworkObjectReference targetReference, Vector3 shotOrigin,
            Vector3 hitPoint, float reportedDamage, int penetrationIndex)
        {
            if (IsRepairingDefense || IsUsingMountedGun || !IsReady.Value || !RoundStarted.Value || IsDowned.Value ||
                !IsFinite(shotOrigin) || !IsFinite(hitPoint) || reportedDamage <= 0f ||
                !targetReference.TryGet(out NetworkObject target) || target == null)
                return;

            if (Vector3.SqrMagnitude(target.transform.position - transform.position) > 22500f) return;
            if (Vector3.SqrMagnitude(shotOrigin - (transform.position + Vector3.up)) > 16f) return;

            string weaponKey = EquippedWeaponKey.Value.ToString();
            if (IsMeleeEquipped.Value ||
                !TryResolveNetworkWeapon(weaponKey, out WeaponController weapon) ||
                weapon == null || weapon.IsMeleeWeapon || weapon.ProjectilePrefab == null)
                return;

            ProjectileStandard projectile = weapon.ProjectilePrefab.GetComponent<ProjectileStandard>();
            if (projectile == null || projectile.AreaOfDamage != null || penetrationIndex < 0 ||
                penetrationIndex > projectile.MaxEnemyPenetrations ||
                !TryConsumeProjectileHitBudget(weaponKey, weapon, projectile)) return;

            if (!TryResolveServerHit(target, shotOrigin, hitPoint, penetrationIndex,
                    projectile.StopPenetrationOnHeavyMonsters, projectile.HeavyMonsterHealthThreshold,
                    out Damageable damageable)) return;

            float damage = Mathf.Max(0f, projectile.Damage) * UpgradeDamage(weaponKey);
            if (damage <= 0f || reportedDamage > damage + .1f) return;

            bool zombieHeadshot = damageable.GetComponent<ZombieHeadHitbox>() != null;
            bool preventsLethalHeadshot = damageable.GetComponentInParent<NonLethalZombieHeadshot>() != null;
            if (projectile.AlwaysLethalZombieHeadshots && zombieHeadshot && !preventsLethalHeadshot &&
                damageable.Health != null)
            {
                float multiplier = Mathf.Max(.01f, damageable.DamageMultiplier);
                damage = Mathf.Max(damage, damageable.Health.CurrentHealth / multiplier + 1f);
            }

            float healthBefore = damageable.Health != null ? damageable.Health.CurrentHealth : 0f;
            damageable.InflictDamage(damage, false, gameObject);
            float damageDealt = damageable.Health != null
                ? Mathf.Max(0f, healthBefore - damageable.Health.CurrentHealth)
                : damage * damageable.DamageMultiplier;
            bool lethal = damageable.Health != null && damageable.Health.CurrentHealth <= 0f;
            if (damageDealt > 0f)
            {
                ProjectileHitFeedbackRpc(damageDealt, zombieHeadshot, lethal, penetrationIndex,
                    projectile.MaxEnemyPenetrations > 0,
                    RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ProjectileHitFeedbackRpc(float damageDealt, bool headshot, bool lethal, int penetrationIndex,
            bool penetrationWeapon, RpcParams rpcParams = default)
        {
            if (!IsOwner || damageDealt <= 0f) return;
            DamageEvent evt = Events.DamageEvent;
            evt.Sender = gameObject;
            evt.DamageValue = damageDealt;
            evt.IsHeadshot = headshot;
            evt.IsLethal = lethal;
            EventManager.Broadcast(evt);

            if (!penetrationWeapon) return;
            PenetrationHitEvent penetrationEvt = Events.PenetrationHitEvent;
            penetrationEvt.Sender = gameObject;
            penetrationEvt.HitNumber = penetrationIndex + 1;
            penetrationEvt.IsLethal = lethal;
            EventManager.Broadcast(penetrationEvt);
        }

        bool TryConsumeProjectileHitBudget(string weaponKey, WeaponController weapon, ProjectileStandard projectile)
        {
            bool sameBatch = Time.time <= serverProjectileBatchUntil &&
                             serverProjectileWeaponKey.Equals(new FixedString64Bytes(weaponKey));
            if (!sameBatch)
            {
                if (Time.time < serverNextProjectileBatchTime) return false;

                serverProjectileWeaponKey = new FixedString64Bytes(weaponKey);
                serverProjectileHitsRemaining = Mathf.Max(1, weapon.BulletsPerShot) *
                                                (Mathf.Max(0, projectile.MaxEnemyPenetrations) + 1);
                serverProjectileBatchUntil = Time.time + .15f + Mathf.Max(0, projectile.MaxEnemyPenetrations) * .2f;
                serverNextProjectileBatchTime = Time.time + Mathf.Max(.035f, weapon.DelayBetweenShots * UpgradeDelay(weaponKey) * .75f);
            }

            if (serverProjectileHitsRemaining <= 0) return false;
            serverProjectileHitsRemaining--;
            return true;
        }

        bool TryResolveServerHit(NetworkObject target, Vector3 shotOrigin, Vector3 hitPoint,
            int allowedEnemyPenetrations,
            bool stopOnHeavyMonsters, float heavyMonsterHealthThreshold,
            out Damageable damageable)
        {
            damageable = null;
            Collider bestCollider = null;
            float bestDistanceSqr = 2.25f;
            foreach (Collider candidate in target.GetComponentsInChildren<Collider>())
            {
                if (candidate == null || !candidate.enabled) continue;
                float distanceSqr = (candidate.ClosestPoint(hitPoint) - hitPoint).sqrMagnitude;
                Damageable candidateDamageable = candidate.GetComponent<Damageable>();
                if (candidateDamageable == null) continue;
                bool isEquallyClose = Mathf.Abs(distanceSqr - bestDistanceSqr) <= 0.0001f;
                if (distanceSqr > bestDistanceSqr + 0.0001f ||
                    (isEquallyClose && damageable != null &&
                     candidateDamageable.DamageMultiplier <= damageable.DamageMultiplier))
                {
                    continue;
                }
                bestDistanceSqr = distanceSqr;
                bestCollider = candidate;
                damageable = candidateDamageable;
            }

            if (bestCollider == null || damageable == null) return false;

            Vector3 displacement = hitPoint - shotOrigin;
            float distance = displacement.magnitude;
            if (distance <= .01f || distance > 150f) return false;

            RaycastHit[] hits = Physics.RaycastAll(shotOrigin, displacement / distance, distance + 1.5f,
                -1, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            HashSet<NetworkObject> penetratedEnemies = new();
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (ProjectilePassThroughVolume.Contains(hit.point)) continue;
                if (hit.collider.GetComponent<IgnoreHitDetection>() != null) continue;
                if (hit.collider.isTrigger && hit.collider.GetComponent<Damageable>() == null) continue;

                NetworkObject hitNetworkObject = hit.collider.GetComponentInParent<NetworkObject>();
                if (hitNetworkObject == target) return true;
                if (hitNetworkObject != null && IsPenetrableMonster(hitNetworkObject) &&
                    (!stopOnHeavyMonsters || !IsHeavyMonster(hitNetworkObject, heavyMonsterHealthThreshold)) &&
                    (penetratedEnemies.Contains(hitNetworkObject) ||
                     penetratedEnemies.Count < allowedEnemyPenetrations))
                {
                    penetratedEnemies.Add(hitNetworkObject);
                    continue;
                }
                return false;
            }

            return false;
        }

        static bool IsPenetrableMonster(NetworkObject target) =>
            target != null && (target.GetComponent<ZombieAI>() != null ||
                               target.GetComponent<ZombieTown.Enemies.FlyingRangedMonster>() != null);

        static bool IsHeavyMonster(NetworkObject target, float healthThreshold)
        {
            Health targetHealth = target != null ? target.GetComponent<Health>() : null;
            return targetHealth != null && targetHealth.MaxHealth >= Mathf.Max(1f, healthThreshold);
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        void RequestNetworkProjectileHit(ProjectileHitRequest request)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (request == null || manager == null || !manager.IsListening || manager.IsServer) return;
            NetworkObject target = request.Collider != null
                ? request.Collider.GetComponentInParent<NetworkObject>() : null;
            if (target == null) return;
            request.WasRelayed = true;
            SubmitProjectileHitRpc(target, request.ShotOrigin, request.HitPoint, request.Damage,
                request.PenetrationIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartRoundRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId || !IsReady.Value) return;

            // Release whichever players are already ready now; a single not-yet-ready
            // or stale object must never block everyone else from starting.
            serverSessionRoundStarted = true;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>(FindObjectsInactive.Include))
                if (player != null && player.IsSpawned && player.IsReady.Value)
                    player.RoundStarted.Value = true;
            NetworkRoundGate.Invalidate();
        }

        public void RequestForkliftControl()
        {
            if (!IsOwner) return;
            RequestForkliftControlRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestForkliftControlRpc()
        {
            ForkliftDriver.TryAssignClosestOnServer(this, OwnerClientId);
        }

        public void SubmitForkliftInput(float steering, float acceleration, float handbrake, float forkInput)
        {
            if (!IsOwner) return;
            SubmitForkliftInputRpc(steering, acceleration, handbrake, forkInput);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void SubmitForkliftInputRpc(float steering, float acceleration, float handbrake, float forkInput)
        {
            ForkliftDriver.ApplyInputOnServer(OwnerClientId, steering, acceleration, handbrake, forkInput);
        }

        public void RequestForkliftExit()
        {
            if (!IsOwner) return;
            RequestForkliftExitRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestForkliftExitRpc()
        {
            ForkliftDriver.ExitOnServer(OwnerClientId);
        }
        [Rpc(SendTo.SpecifiedInParams)]
        void ConfirmClassRpc(PlayerArchetype confirmedClass, float standingHeight, Vector3 position, Quaternion rotation, RpcParams rpcParams = default)
        {
            if (!IsOwner) return;
            PrepareOwnerClass(confirmedClass, standingHeight);
            ApplyOwnerSpawnAndRelease(position, rotation);
            StartCoroutine(ReplaceOwnerLoadoutNextFrame(confirmedClass));
            ClassConfirmed?.Invoke();
        }
        public bool TryMeleeAttack(float baseDamage)
        {
            if (!IsOwner || !IsReady.Value || Time.time < nextMeleeAttackTime) return false;
            WeaponController activeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            if (activeWeapon == null || !activeWeapon.IsMeleeWeapon) return false;

            int predictedAttack = localComboIndex;
            localComboIndex = (localComboIndex + 1) % 3;
            firstPersonHands?.PlayMeleeSwing(predictedAttack, activeWeapon.WeaponRoot);
            PlayMeleeSwingSfx(false, false);
            MeleeRpc(transform.position + Vector3.up, transform.forward, Mathf.Clamp(baseDamage, 1f, 45f));
            nextMeleeAttackTime = Time.time + (predictedAttack == 2 ? .62f : .5f);
            return true;
        }

        public bool TryContinuousMeleeAttack(float damagePerTick, float range)
        {
            if (!IsOwner || !IsReady.Value) return false;
            WeaponController activeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            if (activeWeapon == null || activeWeapon.GetComponent<ChainsawWeapon>() == null) return false;
            ChainsawDamageRpc(transform.position + Vector3.up, transform.forward,
                Mathf.Clamp(damagePerTick, 1f, 14f), Mathf.Clamp(range, 1f, 2.8f));
            return true;
        }

        [Rpc(SendTo.Server)]
        void ChainsawDamageRpc(Vector3 origin, Vector3 direction, float requestedDamage, float requestedRange)
        {
            if (IsRepairingDefense || IsUsingMountedGun) return;
            WeaponController activeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            if (activeWeapon == null || activeWeapon.GetComponent<ChainsawWeapon>() == null ||
                Time.time < serverNextChainsawTime)
                return;

            serverNextChainsawTime = Time.time + .1f;
            Vector3 authoritativeOrigin = transform.position + Vector3.up * 1.05f;
            if (Vector3.Distance(origin, authoritativeOrigin) > 1.5f) origin = authoritativeOrigin;
            direction.y = Mathf.Clamp(direction.y, -.3f, .3f);
            direction = direction.sqrMagnitude > .01f ? direction.normalized : transform.forward;
            if (Vector3.Dot(direction, transform.forward) < .5f) direction = transform.forward;

            float damage = Mathf.Clamp(requestedDamage, 1f, 18f);
            float range = Mathf.Clamp(requestedRange, 1f, 2.8f);
            RaycastHit[] hits = Physics.SphereCastAll(origin, .52f, direction, range, meleeLayers,
                QueryTriggerInteraction.Ignore);
            HashSet<Health> damagedTargets = new();
            float totalDamage = 0f;
            bool lethal = false;
            int killCount = 0;
            foreach (RaycastHit hit in hits)
            {
                Damageable damageable = hit.collider.GetComponentInParent<Damageable>();
                Health targetHealth = damageable != null ? damageable.Health : null;
                if (targetHealth == null || targetHealth == health || !damagedTargets.Add(targetHealth)) continue;
                float before = targetHealth.CurrentHealth;
                ZombieAI zombie = hit.collider.GetComponentInParent<ZombieAI>();
                if (zombie != null && UnityEngine.Random.value < .5f)
                    zombie.PrepareChainsawDecapitation(direction, hit.point);
                // Regular zombies are the chainsaw's end-game power fantasy: one
                // confirmed contact is lethal. Boss/heavy variants retain their
                // larger health pool and receive the stronger per-tick damage.
                bool regularZombie = zombie != null && targetHealth.MaxHealth <= 200f;
                float appliedDamage = regularZombie
                    ? before / Mathf.Max(.01f, damageable.DamageMultiplier) + 1f
                    : damage;
                damageable.InflictDamage(appliedDamage, false, gameObject);
                totalDamage += Mathf.Max(0f, before - targetHealth.CurrentHealth);
                bool justKilled = before > 0f && targetHealth.CurrentHealth <= 0f;
                lethal |= justKilled;
                if (justKilled) killCount++;
            }
            ChainsawDamageFeedbackRpc(totalDamage, lethal, killCount);
        }

        [Rpc(SendTo.Everyone)]
        void ChainsawDamageFeedbackRpc(float damageDealt, bool lethal, int killCount)
        {
            if (!IsOwner || damageDealt <= 0f) return;
            MeleeDamageEvent evt = Events.MeleeDamageEvent;
            evt.Sender = gameObject;
            evt.DamageValue = damageDealt;
            evt.IsLethal = lethal;
            evt.IsSpecial = false;
            evt.KillCount = killCount;
            EventManager.Broadcast(evt);
        }

        [Rpc(SendTo.Server)] public void MeleeRpc(Vector3 origin, Vector3 direction, float requestedDamage)
        {
            if (IsRepairingDefense || IsUsingMountedGun) return;
            ArchetypeTuning tuning = Get(SelectedClass.Value);
            float baseDamage = Mathf.Clamp(requestedDamage, 1f, 45f);
            if (SelectedClass.Value == PlayerArchetype.Striker)
                baseDamage = Mathf.Min(50f,
                    Mathf.Max(baseDamage, tuning.MeleeDamage) * regularMeleeDamageMultiplier);
            if (Time.time < serverNextMeleeTime) return;

            int combo = serverComboIndex;
            serverComboIndex = (serverComboIndex + 1) % 3;
            serverNextMeleeTime = Time.time + (combo == 2 ? .58f : .46f);

            Vector3 authoritativeOrigin = transform.position + Vector3.up * 1.05f;
            if (Vector3.Distance(origin, authoritativeOrigin) > 1.5f) origin = authoritativeOrigin;
            direction.y = Mathf.Clamp(direction.y, -.35f, .35f);
            direction = direction.sqrMagnitude > .01f ? direction.normalized : transform.forward;
            if (Vector3.Dot(direction, transform.forward) < .35f) direction = transform.forward;

            float range = meleeRange + (combo == 2 ? .25f : 0f);
            float radius = combo == 1 ? .7f : .62f;
            float damage = baseDamage * (combo == 1 ? .85f : combo == 2 ? 1.3f : 1f);
            WeaponController activeMeleeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            bool localSword = activeMeleeWeapon != null && activeMeleeWeapon.IsMeleeWeapon &&
                              activeMeleeWeapon.GetComponent<ChainsawWeapon>() == null &&
                              (activeMeleeWeapon.WeaponName?.IndexOf("Sword",
                                  StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
            // Remote players do not keep the owner's locally-instantiated inventory on
            // the server. The replicated key is the authoritative fallback there.
            bool replicatedSword = IsMeleeEquipped.Value &&
                                    EquippedWeaponKey.Value.ToString().IndexOf("Sword",
                                        StringComparison.OrdinalIgnoreCase) >= 0;
            bool swordEquipped = localSword || replicatedSword;
            RaycastHit[] hits = Physics.SphereCastAll(origin, radius, direction, range, meleeLayers, QueryTriggerInteraction.Ignore);
            Collider[] closeHits = Physics.OverlapSphere(origin, closeMeleeRadius, meleeLayers,
                QueryTriggerInteraction.Ignore);
            HashSet<Health> damagedTargets = new();
            float totalDamage = 0f;
            bool lethal = false;
            int killCount = 0;

            void DamageTarget(Collider targetCollider, bool requireForwardArc)
            {
                if (targetCollider == null) return;
                if (requireForwardArc)
                {
                    Vector3 flatDirection = direction;
                    flatDirection.y = 0f;
                    Vector3 toTarget = targetCollider.bounds.center - origin;
                    toTarget.y = 0f;
                    if (flatDirection.sqrMagnitude > .01f && toTarget.sqrMagnitude > .01f &&
                        Vector3.Dot(flatDirection.normalized, toTarget.normalized) < -.15f)
                        return;
                }

                Damageable damageable = targetCollider.GetComponentInParent<Damageable>();
                Health targetHealth = damageable != null ? damageable.Health : null;
                if (damageable == null || targetHealth == null || targetHealth == health || !damagedTargets.Add(targetHealth))
                    return;

                float healthBefore = targetHealth.CurrentHealth;
                float appliedDamage = damage;
                ZombieAI zombie = targetCollider.GetComponentInParent<ZombieAI>();
                bool swordHeadshot = swordEquipped && zombie != null &&
                                     UnityEngine.Random.value < swordHeadshotChance;
                ZombieHeadHitbox headHitbox = targetCollider.GetComponent<ZombieHeadHitbox>();
                if (swordHeadshot)
                {
                    headHitbox = zombie.GetComponentInChildren<ZombieHeadHitbox>(true);
                    Transform head = headHitbox != null ? headHitbox.transform : null;
                    Vector3 hitPoint = head != null ? head.position :
                        zombie.transform.position + Vector3.up * 1.5f;
                    appliedDamage *= Mathf.Max(1f, zombie.HeadshotMultiplier);
                    zombie.PrepareHeadshot(appliedDamage, head, direction, hitPoint);
                }
                else if (headHitbox != null)
                {
                    headHitbox.SetImpact(new Ray(targetCollider.bounds.center, direction));
                    headHitbox.RegisterHit(appliedDamage);
                }
                if (swordEquipped)
                    targetCollider.GetComponentInParent<OutpostGuardAI>()?.PrepareSilentMeleeDamage(gameObject);
                damageable.InflictDamage(appliedDamage, false, gameObject);
                float dealt = Mathf.Max(0f, healthBefore - targetHealth.CurrentHealth);
                totalDamage += dealt;
                bool justKilled = healthBefore > 0f && targetHealth.CurrentHealth <= 0f;
                lethal |= justKilled;
                if (justKilled) killCount++;
            }

            // SphereCast can miss colliders that already overlap its starting
            // sphere. Include a short forward arc so zombies pressed against the
            // player are still struck without turning light attacks into a 360 hit.
            // Resolve head colliders first. A body collider from the same zombie may
            // overlap the swing volume and would otherwise consume the one-hit slot.
            foreach (Collider closeHit in closeHits)
                if (closeHit.GetComponent<ZombieHeadHitbox>() != null) DamageTarget(closeHit, true);
            foreach (RaycastHit hit in hits)
                if (hit.collider.GetComponent<ZombieHeadHitbox>() != null) DamageTarget(hit.collider, false);
            foreach (Collider closeHit in closeHits) DamageTarget(closeHit, true);
            foreach (RaycastHit hit in hits) DamageTarget(hit.collider, false);

            PlayMeleeCosmeticRpc((byte)combo, totalDamage, lethal, killCount);
        }

        void UpdateSpecialMeleeInput(WeaponController activeWeapon, bool canCharge)
        {
            if (input == null) return;

            if (!canCharge || activeWeapon == null || !input.CanProcessInput() || input.RepairFireBlocked)
            {
                if (isChargingSpecial)
                {
                    firstPersonHands?.CancelSpecialMeleeCharge();
                    SetSpecialMeleeChargingRpc(false);
                }
                isChargingSpecial = false;
                HideSpecialChargeHud();
                return;
            }

            bool chargePressed = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            bool chargeHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;

            if (!isChargingSpecial && Time.time >= nextAbilityTime && chargePressed)
            {
                isChargingSpecial = true;
                specialChargeStarted = Time.time;
                firstPersonHands?.BeginSpecialMeleeCharge(activeWeapon.WeaponRoot, specialChargeDuration);
                SetSpecialMeleeChargingRpc(true);
                UpdateSpecialChargeHud(0f);
                return;
            }

            if (!isChargingSpecial) return;

            float chargeProgress = Mathf.Clamp01((Time.time - specialChargeStarted) /
                                                 Mathf.Max(.1f, specialChargeDuration));
            UpdateSpecialChargeHud(chargeProgress);
            if (chargeHeld) return;

            isChargingSpecial = false;
            SetSpecialMeleeChargingRpc(false);
            HideSpecialChargeHud();
            if (Time.time >= specialChargeStarted + specialChargeDuration)
            {
                firstPersonHands?.PlaySpecialMeleeSwing(activeWeapon.WeaponRoot);
                PlayMeleeSwingSfx(true, false);
                SpecialMeleeRpc();
                nextAbilityTime = Time.time + 1.2f;
                nextMeleeAttackTime = Mathf.Max(nextMeleeAttackTime, Time.time + .55f);
            }
            else
            {
                // Releasing RMB before the minimum charge explicitly cancels it.
                // LMB remains exclusively responsible for regular combo attacks.
                firstPersonHands?.CancelSpecialMeleeCharge();
            }
        }

        [Rpc(SendTo.Everyone)]
        void SetSpecialMeleeChargingRpc(bool charging)
        {
            if (IsOwner) return;
            spawnedWeaponHold?.SetSpecialMeleeCharging(charging);
        }

        [Rpc(SendTo.Server)]
        void SpecialMeleeRpc()
        {
            if (IsRepairingDefense || IsUsingMountedGun) return;
            WeaponController activeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            bool localSword = IsSwordWeapon(activeWeapon);
            bool replicatedSword = IsMeleeEquipped.Value &&
                                    EquippedWeaponKey.Value.ToString().IndexOf("Sword",
                                        StringComparison.OrdinalIgnoreCase) >= 0;
            if ((!localSword && !replicatedSword) || Time.time < serverNextSpecialTime)
                return;

            serverNextSpecialTime = Time.time + 1.2f;
            ArchetypeTuning tuning = Get(SelectedClass.Value);
            float damage = Mathf.Max(1f, tuning.MeleeDamage * specialDamageMultiplier);
            Vector3 origin = transform.position + Vector3.up;
            Collider[] hits = Physics.OverlapSphere(origin, specialRadius, meleeLayers,
                QueryTriggerInteraction.Ignore);
            HashSet<ZombieAI> pushedZombies = new();
            float totalDamage = 0f;
            bool lethal = false;
            int killCount = 0;

            foreach (Collider hit in hits)
            {
                ZombieAI zombie = hit.GetComponentInParent<ZombieAI>();
                if (zombie == null || !pushedZombies.Add(zombie)) continue;

                // Move first. If damage is lethal, ZombieAI enters its death state
                // immediately and would otherwise reject the escape-swing knockback.
                Vector3 away = zombie.transform.position - transform.position;
                away.y = 0f;
                zombie.ApplyKnockback(away, specialKnockbackDistance);

                Damageable damageable = zombie.GetComponent<Damageable>();
                Health targetHealth = damageable != null ? damageable.Health : null;
                if (targetHealth != null)
                {
                    float healthBefore = targetHealth.CurrentHealth;
                    damageable.InflictDamage(damage, false, gameObject);
                    totalDamage += Mathf.Max(0f, healthBefore - targetHealth.CurrentHealth);
                    bool justKilled = healthBefore > 0f && targetHealth.CurrentHealth <= 0f;
                    lethal |= justKilled;
                    if (justKilled) killCount++;
                }

            }

            PlaySpecialMeleeCosmeticRpc(totalDamage, lethal, killCount);
        }

        static bool IsSwordWeapon(WeaponController weapon)
        {
            if (weapon == null || !weapon.IsMeleeWeapon || weapon.GetComponent<ChainsawWeapon>() != null)
                return false;
            string weaponName = weapon.WeaponName ?? string.Empty;
            return weaponName.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   weaponName.IndexOf("Blade", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void UpdateSpecialChargeHud(float progress)
        {
            if (specialChargeCanvas == null)
            {
                specialChargeCanvas = RuntimeMenuUI.CreateCanvas("Sword Charge HUD", 858);
                RectTransform panel = RuntimeMenuUI.Block("Panel", specialChargeCanvas.transform,
                    new Color(.015f, .02f, .025f, .88f));
                panel.anchorMin = panel.anchorMax = new Vector2(.5f, .22f);
                panel.sizeDelta = new Vector2(210f, 30f);
                panel.anchoredPosition = Vector2.zero;

                GameObject fillObject = new("Charge Fill");
                fillObject.transform.SetParent(panel, false);
                RectTransform fillRect = fillObject.AddComponent<RectTransform>();
                RuntimeMenuUI.Stretch(fillRect, 4f, 4f, 4f, 4f);
                specialChargeFill = fillObject.AddComponent<Image>();
                specialChargeFill.color = new Color(.93f, .18f, .08f, .95f);
                specialChargeFill.type = Image.Type.Filled;
                specialChargeFill.fillMethod = Image.FillMethod.Horizontal;
                specialChargeFill.fillOrigin = 0;

                specialChargeText = RuntimeMenuUI.Label("Text", panel, string.Empty, 13,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(specialChargeText.rectTransform, 6f, 6f, 2f, 2f);
            }

            specialChargeCanvas.gameObject.SetActive(true);
            if (specialChargeFill != null)
            {
                float clampedProgress = Mathf.Clamp01(progress);
                specialChargeFill.fillAmount = clampedProgress;
                specialChargeFill.color = Color.Lerp(
                    new Color(.93f, .18f, .08f, .95f),
                    new Color(.25f, .92f, .32f, .98f), clampedProgress);
            }
            if (specialChargeText != null)
                specialChargeText.text = progress >= 1f
                    ? GameLocalization.Text("RELEASE RMB", "LAAT RMB LOS")
                    : GameLocalization.Text("CHARGING", "OPLADEN");
        }

        void HideSpecialChargeHud()
        {
            if (specialChargeCanvas != null) specialChargeCanvas.gameObject.SetActive(false);
        }

        [Rpc(SendTo.Everyone)]
        void PlaySpecialMeleeCosmeticRpc(float damageDealt, bool lethal, int killCount)
        {
            if (!IsOwner)
            {
                spawnedWeaponHold?.PlaySpecialMeleeSwing();
                PlayMeleeSwingSfx(true, true);
                return;
            }

            if (damageDealt <= 0f) return;
            MeleeDamageEvent evt = Events.MeleeDamageEvent;
            evt.Sender = gameObject;
            evt.DamageValue = damageDealt;
            evt.IsLethal = lethal;
            evt.IsSpecial = true;
            evt.KillCount = killCount;
            EventManager.Broadcast(evt);
        }
        [Rpc(SendTo.Everyone)]
        void PlayMeleeCosmeticRpc(byte combo, float damageDealt, bool lethal, int killCount)
        {
            if (!IsOwner)
            {
                spawnedWeaponHold?.PlayMeleeSwing(combo);
                PlayMeleeSwingSfx(false, true);
                return;
            }

            if (damageDealt > 0f)
            {
                MeleeDamageEvent evt = Events.MeleeDamageEvent;
                evt.Sender = gameObject;
                evt.DamageValue = damageDealt;
                evt.IsLethal = lethal;
                evt.IsSpecial = false;
                evt.KillCount = killCount;
                EventManager.Broadcast(evt);
            }
        }

        void PlayMeleeSwingSfx(bool special, bool spatial)
        {
            AudioClip clip = special && specialMeleeSwingSfx != null ? specialMeleeSwingSfx : meleeSwingSfx;
            if (clip == null) return;
            AudioUtility.CreateSFX(clip, transform.position, AudioUtility.AudioGroups.WeaponShoot,
                spatial ? 1f : 0f, 2f, special ? .42f : .24f, special ? .76f : 1.28f);
        }

        void OnLocalPlayerSound(PlayerAudio.PlayerSoundEvent soundEvent, int clipIndex, float pitch, float volume)
        {
            if (!IsOwner || !IsSpawned) return;
            RelayPlayerSoundRpc((byte)soundEvent, clipIndex, pitch, volume);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        void RelayPlayerSoundRpc(byte soundEvent, int clipIndex, float pitch, float volume)
        {
            if (!IsReady.Value || soundEvent > (byte)PlayerAudio.PlayerSoundEvent.Death) return;
            PlayPlayerSoundRpc(soundEvent, clipIndex, pitch, volume);
        }

        [Rpc(SendTo.Everyone, Delivery = RpcDelivery.Unreliable)]
        void PlayPlayerSoundRpc(byte soundEvent, int clipIndex, float pitch, float volume)
        {
            if (IsOwner) return;
            playerAudio?.PlayReplicated((PlayerAudio.PlayerSoundEvent)soundEvent, clipIndex, pitch, volume);
        }

        void UpdateReloadPresentation()
        {
            if (!IsOwner || !IsReady.Value) return;
            WeaponController activeWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            bool reloading = activeWeapon != null && activeWeapon.IsReloading;
            if (reloading && !wasReloading) RequestReloadCosmeticRpc();
            wasReloading = reloading;
        }

        [Rpc(SendTo.Server)]
        void RequestReloadCosmeticRpc()
        {
            if (Time.time < serverNextReloadCosmeticTime) return;
            serverNextReloadCosmeticTime = Time.time + .2f;
            PlayReloadCosmeticRpc();
        }

        [Rpc(SendTo.Everyone)]
        void PlayReloadCosmeticRpc()
        {
            if (!IsOwner) spawnedWeaponHold?.PlayReload();
        }
        [Rpc(SendTo.Server)] public void HealAuraRpc()
        {
            ArchetypeTuning tuning = Get(SelectedClass.Value);
            if (SelectedClass.Value != PlayerArchetype.Medic || tuning.HealAmount <= 0f ||
                HealAuraActive.Value || IsDowned.Value || Time.time < serverNextHealTime)
                return;

            serverNextHealTime = Time.time + healAuraDuration + healCooldown;
            HealAuraActive.Value = true;
            StartCoroutine(RunHealAura(tuning.HealAmount));
        }

        IEnumerator RunHealAura(float healPerSecond)
        {
            WaitForSeconds tickWait = new(HealAuraTickInterval);
            float elapsed = 0f;
            while (elapsed < healAuraDuration && HealAuraActive.Value)
            {
                foreach (PlayerClassController teammate in FindObjectsByType<PlayerClassController>())
                {
                    if (teammate == null || !teammate.IsReady.Value || teammate.IsDowned.Value ||
                        Vector3.Distance(transform.position, teammate.transform.position) > healRadius)
                        continue;
                    teammate.health?.Heal(healPerSecond * HealAuraTickInterval);
                }
                yield return tickWait;
                elapsed += HealAuraTickInterval;
            }
            HealAuraActive.Value = false;
        }

        void OnHealAuraActiveChanged(bool previous, bool current) => ApplyHealAuraVisual(current);

        void ApplyHealAuraVisual(bool active)
        {
            if (active)
            {
                if (healAuraVisual != null) Destroy(healAuraVisual);
                healAuraVisual = MedicHealAuraVfx.Create(transform, healRadius);
            }
            else if (healAuraVisual != null)
            {
                Destroy(healAuraVisual);
                healAuraVisual = null;
            }
        }

        void UpdateHealAuraHud()
        {
            if (!IsReady.Value)
            {
                if (healAuraStatusCanvas != null) healAuraStatusCanvas.gameObject.SetActive(false);
                return;
            }
            if (healAuraStatusCanvas == null)
            {
                healAuraStatusCanvas = RuntimeMenuUI.CreateCanvas("Heal Aura Status", 855);
                RectTransform panel = RuntimeMenuUI.Block("Panel", healAuraStatusCanvas.transform,
                    new Color(.02f, .05f, .03f, .85f));
                panel.anchorMin = new Vector2(.5f, .04f);
                panel.anchorMax = new Vector2(.5f, .04f);
                panel.sizeDelta = new Vector2(280, 34);
                panel.anchoredPosition = Vector2.zero;
                healAuraStatusText = RuntimeMenuUI.Label("Text", panel, string.Empty, 16,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(healAuraStatusText.rectTransform, 8, 8, 2, 2);
            }
            healAuraStatusCanvas.gameObject.SetActive(true);
            if (HealAuraActive.Value)
                healAuraStatusText.text = GameLocalization.Text("HEAL AURA ACTIVE", "HEAL AURA ACTIEF");
            else if (Time.time < nextAbilityTime)
                healAuraStatusText.text = GameLocalization.Text(
                    $"Heal Aura: {Mathf.CeilToInt(nextAbilityTime - Time.time)}s",
                    $"Heal Aura: {Mathf.CeilToInt(nextAbilityTime - Time.time)}s");
            else
                healAuraStatusText.text = GameLocalization.Text("Q  HEAL AURA READY", "Q  HEAL AURA KLAAR");
        }

        void UpdateReviveInteraction()
        {
            PlayerClassController target = FindNearbyDownedTeammate();
            if (target == null) { HideRevivePrompt(); return; }
            ShowRevivePrompt(GameLocalization.Text(
                $"HOLD F TO REVIVE  ({Mathf.RoundToInt(target.ReviveProgress.Value * 100f)}%)",
                $"HOUD E INGEDRUKT OM TE REVIVEN  ({Mathf.RoundToInt(target.ReviveProgress.Value * 100f)}%)"));
            if (Keyboard.current != null && Unity.FPS.Game.GameplayInteraction.Held)
                target.RequestReviveTickRpc();
        }

        PlayerClassController FindNearbyDownedTeammate()
        {
            PlayerClassController closest = null;
            float closestSqr = reviveRange * reviveRange;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
            {
                if (player == null || player == this || !player.IsDowned.Value) continue;
                float sqr = (player.transform.position - transform.position).sqrMagnitude;
                if (sqr >= closestSqr) continue;
                closest = player;
                closestSqr = sqr;
            }
            return closest;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone, Delivery = RpcDelivery.Unreliable)]
        public void RequestReviveTickRpc(RpcParams rpcParams = default)
        {
            if (!IsDowned.Value || NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client) ||
                client.PlayerObject == null ||
                Vector3.Distance(client.PlayerObject.transform.position, transform.position) > reviveRange + 1f)
                return;
            reviverClientId = rpcParams.Receive.SenderClientId;
            reviverLastPingTime = Time.time;
        }

        // Server-only, called each frame while this player is downed.
        void TickReviveProgress()
        {
            if (Time.time - reviverLastPingTime > .35f)
            {
                if (ReviveProgress.Value != 0f) ReviveProgress.Value = 0f;
                return;
            }
            PlayerClassController reviver = FindPlayerById(reviverClientId);
            float required = reviver != null && reviver.SelectedClass.Value == PlayerArchetype.Medic
                ? reviveHoldSecondsMedic : reviveHoldSeconds;
            ReviveProgress.Value = Mathf.Min(1f, ReviveProgress.Value + Time.deltaTime / required);
            if (ReviveProgress.Value >= 1f) CompleteRevive();
        }

        void CompleteRevive()
        {
            IsDowned.Value = false;
            ReviveProgress.Value = 0f;
            reviverClientId = NoReviver;
            if (health == null) return;
            health.Invincible = false;
            health.ReviveAndSetHealth(downedReviveHealth);
            SyncedHealth.Value = health.CurrentHealth;
        }

        static PlayerClassController FindPlayerById(ulong clientId)
        {
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
                return client.PlayerObject.GetComponent<PlayerClassController>();
            return null;
        }

        void OnDownedChanged(bool previous, bool current)
        {
            ApplyDownedPresentation(current);
            if (previous == current) return;
            int playerNumber = (int)OwnerClientId + 1;
            TeamEventFeed.Show(current
                ? GameLocalization.Text($"PLAYER {playerNumber} DOWNED", $"SPELER {playerNumber} NEERGEHAALD")
                : GameLocalization.Text($"PLAYER {playerNumber} REVIVED", $"SPELER {playerNumber} GEREVIVED"),
                current ? new Color(1f, .38f, .25f) : new Color(.35f, 1f, .58f));
        }

        void ApplyDownedPresentation(bool downed)
        {
            CharacterController capsule = GetComponent<CharacterController>();
            if (capsule != null) capsule.enabled = !downed;
            PlayerCharacterController movement = GetComponent<PlayerCharacterController>();
            if (movement != null) movement.CharacterVelocity = Vector3.zero;
            if (IsOwner)
            {
                input?.SetWeaponFireEnabled(!downed);
                WeaponController active = weapons != null ? weapons.GetActiveWeapon() : null;
                if (active != null) active.ShowWeapon(!downed);
                if (!downed) HideRevivePrompt();
            }
            if (!downed && downedStatusCanvas != null) downedStatusCanvas.gameObject.SetActive(false);
        }

        void UpdateDownedOwnerHud()
        {
            if (downedStatusCanvas == null)
            {
                downedStatusCanvas = RuntimeMenuUI.CreateCanvas("Downed Status", 862);
                RectTransform panel = RuntimeMenuUI.Block("Panel", downedStatusCanvas.transform,
                    new Color(.35f, .02f, .02f, .55f));
                panel.anchorMin = new Vector2(.25f, .82f);
                panel.anchorMax = new Vector2(.75f, .9f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                downedStatusText = RuntimeMenuUI.Label("Text", panel, string.Empty, 22,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(downedStatusText.rectTransform, 12, 12, 4, 4);
            }
            downedStatusCanvas.gameObject.SetActive(true);
            downedStatusText.text = GameLocalization.Text(
                $"DOWNED - WAIT FOR REVIVE  ({Mathf.RoundToInt(ReviveProgress.Value * 100f)}%)",
                $"NEERGEHAALD - WACHT OP REVIVE  ({Mathf.RoundToInt(ReviveProgress.Value * 100f)}%)");
        }

        void ShowRevivePrompt(string message)
        {
            if (revivePromptCanvas == null)
            {
                revivePromptCanvas = RuntimeMenuUI.CreateCanvas("Revive Prompt", 861);
                RectTransform panel = RuntimeMenuUI.Block("Prompt", revivePromptCanvas.transform,
                    new Color(.02f, .05f, .03f, .9f));
                panel.anchorMin = new Vector2(.26f, .19f);
                panel.anchorMax = new Vector2(.74f, .25f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                revivePromptText = RuntimeMenuUI.Label("Text", panel, string.Empty, 18,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(revivePromptText.rectTransform, 12, 12, 4, 4);
            }
            revivePromptCanvas.gameObject.SetActive(true);
            revivePromptText.text = message;
        }

        void HideRevivePrompt()
        {
            if (revivePromptCanvas != null) revivePromptCanvas.gameObject.SetActive(false);
        }
        public float AbsorbDamage(float incoming)
        {
            if (!IsServer || incoming <= 0 || Armor.Value <= 0) return incoming;
            float absorbed = Mathf.Min(incoming, Armor.Value);
            Armor.Value -= absorbed; lastArmorDamageTime = Time.time;
            return incoming - absorbed;
        }
        void ApplyServerClass(PlayerArchetype type)
        {
            EnsureGameplayServices();
            ArchetypeTuning tuning = Get(type);
            Armor.Value = tuning.MaxArmor;
            serverComboIndex = 0;
            if (weapons != null)
                weapons.StartingWeapons = tuning.StartingWeapon == null
                    ? new List<WeaponController>()
                    : new List<WeaponController> { tuning.StartingWeapon };
        }
        void OnClassChanged(PlayerArchetype previous, PlayerArchetype current)
        {
            ApplyPresentation(current);
            if (IsOwner)
            {
                ConfigureFirstPersonView(current);
            }
        }
        IEnumerator ReplaceOwnerLoadoutNextFrame(PlayerArchetype type)
        {
            yield return null;
            ArchetypeTuning tuning = Get(type);
            if (weapons == null) yield break;

            // PlayerWeaponsManager may already have initialized its serialized default.
            // Replace it deterministically, otherwise the class choice depends on RPC/Start timing.
            for (int i = 0; i < 9; i++)
            {
                WeaponController existing = weapons.GetWeaponAtSlotIndex(i);
                if (existing != null) weapons.RemoveWeapon(existing);
            }
            if (tuning.StartingWeapon == null) yield break;

            weapons.AddWeapon(tuning.StartingWeapon);
            WeaponController instance = weapons.HasWeapon(tuning.StartingWeapon);
            for (int i = 0; i < 9; i++)
                if (weapons.GetWeaponAtSlotIndex(i) == instance) { weapons.SwitchToWeaponIndex(i, true); break; }
        }
        void OnArmorChanged(float previous, float current)
        {
            // A hit absorbed entirely by armor never changes Health and therefore
            // never raises Health.OnDamaged. It still needs immediate owner feedback.
            // PlayerAudio's hurt cooldown collapses partial armor+health damage into
            // one sound instead of playing it twice.
            if (IsOwner && current < previous)
                playerAudio?.PlayDamage(false);
        }
        void OnHeightChanged(float previous, float current)
        {
            ApplyPlayerHeight(current);
            if (spawnedVisual != null) NormalizeVisualHeight(spawnedVisual, current);
        }
        void OnReadyChanged(bool previous, bool current)
        {
            if (IsOwner && !current)
                GetComponent<NetworkPlayerOwnership>()?.SetLocalGameplayReady(false);
            NetworkRoundGate.Invalidate();
        }

        void OnRoundStartedChanged(bool previous, bool current)
        {
            NetworkRoundGate.Invalidate();
            if (!current || !IsOwner) return;
            ApplyOwnerSpawnAndRelease();
            StartCoroutine(ReplaceOwnerLoadoutNextFrame(SelectedClass.Value));
        }

        void OnOwnerWeaponSwitched(WeaponController weapon)
        {
            ApplyOwnedUpgrades();
            if (!IsOwner) return;
            isChargingSpecial = false;
            HideSpecialChargeHud();
            input?.SetWeaponFireEnabled(true);
            bool melee = weapon != null && weapon.IsMeleeWeapon;
            firstPersonHands?.SetMeleeMode(melee);
            ObserveOwnerWeapon(melee ? null : weapon);
            ChainsawWeapon chainsaw = weapon != null ? weapon.GetComponent<ChainsawWeapon>() : null;
            lastOwnerChainsawPowered = chainsaw != null && chainsaw.IsPowered;
            SetEquippedWeaponRpc(new FixedString64Bytes(GetWeaponKey(weapon)), melee,
                lastOwnerChainsawPowered);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SetEquippedWeaponRpc(FixedString64Bytes key, bool melee, bool chainsawPowered)
        {
            string value = key.ToString();
            if (value.Length > 63) return;
            var definition = ZombieTown.Foundation.GameplaySceneContext.Active?.balance?.weapons?.Find(value);
            if (definition != null && !ServerOwnsDefinition(definition)) return;
            EquippedWeaponKey.Value = key;
            IsMeleeEquipped.Value = melee;
            ChainsawPowered.Value = melee && chainsawPowered;
        }

        void OnMeleeEquippedChanged(bool previous, bool current)
        {
            spawnedWeaponHold?.SetMeleeEquipped(current);
            RefreshThirdPersonHandProp();
        }

        void OnEquippedWeaponKeyChanged(FixedString64Bytes previous, FixedString64Bytes current) =>
            RefreshThirdPersonHandProp();

        void OnChainsawPoweredChanged(bool previous, bool current)
        {
            spawnedWeaponHold?.SetChainsawPowered(current);
            RefreshRemoteChainsawAudio(current);
        }

        void ObserveOwnerWeapon(WeaponController weapon)
        {
            if (observedOwnerWeapon != null)
                observedOwnerWeapon.OnShootProcessed -= OnOwnerGunshot;
            observedOwnerWeapon = weapon;
            if (observedOwnerWeapon != null)
                observedOwnerWeapon.OnShootProcessed += OnOwnerGunshot;
        }

        void OnOwnerGunshot()
        {
            if (!IsOwner) return;
            // WeaponController already reports the shot with its authored silencer state.
            // Reporting it again here made every suppressed shot audible to guards.
            if (IsSpawned) RequestGunshotCosmeticRpc();
        }

        void UpdateOwnerChainsawState(WeaponController activeWeapon)
        {
            ChainsawWeapon chainsaw = activeWeapon != null ? activeWeapon.GetComponent<ChainsawWeapon>() : null;
            bool powered = chainsaw != null && chainsaw.IsPowered;
            if (powered == lastOwnerChainsawPowered) return;
            lastOwnerChainsawPowered = powered;
            SetChainsawPoweredRpc(powered);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner,
            Delivery = RpcDelivery.Unreliable)]
        void SetChainsawPoweredRpc(bool powered)
        {
            if (IsMeleeEquipped.Value && EquippedWeaponKey.Value.ToString().IndexOf(
                    "Chainsaw", StringComparison.OrdinalIgnoreCase) >= 0)
                ChainsawPowered.Value = powered;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner,
            Delivery = RpcDelivery.Unreliable)]
        void RequestGunshotCosmeticRpc()
        {
            if (!IsReady.Value || IsMeleeEquipped.Value || Time.time < serverNextGunshotCosmeticTime)
                return;
            serverNextGunshotCosmeticTime = Time.time + .035f;
            PlayGunshotCosmeticRpc();
        }

        [Rpc(SendTo.Everyone, Delivery = RpcDelivery.Unreliable)]
        void PlayGunshotCosmeticRpc()
        {
            if (IsOwner) return;
            spawnedWeaponHold?.PlayShot();
            if (!TryResolveNetworkWeapon(EquippedWeaponKey.Value.ToString(), out WeaponController weapon))
                return;

            Vector3 muzzlePosition = transform.position + transform.up * 1.25f + transform.forward * .62f;
            if (spawnedHandProp != null)
                muzzlePosition = spawnedHandProp.transform.position + transform.forward * .45f;
            if (weapon.MuzzleFlashPrefab != null)
            {
                GameObject flash = Instantiate(weapon.MuzzleFlashPrefab, muzzlePosition,
                    Quaternion.LookRotation(transform.forward, transform.up));
                Destroy(flash, 2f);
            }
            if (weapon.ShootSfx != null)
                AudioUtility.CreateSFX(weapon.ShootSfx, muzzlePosition,
                    AudioUtility.AudioGroups.WeaponShoot, 1f, 2f, .82f, 1f);
        }

        void PrepareOwnerClass(PlayerArchetype type, float standingHeight)
        {
            ArchetypeTuning tuning = Get(type);
            if (weapons != null)
                weapons.StartingWeapons = tuning.StartingWeapon == null
                    ? new List<WeaponController>()
                    : new List<WeaponController> { tuning.StartingWeapon };
            localComboIndex = 0;
            wasReloading = false;
            ApplyPlayerHeight(standingHeight);
            ConfigureFirstPersonView(type);
            input?.SetWeaponFireEnabled(true);
        }

        void ApplyPlayerHeight(float standingHeight)
        {
            float height = Mathf.Clamp(standingHeight, 1.8f, 2.5f);
            PlayerCharacterController movement = GetComponent<PlayerCharacterController>();
            if (movement != null)
            {
                movement.CapsuleHeightStanding = height;
                movement.CapsuleHeightCrouching = height * .5f;
                movement.CameraHeightRatio = .92f;
            }
            CharacterController capsule = GetComponent<CharacterController>();
            if (capsule != null)
            {
                bool wasEnabled = capsule.enabled;
                capsule.enabled = false;
                capsule.height = height;
                capsule.center = Vector3.up * (height * .5f);
                capsule.enabled = wasEnabled;
            }
        }

        void ApplyOwnerSpawnAndRelease()
        {
            ApplyOwnerSpawnAndRelease(SpawnPosition.Value, SpawnRotation.Value);
        }

        void ApplyOwnerSpawnAndRelease(Vector3 position, Quaternion rotation)
        {
            CharacterController characterController = GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            PlayerCharacterController movement = GetComponent<PlayerCharacterController>();
            if (movement != null) movement.CharacterVelocity = Vector3.zero;
            if (characterController != null) characterController.enabled = true;
            GetComponent<NetworkPlayerOwnership>()?.SetLocalGameplayReady(!IsSpawned || RoundStarted.Value);
        }

        void AssignSafeSpawn()
        {
            ZombieTown.LevelTwo.LevelPlayerSpawn[] levelSpawns =
                FindObjectsByType<ZombieTown.LevelTwo.LevelPlayerSpawn>();
            if (levelSpawns.Length > 0)
            {
                Array.Sort(levelSpawns, (a, b) => string.CompareOrdinal(a.name, b.name));
                Transform point = levelSpawns[(int)(OwnerClientId % (ulong)levelSpawns.Length)].transform;
                SpawnPosition.Value = point.position + Vector3.up * .15f;
                SpawnRotation.Value = point.rotation;
                transform.SetPositionAndRotation(SpawnPosition.Value, SpawnRotation.Value);
                return;
            }

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Vector3 spawn = new(0f, 5f, 0f);
            if (triangulation.vertices != null && triangulation.vertices.Length > 0)
            {
                Vector3 center = Vector3.zero;
                foreach (Vector3 vertex in triangulation.vertices) center += vertex;
                center /= triangulation.vertices.Length;

                spawn = triangulation.vertices[0];
                float bestDistance = (spawn - center).sqrMagnitude;
                foreach (Vector3 vertex in triangulation.vertices)
                {
                    float distance = (vertex - center).sqrMagnitude;
                    if (distance < bestDistance) { spawn = vertex; bestDistance = distance; }
                }

                float angle = (OwnerClientId % 8) * Mathf.PI * .25f;
                Vector3 offset = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (NavMesh.SamplePosition(spawn + offset * 1.8f, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                    spawn = hit.position;
            }
            SpawnPosition.Value = spawn + Vector3.up * .15f;
            SpawnRotation.Value = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            // Keep the authoritative server copy out of the void while the owner
            // applies the same replicated pose and starts sending transforms.
            transform.SetPositionAndRotation(SpawnPosition.Value, SpawnRotation.Value);
        }

        static void EnsureGameplayServices()
        {
            if (FindAnyObjectByType<ActorsManager>() == null)
            {
                GameObject actors = new("ActorsManager (Runtime)");
                actors.AddComponent<ActorsManager>();
                DontDestroyOnLoad(actors);
            }
            if (FindAnyObjectByType<AudioManager>() == null)
            {
                GameObject audio = new("AudioManager (Runtime)");
                audio.AddComponent<AudioManager>();
                DontDestroyOnLoad(audio);
            }
        }
        void ApplyPresentation(PlayerArchetype type)
        {
            if (spawnedVisual != null) Destroy(spawnedVisual);
            ArchetypeTuning tuning = Get(type);
            if (tuning.CharacterPrefab == null || visualRoot == null) return;
            spawnedVisual = Instantiate(tuning.CharacterPrefab, visualRoot);
            spawnedVisual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            NormalizeVisualHeight(spawnedVisual, StandingHeight.Value);
            if (IsOwner) foreach (Renderer r in spawnedVisual.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            Animator source = spawnedVisual.GetComponentInChildren<Animator>();
            if (source != null)
            {
                source.applyRootMotion = false;
                if (locomotionController != null) source.runtimeAnimatorController = locomotionController;
                spawnedWeaponHold = source.gameObject.AddComponent<WeaponHoldIK>();
                spawnedWeaponHold.Initialize(source, transform, type, tuning.HandPropRotation);
            }
            Transform socket = rightHandSocket;
            if (source != null && source.isHuman) socket = source.GetBoneTransform(HumanBodyBones.RightHand) ?? socket;
            spawnedHandSocket = socket;
            RefreshThirdPersonHandProp();
        }

        void RefreshThirdPersonHandProp()
        {
            if (spawnedHandProp != null) Destroy(spawnedHandProp);
            spawnedHandProp = null;
            if (spawnedHandSocket == null) return;

            bool melee = IsMeleeEquipped.Value;
            ArchetypeTuning propTuning = melee ? Get(PlayerArchetype.Striker) : Get(SelectedClass.Value);
            string weaponKey = EquippedWeaponKey.Value.ToString();
            GameObject propPrefab = propTuning.HandPropPrefab;
            Vector3 propPosition = propTuning.HandPropPosition;
            Vector3 propRotation = propTuning.HandPropRotation;
            Vector3 propScale = propTuning.HandPropScale;
            bool exactWeaponVisual = TryResolveNetworkWeapon(weaponKey, out WeaponController weaponPrefab) &&
                                     weaponPrefab.WeaponRoot != null;
            if (exactWeaponVisual)
            {
                propPrefab = weaponPrefab.WeaponRoot;
                propPosition = Vector3.zero;
                propRotation = Vector3.zero;
                propScale = Vector3.one;
            }

            if (propPrefab != null)
            {
                spawnedHandProp = Instantiate(propPrefab, spawnedHandSocket);
                spawnedHandProp.name = string.IsNullOrWhiteSpace(weaponKey)
                    ? "Network Hand Prop" : $"Network Hand Prop ({weaponKey})";
                spawnedHandProp.SetActive(true);
                spawnedHandProp.transform.SetLocalPositionAndRotation(propPosition,
                    Quaternion.Euler(propRotation));
                spawnedHandProp.transform.localScale = propScale == Vector3.zero
                    ? Vector3.one : propScale;
                int presentationLayer = spawnedHandSocket.gameObject.layer;
                foreach (Transform child in spawnedHandProp.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = presentationLayer;
                foreach (Collider collider in spawnedHandProp.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (AudioSource source in spawnedHandProp.GetComponentsInChildren<AudioSource>(true))
                    source.enabled = false;
                float targetLength = GetNetworkPropLength(weaponKey, melee);
                NormalizePropLength(spawnedHandProp, targetLength);
                // The local player uses the FPS weapon. Showing this third-person prop
                // while its body is shadows-only creates the detached floating revolver.
                if (IsOwner)
                    foreach (Renderer renderer in spawnedHandProp.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                spawnedWeaponHold?.SetHandProp(spawnedHandProp.transform, propRotation);
            }
        }

        static float GetNetworkPropLength(string key, bool melee)
        {
            if (key.IndexOf("Chainsaw", StringComparison.OrdinalIgnoreCase) >= 0) return 1.15f;
            if (melee) return 1.35f;
            if (key.IndexOf("Pistol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Revolver", StringComparison.OrdinalIgnoreCase) >= 0) return .6f;
            if (key.IndexOf("SMG", StringComparison.OrdinalIgnoreCase) >= 0) return .78f;
            return .95f;
        }

        static string GetWeaponKey(WeaponController weapon)
        {
            if (weapon == null) return string.Empty;
            var definition = ZombieTown.Foundation.GameplaySceneContext.Active?.balance?.weapons?.Find(weapon);
            if (definition != null) return definition.weaponKey;
            return weapon.SourcePrefab != null ? weapon.SourcePrefab.name :
                weapon.gameObject.name.Replace("(Clone)", string.Empty).Trim();
        }

        bool TryResolveNetworkWeapon(string key, out WeaponController weapon)
        {
            weapon = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            var definition = ZombieTown.Foundation.GameplaySceneContext.Active?.balance?.weapons?.Find(key);
            if (definition != null) { weapon=definition.weaponPrefab; return weapon!=null; }
            if (WeaponShopTerminal.TryResolveWeapon(key, out weapon)) return true;
            if (archetypes == null) return false;
            foreach (ArchetypeTuning tuning in archetypes)
            {
                if (tuning.StartingWeapon != null &&
                    string.Equals(tuning.StartingWeapon.gameObject.name, key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    weapon = tuning.StartingWeapon;
                    return true;
                }
            }
            return false;
        }

        void RefreshRemoteChainsawAudio(bool powered)
        {
            if (IsOwner) return;
            if (!powered)
            {
                if (remoteChainsawSource != null) remoteChainsawSource.Stop();
                return;
            }
            if (!TryResolveNetworkWeapon(EquippedWeaponKey.Value.ToString(),
                    out WeaponController weapon)) return;
            ChainsawWeapon chainsaw = weapon.GetComponent<ChainsawWeapon>();
            if (chainsaw == null || chainsaw.EngineLoopClip == null) return;
            if (remoteChainsawSource == null)
            {
                remoteChainsawSource = gameObject.AddComponent<AudioSource>();
                remoteChainsawSource.playOnAwake = false;
                remoteChainsawSource.loop = true;
                remoteChainsawSource.spatialBlend = 1f;
                remoteChainsawSource.rolloffMode = AudioRolloffMode.Logarithmic;
                remoteChainsawSource.minDistance = 2f;
                remoteChainsawSource.maxDistance = 24f;
            }
            remoteChainsawSource.clip = chainsaw.EngineLoopClip;
            remoteChainsawSource.volume = chainsaw.EngineVolume;
            remoteChainsawSource.pitch = chainsaw.EnginePitch;
            if (!remoteChainsawSource.isPlaying) remoteChainsawSource.Play();
        }

        static void NormalizePropLength(GameObject prop, float targetLength)
        {
            Renderer[] renderers = prop.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > .001f) prop.transform.localScale *= targetLength / longest;
        }

        static void NormalizeVisualHeight(GameObject character, float targetHeight)
        {
            character.transform.localScale = Vector3.one;
            float characterHeight = MeasureHeight(character);
            if (characterHeight > .1f)
                character.transform.localScale = Vector3.one * Mathf.Clamp(targetHeight / characterHeight, .75f, 1.75f);
        }

        static float DetermineZombieHeight()
        {
            // Base controller height is 1.8; all network avatars must consistently be 1.2x.
            return 2.16f;
        }

        static float MeasureHeight(GameObject root)
        {
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return hasBounds ? bounds.size.y : 0f;
        }

        void ConfigureFirstPersonView(PlayerArchetype type)
        {
            if (weapons == null) return;
            PlayerCharacterController movement = GetComponent<PlayerCharacterController>();
            if (movement != null) movement.CameraHeightRatio = .92f;
            // Keep the authored Microgame weapon anchors intact. WeaponController and
            // PlayerWeaponsManager already use these sockets for recoil, aim and sway.
            firstPersonHands = FirstPersonHands.Ensure(weapons);
            firstPersonHands?.ApplyClass(type, firstPersonArmsPrefab, locomotionController,
                firstPersonArmsPosition, firstPersonArmsRotation, firstPersonArmsScale);
            firstPersonHands?.ApplyClassProp(type, Get(type).HandPropPrefab);
        }

        public override void OnDestroy()
        {
            if (health != null) health.OnDie -= OnPlayerDied;
            base.OnDestroy();
        }

        ArchetypeTuning Get(PlayerArchetype type)
        {
            foreach (ArchetypeTuning item in archetypes) if (item.Type == type) return item;
            return archetypes != null && archetypes.Length > 0 ? archetypes[0] : default;
        }

        public ArchetypeTuning GetArchetype(PlayerArchetype type) => Get(type);
        public RuntimeAnimatorController LocomotionController => locomotionController;
        public bool CanPickupAmmo
        {
            get
            {
                WeaponController active = weapons != null ? weapons.GetActiveWeapon() : null;
                return active != null && active.HasPhysicalBullets && !active.IsMeleeWeapon;
            }
        }
    }
}
