using System.Collections.Generic;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using ZombieTown.Enemies;
using ZombieTown.Multiplayer;

namespace ZombieTown.Survival
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class SequentialSurvivalMissionController : NetworkBehaviour
    {
        [SerializeField] SequentialSurvivalObjective objective;
        [SerializeField] List<Collider> defenseZones = new();
        [SerializeField] List<GameObject> objectiveMarkers = new();
        [SerializeField] List<string> siteNamesEnglish = new();
        [SerializeField] List<string> siteNamesDutch = new();
        [SerializeField] List<float> defenseDurations = new();
        [SerializeField] List<Transform> enemySpawnAnchors = new();
        [SerializeField] List<GameObject> zombiePrefabs = new();
        [SerializeField] GameObject heavyZombiePrefab;
        [SerializeField] GameObject flyingDemonPrefab;
        [Header("Special Waves")]
        [SerializeField] bool spawnOrcWaveAtEachSite;
        [SerializeField, Min(1)] int orcsPerPlayerAtSite = 1;
        [SerializeField, Range(.5f, 2f)] float orcHealthMultiplier = 1f;
        [SerializeField] bool spawnDemonWaves;
        [SerializeField, Min(1)] int demonsPerPlayerPerWave = 1;
        [SerializeField, Min(.25f)] float baseSpawnInterval = 1.35f;
        [SerializeField, Min(8)] int maximumLivingEnemies = 80;
        [SerializeField] bool scaleEnemyLimitByActivePlayers;
        [SerializeField, Min(4)] int soloMaximumLivingEnemies = 26;
        [SerializeField, Min(1)] int enemiesPerAdditionalPlayer = 10;
        [SerializeField, Min(0f)] float initialSpawnDelay = .5f;
        [SerializeField, Min(0f)] float spawnRampDuration;
        [SerializeField, Min(1f)] float initialSpawnIntervalMultiplier = 1f;
        [SerializeField] bool spreadSpawnsAcrossAnchors;
        [SerializeField] bool keepSpawningNearCurrentZone;
        [SerializeField, Min(8)] int nearbyLivingEnemyTarget = 36;
        [SerializeField, Min(16)] int hardMaximumLivingEnemies = 96;
        [SerializeField, Min(20f)] float nearbyEnemyRadius = 70f;
        [SerializeField] bool preferNearbySpawnAnchors;
        [SerializeField, Min(10f)] float preferredSpawnAnchorDistance = 42f;
        [SerializeField, Range(0, 10)] int demonsStartAtSite = 2;
        [SerializeField, Range(0, 16)] int maximumLivingDemons = 6;
        [SerializeField] bool scaleDemonLimitByActivePlayers = true;
        [SerializeField, Range(0, 8)] int soloMaximumLivingDemons = 2;
        [SerializeField, Range(0, 4)] int demonsPerAdditionalPlayer = 1;
        [SerializeField, Min(2f)] float demonSpawnInterval = 8f;
        [SerializeField, Range(.5f, 2f)] float baseZombieSpeedMultiplier = 1f;
        [SerializeField, Range(1f, 3f)] float finalZombieHealthMultiplier = 1.8f;
        [SerializeField, Range(1f, 3f)] float finalZombieSpeedMultiplier = 1.45f;

        public readonly NetworkVariable<int> CurrentSite = new();
        public readonly NetworkVariable<int> SecondsRemaining = new();
        public readonly NetworkVariable<bool> PlayersHolding = new();
        public readonly NetworkVariable<bool> MissionComplete = new();

        float secondsLeft;
        float missionElapsed;
        float nextSpawnTime;
        float nextDemonSpawnTime;
        double lastServerTime;
        bool initialized;
        bool localCompleted;
        readonly List<int> spawnAnchorOrder = new();
        int spawnAnchorCursor;
        int lastSpawnAnchorIndex = -1;

        void Start()
        {
            ApplyMarkerPresentation();
            RefreshObjective();
        }

        public override void OnNetworkSpawn()
        {
            CurrentSite.OnValueChanged += OnIntChanged;
            SecondsRemaining.OnValueChanged += OnIntChanged;
            PlayersHolding.OnValueChanged += OnBoolChanged;
            MissionComplete.OnValueChanged += OnBoolChanged;
            if (IsServer) InitializeAuthority();
            ApplyMarkerPresentation();
            RefreshObjective();
        }

        public override void OnNetworkDespawn()
        {
            CurrentSite.OnValueChanged -= OnIntChanged;
            SecondsRemaining.OnValueChanged -= OnIntChanged;
            PlayersHolding.OnValueChanged -= OnBoolChanged;
            MissionComplete.OnValueChanged -= OnBoolChanged;
        }

        void InitializeAuthority()
        {
            if (initialized) return;
            initialized = true;
            CurrentSite.Value = 0;
            MissionComplete.Value = false;
            BeginSite(0);
        }

        void Update()
        {
            if (!IsServer || MissionComplete.Value) return;
            // Waiting for ready players must not count as defense time or raise mission pressure.
            if (!NetworkRoundGate.IsOpen) { lastServerTime = NetworkManager.ServerTime.Time; return; }

            double now = NetworkManager.ServerTime.Time;
            float delta = lastServerTime > 0d ? Mathf.Max(0f, (float)(now - lastServerTime)) : 0f;
            lastServerTime = now;
            missionElapsed += delta;

            bool holding = AnyActivePlayerInsideCurrentZone();
            if (PlayersHolding.Value != holding) PlayersHolding.Value = holding;
            if (holding)
            {
                secondsLeft = Mathf.Max(0f, secondsLeft - delta);
                int rounded = Mathf.CeilToInt(secondsLeft);
                if (SecondsRemaining.Value != rounded) SecondsRemaining.Value = rounded;
                if (secondsLeft <= 0f) AdvanceSite();
            }

            if (Time.time >= nextSpawnTime && CanSpawnZombie())
                SpawnZombie();
            if (CurrentSite.Value >= demonsStartAtSite && Time.time >= nextDemonSpawnTime)
            {
                if (spawnDemonWaves) SpawnDemonWave();
                else if (CountLivingDemons() < GetCurrentDemonLimit()) SpawnDemon();
            }
        }

        void BeginSite(int index)
        {
            CurrentSite.Value = Mathf.Clamp(index, 0, Mathf.Max(0, defenseZones.Count - 1));
            secondsLeft = GetDuration(CurrentSite.Value);
            SecondsRemaining.Value = Mathf.CeilToInt(secondsLeft);
            PlayersHolding.Value = false;
            lastServerTime = NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;
            nextSpawnTime = Time.time + (missionElapsed <= .01f ? initialSpawnDelay : .5f);
            nextDemonSpawnTime = Time.time + (spawnDemonWaves ? demonSpawnInterval : 4f);
            if (spawnOrcWaveAtEachSite) SpawnOrcWave();
        }

        void AdvanceSite()
        {
            int next = CurrentSite.Value + 1;
            if (next >= defenseZones.Count)
            {
                SecondsRemaining.Value = 0;
                PlayersHolding.Value = false;
                MissionComplete.Value = true;
                return;
            }
            BeginSite(next);
        }

        void SpawnZombie()
        {
            float progress = GetProgress();
            float pressure = Mathf.Lerp(1f, 1.75f, Mathf.Clamp01(missionElapsed / 480f));
            float ramp = spawnRampDuration > 0f
                ? Mathf.Lerp(initialSpawnIntervalMultiplier, 1f, Mathf.Clamp01(missionElapsed / spawnRampDuration))
                : 1f;
            nextSpawnTime = Time.time + baseSpawnInterval * ramp / pressure * Random.Range(.72f, 1.2f);
            if (zombiePrefabs.Count == 0 || enemySpawnAnchors.Count == 0) return;

            bool useHeavy = !spawnOrcWaveAtEachSite && heavyZombiePrefab != null && CurrentSite.Value > 0 &&
                            Random.value < Mathf.Lerp(.05f, .28f, progress);
            GameObject prefab = useHeavy ? heavyZombiePrefab : zombiePrefabs[Random.Range(0, zombiePrefabs.Count)];
            Transform anchor = ChooseSpawnAnchor();
            if (prefab == null || anchor == null) return;
            Vector3 guess = anchor.position + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 12f, NavMesh.AllAreas)) return;

            GameObject instance = Instantiate(prefab, hit.position + Vector3.up * .05f, anchor.rotation);
            Health enemyHealth = instance.GetComponent<Health>();
            if (enemyHealth != null)
            {
                enemyHealth.MaxHealth *= Mathf.Lerp(1f, finalZombieHealthMultiplier, progress);
                enemyHealth.CurrentHealth = enemyHealth.MaxHealth;
            }
            ZombieAI zombie = instance.GetComponent<ZombieAI>();
            if (zombie != null)
            {
                zombie.MoveSpeed *= baseZombieSpeedMultiplier * Mathf.Lerp(1f, finalZombieSpeedMultiplier, progress);
                NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
                if (agent != null) agent.speed = zombie.MoveSpeed;
            }
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
            else Destroy(instance);
        }

        void SpawnDemon()
        {
            nextDemonSpawnTime = Time.time + demonSpawnInterval * Random.Range(.78f, 1.22f);
            if (flyingDemonPrefab == null || enemySpawnAnchors.Count == 0) return;
            Transform anchor = ChooseSpawnAnchor();
            if (anchor == null) return;
            Vector3 guess = anchor.position + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
            Vector3 position = NavMesh.SamplePosition(guess, out NavMeshHit hit, 12f, NavMesh.AllAreas)
                ? hit.position : anchor.position;
            GameObject instance = Instantiate(flyingDemonPrefab, position, anchor.rotation);
            instance.GetComponent<FlyingRangedMonster>()?.ConfigureFlying();
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
            else Destroy(instance);
        }

        void SpawnDemonWave()
        {
            nextDemonSpawnTime = Time.time + demonSpawnInterval;
            int count = GetActivePlayerCount() * Mathf.Max(1, demonsPerPlayerPerWave);
            for (int i = 0; i < count; i++) SpawnDemonInstance(i, count);
        }

        void SpawnOrcWave()
        {
            if (heavyZombiePrefab == null || enemySpawnAnchors.Count == 0) return;
            int count = GetActivePlayerCount() * Mathf.Max(1, orcsPerPlayerAtSite);
            for (int i = 0; i < count; i++)
            {
                Transform anchor = ChooseSpawnAnchor();
                if (anchor == null) continue;
                Vector3 guess = anchor.position + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
                if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 12f, NavMesh.AllAreas)) continue;
                GameObject instance = Instantiate(heavyZombiePrefab, hit.position + Vector3.up * .05f, anchor.rotation);
                Health health = instance.GetComponent<Health>();
                if (health != null)
                {
                    health.MaxHealth *= orcHealthMultiplier;
                    health.CurrentHealth = health.MaxHealth;
                }
                OrcCombatVariant variant = instance.GetComponent<OrcCombatVariant>();
                if (variant != null)
                {
                    OrcCombatVariant.VariantType[] types =
                    {
                        OrcCombatVariant.VariantType.Club,
                        OrcCombatVariant.VariantType.RockThrower,
                        OrcCombatVariant.VariantType.Brute
                    };
                    variant.Configure(types[i % types.Length]);
                }
                NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject != null) networkObject.Spawn(true);
                else Destroy(instance);
            }
        }

        void SpawnDemonInstance(int index, int count)
        {
            if (flyingDemonPrefab == null || enemySpawnAnchors.Count == 0) return;
            Transform anchor = ChooseSpawnAnchor();
            if (anchor == null) return;
            Vector3 guess = anchor.position + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
            Vector3 position = NavMesh.SamplePosition(guess, out NavMeshHit hit, 12f, NavMesh.AllAreas)
                ? hit.position : anchor.position;
            GameObject instance = Instantiate(flyingDemonPrefab, position, anchor.rotation);
            instance.GetComponent<FlyingRangedMonster>()?.ConfigureFlying();
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
            else Destroy(instance);
        }

        Transform ChooseSpawnAnchor()
        {
            if (enemySpawnAnchors.Count == 0) return null;
            if (spreadSpawnsAcrossAnchors)
            {
                for (int attempt = 0; attempt < enemySpawnAnchors.Count; attempt++)
                {
                    if (spawnAnchorOrder.Count != enemySpawnAnchors.Count || spawnAnchorCursor >= spawnAnchorOrder.Count)
                        RefillSpawnAnchorOrder();
                    int index = spawnAnchorOrder[spawnAnchorCursor++];
                    if (enemySpawnAnchors[index] == null) continue;
                    lastSpawnAnchorIndex = index;
                    return enemySpawnAnchors[index];
                }
            }
            Collider zone = CurrentSite.Value >= 0 && CurrentSite.Value < defenseZones.Count
                ? defenseZones[CurrentSite.Value] : null;
            Vector3 center = zone != null ? zone.bounds.center : Vector3.zero;
            Transform best = null;
            float bestScore = float.NegativeInfinity;
            for (int attempt = 0; attempt < Mathf.Min(8, enemySpawnAnchors.Count); attempt++)
            {
                Transform candidate = enemySpawnAnchors[Random.Range(0, enemySpawnAnchors.Count)];
                if (candidate == null) continue;
                float distance = Vector3.Distance(candidate.position, center);
                float score = preferNearbySpawnAnchors
                    ? -Mathf.Abs(distance - preferredSpawnAnchorDistance) * Random.Range(.85f, 1.15f)
                    : distance * distance * Random.Range(.7f, 1.3f);
                if (score <= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            return best != null ? best : enemySpawnAnchors[0];
        }

        void RefillSpawnAnchorOrder()
        {
            spawnAnchorOrder.Clear();
            for (int i = 0; i < enemySpawnAnchors.Count; i++) spawnAnchorOrder.Add(i);
            for (int i = spawnAnchorOrder.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                (spawnAnchorOrder[i], spawnAnchorOrder[swap]) = (spawnAnchorOrder[swap], spawnAnchorOrder[i]);
            }
            if (spawnAnchorOrder.Count > 1 && spawnAnchorOrder[0] == lastSpawnAnchorIndex)
                (spawnAnchorOrder[0], spawnAnchorOrder[1]) = (spawnAnchorOrder[1], spawnAnchorOrder[0]);
            spawnAnchorCursor = 0;
        }

        bool AnyActivePlayerInsideCurrentZone()
        {
            if (CurrentSite.Value < 0 || CurrentSite.Value >= defenseZones.Count ||
                defenseZones[CurrentSite.Value] == null) return true;
            Collider zone = defenseZones[CurrentSite.Value];
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
            {
                if (player == null || !player.IsSpawned || !player.IsReady.Value || player.IsDowned.Value) continue;
                Vector3 point = player.transform.position + Vector3.up;
                if ((zone.ClosestPoint(point) - point).sqrMagnitude <= .5f * .5f) return true;
            }
            return false;
        }

        float GetDuration(int index) => index >= 0 && index < defenseDurations.Count
            ? Mathf.Max(15f, defenseDurations[index]) : 60f;

        float GetProgress() => defenseZones.Count > 1
            ? Mathf.Clamp01(CurrentSite.Value / (float)(defenseZones.Count - 1)) : 1f;

        static int CountLivingEnemies()
        {
            int count = 0;
            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
            {
                Health health = zombie != null ? zombie.GetComponent<Health>() : null;
                if (health != null && health.CurrentHealth > 0f) count++;
            }
            return count;
        }

        bool CanSpawnZombie()
        {
            int living = CountLivingEnemies();
            int currentLimit = GetCurrentEnemyLimit();
            if (!keepSpawningNearCurrentZone) return living < currentLimit;
            if (living >= Mathf.Max(maximumLivingEnemies, hardMaximumLivingEnemies)) return false;
            if (CurrentSite.Value < 0 || CurrentSite.Value >= defenseZones.Count ||
                defenseZones[CurrentSite.Value] == null) return living < maximumLivingEnemies;

            Vector3 center = defenseZones[CurrentSite.Value].bounds.center;
            float radiusSqr = nearbyEnemyRadius * nearbyEnemyRadius;
            int nearby = 0;
            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
            {
                if (zombie == null || (zombie.transform.position - center).sqrMagnitude > radiusSqr) continue;
                Health health = zombie.GetComponent<Health>();
                if (health != null && health.CurrentHealth > 0f) nearby++;
            }
            return nearby < nearbyLivingEnemyTarget;
        }

        int GetCurrentEnemyLimit()
        {
            if (!scaleEnemyLimitByActivePlayers) return maximumLivingEnemies;
            int players = GetActivePlayerCount();
            return Mathf.Min(maximumLivingEnemies,
                soloMaximumLivingEnemies + (players - 1) * enemiesPerAdditionalPlayer);
        }

        int GetCurrentDemonLimit()
        {
            if (!scaleDemonLimitByActivePlayers) return maximumLivingDemons;
            int players = GetActivePlayerCount();
            return Mathf.Min(maximumLivingDemons,
                soloMaximumLivingDemons + (players - 1) * demonsPerAdditionalPlayer);
        }

        static int GetActivePlayerCount()
        {
            int players = 0;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
                if (player != null && player.IsSpawned && player.IsReady.Value && !player.IsDowned.Value) players++;
            return Mathf.Max(1, players);
        }

        static int CountLivingDemons()
        {
            int count = 0;
            foreach (FlyingRangedMonster demon in FindObjectsByType<FlyingRangedMonster>())
            {
                if (demon == null || !demon.IsFlyingConfigured) continue;
                Health health = demon.GetComponent<Health>();
                if (health == null || health.CurrentHealth > 0f) count++;
            }
            return count;
        }

        void OnIntChanged(int _, int __)
        {
            ApplyMarkerPresentation();
            RefreshObjective();
        }

        void OnBoolChanged(bool _, bool __)
        {
            ApplyMarkerPresentation();
            RefreshObjective();
        }

        void ApplyMarkerPresentation()
        {
            for (int i = 0; i < objectiveMarkers.Count; i++)
                if (objectiveMarkers[i] != null)
                    objectiveMarkers[i].SetActive(!MissionComplete.Value && i == CurrentSite.Value);
        }

        void RefreshObjective()
        {
            if (objective == null) return;
            if (MissionComplete.Value)
            {
                if (localCompleted) return;
                localCompleted = true;
                objective.CompleteObjective(string.Empty, string.Empty,
                    GameLocalization.Text("All positions secured", "Alle posities veiliggesteld"));
                return;
            }

            int index = Mathf.Clamp(CurrentSite.Value, 0, Mathf.Max(0, defenseZones.Count - 1));
            string english = index < siteNamesEnglish.Count ? siteNamesEnglish[index] : $"Position {index + 1}";
            string dutch = index < siteNamesDutch.Count ? siteNamesDutch[index] : $"Positie {index + 1}";
            string site = GameLocalization.Text(english, dutch);
            objective.UpdateObjective(
                PlayersHolding.Value
                    ? GameLocalization.Text($"Defend {site}", $"Verdedig {site}")
                    : GameLocalization.Text($"Reach {site}", $"Ga naar {site}"),
                $"{SecondsRemaining.Value / 60:00}:{SecondsRemaining.Value % 60:00}",
                PlayersHolding.Value
                    ? GameLocalization.Text("Stay inside the marked area", "Blijf in het gemarkeerde gebied")
                    : GameLocalization.Text("Follow the tall objective beacon", "Volg het hoge doelbaken"));
        }
    }
}
