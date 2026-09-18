using System;
using System.Collections.Generic;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ZombieTown.Enemies;

namespace ZombieTown.LevelTwo
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RadioOutpostMissionController : NetworkBehaviour
    {
        public enum MissionPhase : byte { Assault, ActivateRadio, Defend, Complete }

        [SerializeField] RadioOutpostObjective objective;
        [SerializeField] Transform radio;
        [SerializeField] AudioSource radioAudio;
        [SerializeField] Light radioLight;
        [SerializeField] RadioBeaconMarker beaconMarker;
        [SerializeField] AudioClip alarmAudioClip;
        [SerializeField, Min(.5f)] float alarmDelaySeconds = 2.5f;
        [SerializeField, Range(0f, 1f)] float alarmInitialVolume = 1f;
        [SerializeField, Range(0f, 1f)] float alarmSustainedVolume = .42f;
        [SerializeField, Min(.5f)] float alarmLoudDuration = 5f;
        [SerializeField, Min(.1f)] float alarmFadeDuration = 1.1f;
        [SerializeField, Min(.1f)] float alarmMinDistance = 3f;
        [SerializeField, Min(1f)] float alarmMaxDistance = 30f;
        [SerializeField] List<GameObject> zombiePrefabs = new();
        [SerializeField] GameObject heavyZombiePrefab;
        [SerializeField] bool spawnHeavyZombies;
        [SerializeField] GameObject flyingMonsterPrefab;
        [SerializeField] List<Transform> spawnPoints = new();
        [SerializeField, Min(10f)] float defenseDuration = 300f;
        [SerializeField, Min(.25f)] float spawnInterval = 2f;
        [SerializeField, Min(1)] int maximumLivingZombies = 50;
        [SerializeField, Min(1f)] float interactionDistance = 3.5f;
        [SerializeField, Range(0f, 1f)] float radioBeepVolume = .2f;
        [SerializeField, Min(.5f)] float radioBeepInterval = 2f;
        [SerializeField, Range(1.5f, 3f)] float heavyZombieScaleMultiplier = 1f;

        const float FlyingMonsterDelayAfterRadioSeconds = 25f;
        const float HeavyZombieDelayAfterRadioSeconds = 50f;

        public readonly NetworkVariable<byte> Phase = new((byte)MissionPhase.Assault);
        public readonly NetworkVariable<int> GuardsRemaining = new();
        public readonly NetworkVariable<int> DefenseSecondsRemaining = new();
        public readonly NetworkVariable<bool> RadioActive = new();

        readonly HashSet<OutpostGuardAI> remainingGuards = new();
        double defenseEndsAt;
        double flyingMonsterSpawnsAt = double.PositiveInfinity;
        double heavyZombieSpawnsAt = double.PositiveInfinity;
        float nextSpawn;
        int defensePlayerCount = 1;
        bool localCompleted;
        bool authorityInitialized;
        MissionPhase offlinePhase;
        int offlineGuardsRemaining;
        int offlineDefenseSeconds;
        bool offlineRadioActive;
        bool flyingMonstersSpawned;
        bool heavyZombieSpawned;
        bool finalWaveSpawned;
        Coroutine alarmRoutine;
        AudioSource alarmSource;
        float nextRadioBeep;
        float radioInteractionAvailableAt;
        Canvas radioPromptCanvas;
        Text radioPromptText;

        public MissionPhase CurrentPhase => IsSpawned ? (MissionPhase)Phase.Value : offlinePhase;
        public Transform RadioTransform => radio;
        public bool HasHeavyZombieConfigured => heavyZombiePrefab != null || zombiePrefabs.Exists(IsHeavyZombiePrefab);

        bool HasSimulationAuthority
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager == null || !manager.IsListening || IsServer;
            }
        }

        public void Configure(RadioOutpostObjective missionObjective, Transform radioTransform,
            AudioSource audio, Light indicator, IEnumerable<GameObject> zombies, IEnumerable<Transform> spawns)
        {
            objective = missionObjective;
            radio = radioTransform;
            radioAudio = audio;
            radioLight = indicator;
            zombiePrefabs = new List<GameObject>(zombies);
            spawnPoints = new List<Transform>(spawns);
        }

        public void AddZombiePrefab(GameObject prefab)
        {
            if (prefab == null) return;
            if (!zombiePrefabs.Contains(prefab)) zombiePrefabs.Add(prefab);
            if (IsHeavyZombiePrefab(prefab)) heavyZombiePrefab = prefab;
        }

        public void ConfigureFlyingMonsterPrefab(GameObject prefab) => flyingMonsterPrefab = prefab;

        void OnEnable() => GameLocalization.LanguageChanged += RefreshObjective;

        void OnDisable() => GameLocalization.LanguageChanged -= RefreshObjective;

        void Start()
        {
            radioBeepInterval = Mathf.Min(radioBeepInterval, 2.2f);
            ResolveBeaconMarker();
            RefreshBeaconMarker();
            if (!IsSpawned && HasSimulationAuthority)
                InitializeAuthority();
        }

        public override void OnNetworkSpawn()
        {
            Phase.OnValueChanged += OnMissionChanged;
            GuardsRemaining.OnValueChanged += OnCountChanged;
            DefenseSecondsRemaining.OnValueChanged += OnCountChanged;
            RadioActive.OnValueChanged += OnRadioChanged;
            OnRadioChanged(false, RadioActive.Value);
            RefreshObjective();
            ResolveBeaconMarker();
            RefreshBeaconMarker();

            if (!IsServer) return;
            InitializeAuthority();
        }

        void InitializeAuthority()
        {
            if (authorityInitialized) return;
            authorityInitialized = true;
            remainingGuards.Clear();
            foreach (OutpostGuardAI guard in FindObjectsByType<OutpostGuardAI>())
                if (guard != null) remainingGuards.Add(guard);
            SetGuardsRemaining(remainingGuards.Count);
            SetDefenseSeconds(Mathf.CeilToInt(GetDefenseDuration(CountActivePlayers())));
            bool radioCanBeActivated = remainingGuards.Count == 0;
            SetPhase(radioCanBeActivated ? MissionPhase.ActivateRadio : MissionPhase.Assault);
            SetRadioActive(false);
            flyingMonstersSpawned = false;
            heavyZombieSpawned = false;
            finalWaveSpawned = false;
            flyingMonsterSpawnsAt = double.PositiveInfinity;
            heavyZombieSpawnsAt = double.PositiveInfinity;
            RefreshObjective();
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnMissionChanged;
            GuardsRemaining.OnValueChanged -= OnCountChanged;
            DefenseSecondsRemaining.OnValueChanged -= OnCountChanged;
            RadioActive.OnValueChanged -= OnRadioChanged;
            StopAlarm();
            authorityInitialized = false;
        }

        void Update()
        {
            UpdateRadioAudio();

            if (CurrentPhase == MissionPhase.ActivateRadio)
            {
                ZombieTown.Multiplayer.PlayerClassController player = FindLocalPlayer();
                float distance = player != null && radio != null
                    ? Vector3.Distance(player.transform.position, radio.position)
                    : float.PositiveInfinity;
                bool canInteract = Time.time >= radioInteractionAvailableAt && distance <= interactionDistance;
                if (canInteract)
                    ShowRadioPrompt(GameLocalization.Text("F  ACTIVATE RADIO", "F  RADIO ACTIVEREN"));
                else
                    HideRadioPrompt();
                if (canInteract && Keyboard.current != null &&
                    Unity.FPS.Game.GameplayInteraction.Pressed)
                {
                    HideRadioPrompt();
                    if (IsSpawned) ActivateRadioRpc();
                    else BeginDefense();
                }
            }
            else HideRadioPrompt();

            if (!HasSimulationAuthority || CurrentPhase != MissionPhase.Defend || !ZombieTown.Multiplayer.NetworkRoundGate.IsOpen) return;
            double now = IsSpawned && NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(defenseEndsAt - now)));
            SetDefenseSeconds(seconds);
            if (seconds <= 0)
            {
                SetPhase(MissionPhase.Complete);
                return;
            }
            if (!flyingMonstersSpawned && now >= flyingMonsterSpawnsAt) SpawnFlyingMonsterWave();
            if (spawnHeavyZombies && !heavyZombieSpawned && now >= heavyZombieSpawnsAt) SpawnHeavyZombie();
            if (!finalWaveSpawned && seconds <= 60)
            {
                finalWaveSpawned = true;
                heavyZombieSpawned = false;
                flyingMonstersSpawned = false;
                if (spawnHeavyZombies) SpawnHeavyZombie();
                SpawnFlyingMonsterWave();
            }
            int zombieLimit = GetZombieSpawnLimit();
            if (Time.time >= nextSpawn && CountLivingZombies() < zombieLimit) SpawnZombie();
        }

        public void NotifyGuardDefeated(OutpostGuardAI guard)
        {
            if (!HasSimulationAuthority || !remainingGuards.Remove(guard)) return;
            SetGuardsRemaining(remainingGuards.Count);
            if (remainingGuards.Count == 0 && CurrentPhase == MissionPhase.Assault)
                SetPhase(MissionPhase.ActivateRadio);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ActivateRadioRpc(RpcParams rpcParams = default)
        {
            if (CurrentPhase != MissionPhase.ActivateRadio || radio == null || NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client) ||
                client.PlayerObject == null || Vector3.Distance(client.PlayerObject.transform.position, radio.position) > interactionDistance + .75f)
                return;
            BeginDefense();
        }

        void BeginDefense()
        {
            defensePlayerCount = CountActivePlayers();
            float scaledDefenseDuration = GetDefenseDuration(defensePlayerCount);
            SetRadioActive(true);
            SetDefenseSeconds(Mathf.CeilToInt(scaledDefenseDuration));
            double now = IsSpawned && NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
            defenseEndsAt = now + scaledDefenseDuration;
            flyingMonsterSpawnsAt = now + FlyingMonsterDelayAfterRadioSeconds;
            heavyZombieSpawnsAt = now + HeavyZombieDelayAfterRadioSeconds;
            nextSpawn = Time.time + .5f;
            SetPhase(MissionPhase.Defend);
        }

        void SpawnZombie()
        {
            float scaledSpawnInterval = spawnInterval / (Mathf.Max(1, defensePlayerCount) * GetSpawnRateMultiplier());
            nextSpawn = Time.time + scaledSpawnInterval * UnityEngine.Random.Range(.75f, 1.2f);
            if (zombiePrefabs.Count == 0 || spawnPoints.Count == 0) return;
            GameObject prefab = GetRandomRegularZombiePrefab();
            Transform point = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count)];
            SpawnZombiePrefab(prefab, point);
        }

        void SpawnHeavyZombie()
        {
            if (heavyZombieSpawned || spawnPoints.Count == 0) return;
            GameObject prefab = heavyZombiePrefab != null ? heavyZombiePrefab : zombiePrefabs.Find(IsHeavyZombiePrefab);
            if (prefab == null)
            {
                Debug.LogError("Radio Outpost cannot spawn its heavy zombie because no heavy prefab is configured.", this);
                return;
            }

            int heavyCount = GetHeavyZombieCount();
            if (heavyCount <= 0)
            {
                heavyZombieSpawned = true;
                return;
            }

            Vector3 focus = radio != null ? radio.position :
                beaconMarker != null ? beaconMarker.transform.position : transform.position;
            int spawned = 0;
            for (int heavyIndex = 0; heavyIndex < heavyCount; heavyIndex++)
            {
                Vector3 spawnPosition = Vector3.zero;
                bool foundNearbyPoint = false;
                float startAngle = heavyIndex / (float)heavyCount * Mathf.PI * 2f;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = startAngle + attempt / 12f * Mathf.PI * 2f;
                    Vector3 guess = focus + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 12f;
                    if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
                    spawnPosition = hit.position;
                    foundNearbyPoint = true;
                    break;
                }
                if (!foundNearbyPoint)
                {
                    Transform fallback = spawnPoints[(heavyIndex + spawned) % spawnPoints.Count];
                    if (fallback == null) continue;
                    spawnPosition = fallback.position;
                }

                Vector3 towardRadio = focus - spawnPosition;
                towardRadio.y = 0f;
                Quaternion rotation = towardRadio.sqrMagnitude > .01f
                    ? Quaternion.LookRotation(towardRadio) : Quaternion.identity;
                int variantIndex = heavyIndex;
                if (SpawnZombiePrefab(prefab, spawnPosition, rotation,
                        heavyZombieScaleMultiplier, defensePlayerCount, instance =>
                        {
                            OrcCombatVariant ability = instance.GetComponent<OrcCombatVariant>();
                            if (ability == null) return;
                            ability.Configure(variantIndex == 0 ? OrcCombatVariant.VariantType.Club :
                                variantIndex == 1 ? OrcCombatVariant.VariantType.RockThrower :
                                OrcCombatVariant.VariantType.Brute);
                        }))
                    spawned++;
            }

            heavyZombieSpawned = true;
            if (spawned < heavyCount)
                Debug.LogWarning($"Radio Outpost spawned {spawned}/{heavyCount} heavy zombies.", this);
        }

        void SpawnFlyingMonsterWave()
        {
            if (flyingMonstersSpawned || spawnPoints.Count == 0) return;
            Vector3 focus = radio != null ? radio.position :
                beaconMarker != null ? beaconMarker.transform.position : transform.position;
            int spawned = SpawnFlyingMonsters(focus);
            flyingMonstersSpawned = true;
            if (spawned < GetFlyingMonsterCount())
                Debug.LogWarning($"Radio Outpost spawned {spawned}/{GetFlyingMonsterCount()} flying monsters.", this);
        }

        int SpawnFlyingMonsters(Vector3 focus)
        {
            GameObject prefab = flyingMonsterPrefab != null ? flyingMonsterPrefab : GetRandomRegularZombiePrefab();
            if (prefab == null) return 0;
            int spawned = 0;
            int flyingCount = GetFlyingMonsterCount();
            for (int i = 0; i < flyingCount; i++)
            {
                float angle = (i + .5f) / flyingCount * Mathf.PI * 2f;
                Vector3 guess = focus + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 16f;
                Vector3 position = NavMesh.SamplePosition(guess, out NavMeshHit hit, 5f, NavMesh.AllAreas)
                    ? hit.position : spawnPoints[(i + 1) % spawnPoints.Count].position;
                Vector3 direction = focus - position;
                direction.y = 0f;
                Quaternion rotation = direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : Quaternion.identity;
                if (SpawnZombiePrefab(prefab, position, rotation, 1.1f, defensePlayerCount * 2.6f, instance =>
                    instance.GetComponent<FlyingRangedMonster>()?.ConfigureFlying()))
                    spawned++;
            }
            return spawned;
        }

        GameObject GetRandomRegularZombiePrefab()
        {
            int regularCount = 0;
            foreach (GameObject prefab in zombiePrefabs)
                if (prefab != null && !IsHeavyZombiePrefab(prefab)) regularCount++;
            if (regularCount == 0)
                return zombiePrefabs.Find(prefab => prefab != null);

            int selected = UnityEngine.Random.Range(0, regularCount);
            foreach (GameObject prefab in zombiePrefabs)
            {
                if (prefab == null || IsHeavyZombiePrefab(prefab)) continue;
                if (selected-- == 0) return prefab;
            }
            return null;
        }

        static bool IsHeavyZombiePrefab(GameObject prefab)
        {
            if (prefab == null) return false;
            string prefabName = prefab.name;
            return prefabName.IndexOf("Heavy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   prefabName.IndexOf("BigOrk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   prefabName.IndexOf("Big Orc", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool SpawnZombiePrefab(GameObject prefab, Transform point)
        {
            if (prefab == null || point == null) return false;
            return SpawnZombiePrefab(prefab, point.position, point.rotation);
        }

        static bool SpawnZombiePrefab(GameObject prefab, Vector3 position, Quaternion rotation,
            float scaleMultiplier = 1f, float healthMultiplier = 1f, Action<GameObject> configure = null)
        {
            if (prefab == null) return false;
            GameObject zombie = Instantiate(prefab, position, rotation);
            if (scaleMultiplier > 1f)
            {
                zombie.transform.localScale *= scaleMultiplier;
                ZombieAI zombieAI = zombie.GetComponent<ZombieAI>();
                if (zombieAI != null)
                {
                    zombieAI.GroundOffset *= scaleMultiplier;
                    zombieAI.ConfigureLargeZombie();
                }
            }
            if (healthMultiplier > 1f)
            {
                Health health = zombie.GetComponent<Health>();
                if (health != null)
                {
                    health.MaxHealth *= healthMultiplier;
                    health.CurrentHealth = health.MaxHealth;
                }
            }
            configure?.Invoke(zombie);
            NetworkObject networkObject = zombie.GetComponent<NetworkObject>();
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                if (networkObject != null) networkObject.Spawn(true);
                else
                {
                    Debug.LogError($"Radio Outpost zombie prefab '{prefab.name}' has no NetworkObject.", prefab);
                    Destroy(zombie);
                    return false;
                }
            }
            return true;
        }

        int GetZombieSpawnLimit()
        {
            int extraPlayers = Mathf.Max(0, defensePlayerCount - 1);
            float pressure = defensePlayerCount <= 1 && soloPressure != null ? soloPressure.aliveLimitMultiplier : 1f;
            return Mathf.RoundToInt(maximumLivingZombies * (1f + extraPlayers * .3f) * GetDefenseRamp() * pressure);
        }

        public Unity.FPS.Game.SoloZombiePressureProfile soloPressure;
        float GetSpawnRateMultiplier()
        {
            return GetDefenseRamp() * (defensePlayerCount <= 1 && soloPressure != null ? soloPressure.spawnRateMultiplier : 1f);
        }
        float GetDefenseRamp()
        {
            float totalSeconds = Mathf.Max(1f, GetDefenseDuration(defensePlayerCount));
            float elapsedRatio = 1f - Mathf.Clamp01(GetDefenseSeconds() / totalSeconds);
            return Mathf.Lerp(1f, 1.7f, elapsedRatio);
        }

        int GetHeavyZombieCount() => Mathf.Max(1, defensePlayerCount);

        int GetFlyingMonsterCount() => Mathf.Max(1, defensePlayerCount);

        static int CountLivingZombies()
        {
            int count = 0;
            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
                if (zombie != null && zombie.gameObject.activeInHierarchy) count++;
            return count;
        }

        static int CountActivePlayers()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening && manager.IsServer)
            {
                int networkPlayers = 0;
                foreach (NetworkClient client in manager.ConnectedClients.Values)
                {
                    if (client.PlayerObject == null) continue;
                    ZombieTown.Multiplayer.PlayerClassController player =
                        client.PlayerObject.GetComponent<ZombieTown.Multiplayer.PlayerClassController>();
                    if (player != null && player.IsReady.Value) networkPlayers++;
                }
                return Mathf.Max(1, networkPlayers);
            }

            int localPlayers = 0;
            foreach (ZombieTown.Multiplayer.PlayerClassController player in
                     FindObjectsByType<ZombieTown.Multiplayer.PlayerClassController>())
                if (player != null) localPlayers++;
            return Mathf.Max(1, localPlayers);
        }

        // Solo defense duration must stay at the full configured value; only extra players extend it further.
        float GetDefenseDuration(int playerCount) => defenseDuration * (1f + Mathf.Max(0, playerCount - 1) * .15f);

        static ZombieTown.Multiplayer.PlayerClassController FindLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            bool networkActive = manager != null && manager.IsListening;
            foreach (ZombieTown.Multiplayer.PlayerClassController player in
                     FindObjectsByType<ZombieTown.Multiplayer.PlayerClassController>())
                if ((!networkActive || player.IsOwner) && (!networkActive || player.IsReady.Value)) return player;
            return null;
        }

        void OnMissionChanged(byte previous, byte current)
        {
            if (current == (byte)MissionPhase.ActivateRadio && previous != current)
                radioInteractionAvailableAt = Time.time + .6f;
            if (current == (byte)MissionPhase.Complete)
                StopAlarm();
            RefreshObjective();
            RefreshBeaconMarker();
        }
        void OnCountChanged(int previous, int current) => RefreshObjective();

        void OnRadioChanged(bool previous, bool current)
        {
            StopAlarm();
            if (radioAudio != null)
            {
                radioAudio.Stop();
                radioAudio.loop = false;
                radioAudio.volume = radioBeepVolume;
                nextRadioBeep = current ? Time.time : float.PositiveInfinity;
            }
            if (radioLight != null) radioLight.enabled = current;
            if (current)
            {
                alarmRoutine = StartCoroutine(PlayDelayedAlarm());
            }
        }

        System.Collections.IEnumerator PlayDelayedAlarm()
        {
            yield return new WaitForSeconds(Mathf.Max(.5f, alarmDelaySeconds));
            bool active = IsSpawned ? RadioActive.Value : offlineRadioActive;
            if (!active || CurrentPhase != MissionPhase.Defend || alarmAudioClip == null)
            {
                alarmRoutine = null;
                yield break;
            }

            GameObject alarmObject = new("Radio Beacon Alarm Audio");
            alarmObject.transform.SetParent(radio != null ? radio : transform, false);
            alarmSource = alarmObject.AddComponent<AudioSource>();
            if (radioAudio != null)
            {
                alarmSource.outputAudioMixerGroup = radioAudio.outputAudioMixerGroup;
            }
            alarmSource.spatialBlend = 1f;
            alarmSource.rolloffMode = AudioRolloffMode.Logarithmic;
            alarmSource.minDistance = Mathf.Max(.1f, alarmMinDistance);
            alarmSource.maxDistance = Mathf.Max(alarmSource.minDistance + 1f, alarmMaxDistance);
            alarmSource.clip = alarmAudioClip;
            alarmSource.loop = true;
            alarmSource.playOnAwake = false;
            alarmSource.volume = alarmInitialVolume;
            alarmSource.Play();

            yield return new WaitForSeconds(Mathf.Max(.5f, alarmLoudDuration));
            float startVolume = alarmSource != null ? alarmSource.volume : alarmInitialVolume;
            float elapsed = 0f;
            while (alarmSource != null && elapsed < alarmFadeDuration)
            {
                elapsed += Time.deltaTime;
                alarmSource.volume = Mathf.Lerp(startVolume, alarmSustainedVolume,
                    Mathf.Clamp01(elapsed / Mathf.Max(.1f, alarmFadeDuration)));
                yield return null;
            }
            if (alarmSource != null) alarmSource.volume = alarmSustainedVolume;
            alarmRoutine = null;
        }

        void StopAlarm()
        {
            if (alarmRoutine != null)
            {
                StopCoroutine(alarmRoutine);
                alarmRoutine = null;
            }
            if (alarmSource == null) return;
            alarmSource.Stop();
            Destroy(alarmSource.gameObject);
            alarmSource = null;
        }

        void UpdateRadioAudio()
        {
            bool active = IsSpawned ? RadioActive.Value : offlineRadioActive;
            if (!active || radioAudio == null || radioAudio.clip == null || radioAudio.isPlaying ||
                Time.time < nextRadioBeep) return;
            radioAudio.Play();
            nextRadioBeep = Time.time + radioBeepInterval;
        }

        void RefreshObjective()
        {
            if (objective == null) return;
            objective.Title = GameLocalization.Text("OPERATION RADIO OUTPOST", "OPERATIE RADIOPOST");
            objective.Description = GameLocalization.Text(
                "Capture the building, activate the radio and defend the outpost.",
                "Verover het gebouw, activeer de radio en verdedig de buitenpost.");
            switch (CurrentPhase)
            {
                case MissionPhase.Assault:
                    objective.UpdateObjective(
                        GameLocalization.Text("Capture the radio building", "Verover het radiogebouw"),
                        GameLocalization.Text($"{GetGuardsRemaining()} guards", $"{GetGuardsRemaining()} bewakers"),
                        GameLocalization.Text("Eliminate the guards", "Schakel de bewakers uit"));
                    break;
                case MissionPhase.ActivateRadio:
                    objective.UpdateObjective(
                        GameLocalization.Text("Activate the radio inside", "Zet de radio binnen aan"),
                        GameLocalization.Text("Press E", "Druk op E"),
                        GameLocalization.Text("The building is secure", "Het gebouw is veilig"));
                    break;
                case MissionPhase.Defend:
                    int seconds = GetDefenseSeconds();
                    objective.UpdateObjective(
                        GameLocalization.Text("Defend the radio against the zombies", "Verdedig de radio tegen de zombies"),
                        $"{seconds / 60:0}:{seconds % 60:00}",
                        GameLocalization.Text("Hold your ground", "Blijf standhouden"));
                    break;
                case MissionPhase.Complete when !localCompleted:
                    localCompleted = true;
                    objective.CompleteObjective(
                        GameLocalization.Text("Radio link secured", "Radioverbinding beveiligd"),
                        GameLocalization.Text("COMPLETE", "VOLTOOID"),
                        GameLocalization.Text("The outpost is yours", "De buitenpost is van jullie"));
                    break;
            }
        }

        void ResolveBeaconMarker()
        {
            if (beaconMarker != null) return;
            RadioBeaconMarker[] markers = FindObjectsByType<RadioBeaconMarker>(
                FindObjectsInactive.Include);
            if (markers.Length > 0) beaconMarker = markers[0];
        }

        void RefreshBeaconMarker()
        {
            ResolveBeaconMarker();
            beaconMarker?.SetVisible(CurrentPhase == MissionPhase.ActivateRadio);
        }

        int GetGuardsRemaining() => IsSpawned ? GuardsRemaining.Value : offlineGuardsRemaining;
        int GetDefenseSeconds() => IsSpawned ? DefenseSecondsRemaining.Value : offlineDefenseSeconds;

        void SetGuardsRemaining(int value)
        {
            if (IsSpawned) GuardsRemaining.Value = value;
            else offlineGuardsRemaining = value;
            RefreshObjective();
        }

        void SetDefenseSeconds(int value)
        {
            if (IsSpawned)
            {
                if (DefenseSecondsRemaining.Value != value) DefenseSecondsRemaining.Value = value;
            }
            else if (offlineDefenseSeconds != value)
            {
                offlineDefenseSeconds = value;
                RefreshObjective();
            }
        }

        void SetRadioActive(bool value)
        {
            if (IsSpawned) RadioActive.Value = value;
            else
            {
                bool previous = offlineRadioActive;
                offlineRadioActive = value;
                OnRadioChanged(previous, value);
            }
        }

        void SetPhase(MissionPhase value)
        {
            if (IsSpawned) Phase.Value = (byte)value;
            else
            {
                MissionPhase previous = offlinePhase;
                offlinePhase = value;
                if (value == MissionPhase.ActivateRadio && previous != value)
                    radioInteractionAvailableAt = Time.time + .6f;
            }
            if (value == MissionPhase.Complete)
                StopAlarm();
            RefreshObjective();
        }

        void ShowRadioPrompt(string message)
        {
            if (radioPromptCanvas == null)
            {
                radioPromptCanvas = ZombieTown.Multiplayer.RuntimeMenuUI.CreateCanvas("Radio Interaction Prompt", 855);
                RectTransform panel = ZombieTown.Multiplayer.RuntimeMenuUI.Block("Prompt", radioPromptCanvas.transform,
                    new Color(.025f, .04f, .055f, .9f));
                panel.anchorMin = new Vector2(.28f, .115f);
                panel.anchorMax = new Vector2(.72f, .175f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                radioPromptText = ZombieTown.Multiplayer.RuntimeMenuUI.Label("Text", panel, string.Empty, 18,
                    TextAnchor.MiddleCenter, ZombieTown.Multiplayer.RuntimeMenuUI.White);
                ZombieTown.Multiplayer.RuntimeMenuUI.Stretch(radioPromptText.rectTransform, 12, 12, 4, 4);
            }
            radioPromptCanvas.gameObject.SetActive(true);
            radioPromptText.text = message;
        }

        void HideRadioPrompt()
        {
            if (radioPromptCanvas != null) radioPromptCanvas.gameObject.SetActive(false);
        }
    }
}
