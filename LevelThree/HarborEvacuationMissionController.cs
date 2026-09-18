using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.AI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using ZombieTown.Enemies;
using ZombieTown.LevelTwo;
using ZombieTown.Multiplayer;

namespace ZombieTown.LevelThree
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HarborEvacuationMissionController : NetworkBehaviour
    {
        public enum MissionPhase : byte { RestorePower, ActivateBeacon, BoardFerry, Defend, Extract, Complete }

        [SerializeField] HarborEvacuationObjective objective;
        [Tooltip("Reuse the old radio as a temporary ferry beacon, or assign a new harbor beacon.")]
        [SerializeField] Transform radio;
        [SerializeField] AudioSource radioAudio;
        [SerializeField] RadioBeaconMarker beaconMarker;
        [SerializeField] RadioBeaconMarker ferryMarker;
        [SerializeField] List<HarborPowerSwitch> powerSwitches = new();
        [SerializeField] Collider extractionZone;
        [SerializeField] List<GameObject> zombiePrefabs = new();
        [Tooltip("Kept separate from zombiePrefabs so orcs can be reused/tuned independently across levels.")]
        [SerializeField] GameObject orcPrefab;
        [SerializeField, Range(1.5f, 4f)] float orcScaleMultiplier = 2.5f;
        [SerializeField, Min(.25f)] float orcSpawnInterval = 8f;
        [Tooltip("Kept separate from zombiePrefabs so demons can be reused/tuned independently across levels.")]
        [SerializeField] GameObject demonPrefab;
        [SerializeField, Min(1)] int finalWaveDemonsPerPlayer = 2;
        [SerializeField, Min(.25f)] float demonSpawnInterval = 7f;
        [SerializeField, Min(1)] int maximumLivingDemons = 8;
        [SerializeField] List<Transform> spawnPoints = new();
        [SerializeField, Min(15f)] float defenseDuration = 60f;
        [SerializeField, Min(.25f)] float spawnInterval = 1.15f;
        [SerializeField, Min(1)] int maximumLivingZombies = 70;
        [SerializeField, Min(1f)] float interactionDistance = 3.5f;
        [SerializeField, Min(0f)] float boatExitGraceSeconds = 4f;
        [SerializeField, Min(0f)] float extractionEdgeTolerance = .75f;

        public readonly NetworkVariable<byte> Phase = new((byte)MissionPhase.RestorePower);
        public readonly NetworkVariable<int> ActivatedPowerMask = new();
        public readonly NetworkVariable<int> DefenseSecondsRemaining = new();
        public readonly NetworkVariable<bool> CountdownPaused = new();

        double lastServerTime;
        float defenseSecondsLeft;
        float nextSpawn;
        float nextOrcSpawn;
        float nextDemonSpawn;
        float secondsOutsideBoat;
        bool initialized;
        bool localCompleted;
        GameObject extractionZoneIndicator;

        MissionPhase CurrentPhase => (MissionPhase)Phase.Value;

        void Start()
        {
            ResolvePowerSwitches();
            ApplyHarborLightingTuning();
            EnsureExtractionZoneIndicator();
            RefreshPresentation();
        }

        static void ApplyHarborLightingTuning()
        {
            RenderSettings.ambientIntensity = Mathf.Max(RenderSettings.ambientIntensity, .9f);
            if (RenderSettings.fog) RenderSettings.fogDensity = Mathf.Min(RenderSettings.fogDensity, .007f);
            foreach (Light light in FindObjectsByType<Light>())
            {
                if (light.name == "Outpost Sun") light.intensity = Mathf.Max(light.intensity, .62f);
                else if (light.name.StartsWith("Warm Pool"))
                {
                    light.intensity = Mathf.Max(light.intensity, 5.35f);
                    light.range = Mathf.Max(light.range, 16f);
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            Phase.OnValueChanged += OnPhaseChanged;
            ActivatedPowerMask.OnValueChanged += OnIntChanged;
            DefenseSecondsRemaining.OnValueChanged += OnIntChanged;
            CountdownPaused.OnValueChanged += OnBoolChanged;
            if (IsServer) InitializeAuthority();
            RefreshPresentation();
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnPhaseChanged;
            ActivatedPowerMask.OnValueChanged -= OnIntChanged;
            DefenseSecondsRemaining.OnValueChanged -= OnIntChanged;
            CountdownPaused.OnValueChanged -= OnBoolChanged;
        }

        void InitializeAuthority()
        {
            if (initialized) return;
            ResolvePowerSwitches();
            initialized = true;
            ActivatedPowerMask.Value = 0;
            defenseSecondsLeft = defenseDuration;
            DefenseSecondsRemaining.Value = Mathf.CeilToInt(defenseSecondsLeft);
            CountdownPaused.Value = false;
            Phase.Value = (byte)(powerSwitches.Count > 0
                ? MissionPhase.RestorePower : MissionPhase.ActivateBeacon);
        }

        void ResolvePowerSwitches()
        {
            if (powerSwitches == null) powerSwitches = new List<HarborPowerSwitch>();
            if (powerSwitches.Count == 0)
                powerSwitches.AddRange(FindObjectsByType<HarborPowerSwitch>());
        }

        void Update()
        {
            UpdateLocalInteraction();
            if (!IsServer || !NetworkRoundGate.IsOpen) return;

            if (CurrentPhase != MissionPhase.Complete)
            {
                float pressure = soloPressure != null && NetworkManager.ConnectedClients.Count <= 1 ? soloPressure.aliveLimitMultiplier : 1f;
                if (CountLivingZombies() < Mathf.CeilToInt(maximumLivingZombies * pressure) && Time.time >= nextSpawn)
                    SpawnZombie();
                if (CurrentPhase == MissionPhase.BoardFerry || CurrentPhase == MissionPhase.Defend)
                    HandleZombiesAtFerry();
                bool hornHasSounded = CurrentPhase == MissionPhase.BoardFerry ||
                                      CurrentPhase == MissionPhase.Defend;
                if (hornHasSounded && orcPrefab != null && Time.time >= nextOrcSpawn)
                    SpawnOrc();
            }

            if (CurrentPhase == MissionPhase.BoardFerry && AllActivePlayersInsideExtraction())
            {
                BeginBoatDefense();
                return;
            }

            if (CurrentPhase == MissionPhase.Defend)
            {
                double now = NetworkManager.ServerTime.Time;
                double delta = lastServerTime > 0d ? now - lastServerTime : 0d;
                lastServerTime = now;
                bool allAboard = AllActivePlayersInsideExtraction();
                if (allAboard)
                    secondsOutsideBoat = 0f;
                else
                    secondsOutsideBoat += Mathf.Max(0f, (float)delta);

                bool countdownPaused = !allAboard && secondsOutsideBoat > boatExitGraceSeconds;
                CountdownPaused.Value = countdownPaused;
                if (!countdownPaused)
                    defenseSecondsLeft = Mathf.Max(0f, defenseSecondsLeft - Mathf.Max(0f, (float)delta));

                int seconds = Mathf.Max(0, Mathf.CeilToInt(defenseSecondsLeft));
                DefenseSecondsRemaining.Value = seconds;
                if (seconds <= 0)
                {
                    Phase.Value = (byte)MissionPhase.Complete;
                    return;
                }

                if (!countdownPaused && CountLivingDemons() < maximumLivingDemons && Time.time >= nextDemonSpawn)
                    SpawnDemon();
            }
        }

        void UpdateLocalInteraction()
        {
            PlayerClassController player = FindLocalPlayer();
            if (player == null || Keyboard.current == null || !Unity.FPS.Game.GameplayInteraction.Pressed)
                return;

            if (CurrentPhase == MissionPhase.RestorePower)
            {
                for (int i = 0; i < powerSwitches.Count && i < 30; i++)
                {
                    if ((ActivatedPowerMask.Value & (1 << i)) != 0 || powerSwitches[i] == null) continue;
                    if (Vector3.Distance(player.transform.position, powerSwitches[i].transform.position) <= interactionDistance)
                    {
                        ActivatePowerSwitchRpc(i);
                        return;
                    }
                }
            }
            else if (CurrentPhase == MissionPhase.ActivateBeacon && radio != null &&
                     Vector3.Distance(player.transform.position, radio.position) <= interactionDistance)
            {
                ActivateBeaconRpc();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ActivatePowerSwitchRpc(int index, RpcParams rpcParams = default)
        {
            if (CurrentPhase != MissionPhase.RestorePower || index < 0 ||
                index >= powerSwitches.Count || index >= 30 || powerSwitches[index] == null ||
                !TryGetRequestPlayer(rpcParams, out Transform player) ||
                Vector3.Distance(player.position, powerSwitches[index].transform.position) > interactionDistance + .75f)
                return;

            ActivatedPowerMask.Value |= 1 << index;
            int requiredMask = (1 << Mathf.Min(30, powerSwitches.Count)) - 1;
            if ((ActivatedPowerMask.Value & requiredMask) == requiredMask)
                Phase.Value = (byte)MissionPhase.ActivateBeacon;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ActivateBeaconRpc(RpcParams rpcParams = default)
        {
            if (CurrentPhase != MissionPhase.ActivateBeacon || radio == null ||
                !TryGetRequestPlayer(rpcParams, out Transform player) ||
                Vector3.Distance(player.position, radio.position) > interactionDistance + .75f)
                return;

            if (radioAudio != null) PlayBeaconRpc();
            defenseSecondsLeft = defenseDuration;
            DefenseSecondsRemaining.Value = Mathf.CeilToInt(defenseDuration);
            Phase.Value = (byte)MissionPhase.BoardFerry;
        }

        void BeginBoatDefense()
        {
            defenseSecondsLeft = defenseDuration;
            DefenseSecondsRemaining.Value = Mathf.CeilToInt(defenseDuration);
            lastServerTime = NetworkManager.ServerTime.Time;
            secondsOutsideBoat = 0f;
            CountdownPaused.Value = false;
            nextDemonSpawn = Time.time + demonSpawnInterval;
            Phase.Value = (byte)MissionPhase.Defend;
            SpawnInitialDemonWave();
        }

        [Rpc(SendTo.Everyone)]
        void PlayBeaconRpc()
        {
            if (radioAudio != null && !radioAudio.isPlaying) radioAudio.Play();
        }

        bool TryGetRequestPlayer(RpcParams rpcParams, out Transform player)
        {
            player = null;
            if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(
                    rpcParams.Receive.SenderClientId, out NetworkClient client) || client.PlayerObject == null)
                return false;
            player = client.PlayerObject.transform;
            return true;
        }

        void SpawnZombie()
        {
            float pressure=soloPressure!=null && (NetworkManager==null || NetworkManager.ConnectedClients.Count<=1)?soloPressure.spawnRateMultiplier:1f;
            nextSpawn = Time.time + spawnInterval * Random.Range(.8f, 1.2f)/Mathf.Max(.1f,pressure);
            if (zombiePrefabs.Count == 0 || spawnPoints.Count == 0) return;
            GameObject prefab = ChooseOrdinaryZombiePrefab();
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Count)];
            if (prefab == null || point == null) return;
            if (!NavMesh.SamplePosition(point.position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                return;
            GameObject instance = Instantiate(prefab, hit.position + Vector3.up * .05f, point.rotation);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
        }

        GameObject ChooseOrdinaryZombiePrefab()
        {
            int start = Random.Range(0, zombiePrefabs.Count);
            for (int offset = 0; offset < zombiePrefabs.Count; offset++)
            {
                GameObject candidate = zombiePrefabs[(start + offset) % zombiePrefabs.Count];
                if (candidate == null || candidate == orcPrefab || candidate == demonPrefab) continue;
                if (candidate.GetComponent<OrcCombatVariant>() != null ||
                    candidate.GetComponent<FlyingRangedMonster>() != null) continue;
                return candidate;
            }
            return null;
        }

        void SpawnOrc()
        {
            nextOrcSpawn = Time.time + orcSpawnInterval * Random.Range(.85f, 1.2f);
            if (orcPrefab == null || spawnPoints.Count == 0) return;
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Count)];
            if (point == null || !NavMesh.SamplePosition(point.position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                return;

            GameObject instance = Instantiate(orcPrefab, hit.position + Vector3.up * .05f, point.rotation);
            instance.transform.localScale *= orcScaleMultiplier;
            ZombieAI zombieAI = instance.GetComponent<ZombieAI>();
            if (zombieAI != null)
            {
                zombieAI.GroundOffset *= orcScaleMultiplier;
                zombieAI.ConfigureLargeZombie();
            }

            OrcCombatVariant variant = instance.GetComponent<OrcCombatVariant>();
            if (variant != null)
            {
                OrcCombatVariant.VariantType[] kinds =
                {
                    OrcCombatVariant.VariantType.Club,
                    OrcCombatVariant.VariantType.RockThrower,
                    OrcCombatVariant.VariantType.Brute
                };
                variant.Configure(kinds[Random.Range(0, kinds.Length)]);
            }

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
        }

        void SpawnInitialDemonWave()
        {
            if (demonPrefab == null || spawnPoints.Count == 0) return;
            int demonCount = Mathf.Max(finalWaveDemonsPerPlayer, CountActivePlayers() * finalWaveDemonsPerPlayer);
            for (int i = 0; i < demonCount; i++)
                SpawnDemon(i, demonCount);
        }

        void SpawnDemon()
        {
            nextDemonSpawn = Time.time + demonSpawnInterval * Random.Range(.85f, 1.15f);
            SpawnDemon(Random.Range(0, Mathf.Max(1, spawnPoints.Count)), Mathf.Max(1, spawnPoints.Count));
        }

        void SpawnDemon(int index, int count)
        {
            if (demonPrefab == null || spawnPoints.Count == 0) return;
            Vector3 focus = extractionZone != null ? extractionZone.bounds.center :
                radio != null ? radio.position : transform.position;
            float angle = (index + .5f) / Mathf.Max(1, count) * Mathf.PI * 2f;
            Vector3 guess = focus + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 16f;
            Vector3 position = NavMesh.SamplePosition(guess, out NavMeshHit hit, 6f, NavMesh.AllAreas)
                ? hit.position : spawnPoints[index % spawnPoints.Count].position;
            Vector3 direction = focus - position;
            direction.y = 0f;
            Quaternion rotation = direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : Quaternion.identity;

            GameObject instance = Instantiate(demonPrefab, position, rotation);
            instance.GetComponent<FlyingRangedMonster>()?.ConfigureFlying();
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
        }

        static int CountActivePlayers()
        {
            int count = 0;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
                if (player != null && player.IsReady.Value) count++;
            return Mathf.Max(1, count);
        }

        void HandleZombiesAtFerry()
        {
            if (extractionZone == null) return;
            Bounds zoneBounds = extractionZone.bounds;
            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
            {
                if (zombie == null || !zoneBounds.Contains(zombie.transform.position)) continue;
                zombie.ForceFerryFall(zoneBounds.center);
            }
        }

        bool AllActivePlayersInsideExtraction()
        {
            if (extractionZone == null) return true;
            bool found = false;
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
            {
                if (player == null || !player.IsSpawned || !player.IsReady.Value || player.IsDowned.Value) continue;
                found = true;
                if (!IsPlayerInsideExtraction(player)) return false;
            }
            return found;
        }

        bool IsPlayerInsideExtraction(PlayerClassController player)
        {
            CharacterController character = player.GetComponent<CharacterController>();
            if (character != null && extractionZone.bounds.Intersects(character.bounds)) return true;

            Vector3 feet = player.transform.position;
            Vector3 chest = feet + Vector3.up * Mathf.Max(.8f, player.StandingHeight.Value * .5f);
            return IsNearExtraction(feet) || IsNearExtraction(chest);
        }

        bool IsNearExtraction(Vector3 point)
        {
            Vector3 closest = extractionZone.ClosestPoint(point);
            return (closest - point).sqrMagnitude <= extractionEdgeTolerance * extractionEdgeTolerance;
        }

        public Unity.FPS.Game.SoloZombiePressureProfile soloPressure;
        int CountLivingZombies()
        {
            int count = 0;
            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
            {
                Health zombieHealth = zombie != null ? zombie.GetComponent<Health>() : null;
                if (zombieHealth != null && zombieHealth.CurrentHealth > 0f) count++;
            }
            return count;
        }

        static int CountLivingDemons()
        {
            int count = 0;
            foreach (FlyingRangedMonster demon in FindObjectsByType<FlyingRangedMonster>())
            {
                Health demonHealth = demon != null ? demon.GetComponent<Health>() : null;
                if (demon.IsFlyingConfigured && (demonHealth == null || demonHealth.CurrentHealth > 0f)) count++;
            }
            return count;
        }

        void EnsureExtractionZoneIndicator()
        {
            if (extractionZone == null || extractionZoneIndicator != null) return;
            BoxCollider box = extractionZone as BoxCollider;
            extractionZoneIndicator = new GameObject("Glowing Ferry Boarding Zone");
            extractionZoneIndicator.transform.SetParent(extractionZone.transform, false);

            Vector3 center = box != null ? box.center : extractionZone.transform.InverseTransformPoint(extractionZone.bounds.center);
            Vector3 size = box != null ? box.size : extractionZone.bounds.size;
            float deckY = center.y - size.y * .5f + .08f;
            Material markerMaterial = new(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            markerMaterial.name = "Runtime Ferry Boarding Marker";
            markerMaterial.color = new Color(.05f, .95f, 1f, 1f);
            if (markerMaterial.HasProperty("_BaseColor")) markerMaterial.SetColor("_BaseColor", markerMaterial.color);

            CreateZoneEdge("Port edge", new Vector3(center.x - size.x * .5f, deckY, center.z),
                new Vector3(.12f, .06f, size.z), markerMaterial);
            CreateZoneEdge("Starboard edge", new Vector3(center.x + size.x * .5f, deckY, center.z),
                new Vector3(.12f, .06f, size.z), markerMaterial);
            CreateZoneEdge("Bow edge", new Vector3(center.x, deckY, center.z + size.z * .5f),
                new Vector3(size.x, .06f, .12f), markerMaterial);
            CreateZoneEdge("Stern edge", new Vector3(center.x, deckY, center.z - size.z * .5f),
                new Vector3(size.x, .06f, .12f), markerMaterial);
        }

        void CreateZoneEdge(string edgeName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = edgeName;
            edge.transform.SetParent(extractionZoneIndicator.transform, false);
            edge.transform.localPosition = localPosition;
            edge.transform.localRotation = Quaternion.identity;
            edge.transform.localScale = localScale;
            Destroy(edge.GetComponent<Collider>());
            edge.GetComponent<Renderer>().sharedMaterial = material;
        }

        PlayerClassController FindLocalPlayer()
        {
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
                if (player != null && player.IsOwner && player.IsReady.Value) return player;
            return null;
        }

        void OnPhaseChanged(byte previous, byte current) => RefreshPresentation();
        void OnIntChanged(int previous, int current) => RefreshPresentation();
        void OnBoolChanged(bool previous, bool current) => RefreshPresentation();

        void RefreshPresentation()
        {
            for (int i = 0; i < powerSwitches.Count; i++)
                powerSwitches[i]?.SetActivated((ActivatedPowerMask.Value & (1 << i)) != 0);
            beaconMarker?.SetVisible(CurrentPhase == MissionPhase.ActivateBeacon);
            ferryMarker?.SetVisible(CurrentPhase == MissionPhase.BoardFerry || CurrentPhase == MissionPhase.Defend);
            if (extractionZoneIndicator != null)
                extractionZoneIndicator.SetActive(CurrentPhase == MissionPhase.BoardFerry || CurrentPhase == MissionPhase.Defend);
            if (objective == null) return;

            switch (CurrentPhase)
            {
                case MissionPhase.RestorePower:
                    int active = 0;
                    for (int i = 0; i < powerSwitches.Count; i++)
                        if ((ActivatedPowerMask.Value & (1 << i)) != 0) active++;
                    objective.UpdateObjective(
                        GameLocalization.Text("Restore harbor power", "Herstel de havenstroom"),
                        $"{active} / {powerSwitches.Count}",
                        GameLocalization.Text("Press E at each power switch", "Druk E bij elke stroomschakelaar"));
                    break;
                case MissionPhase.ActivateBeacon:
                    objective.UpdateObjective(GameLocalization.Text("Sound the evacuation horn", "Laat de evacuatiehoorn klinken"),
                        "F", GameLocalization.Text("Follow the objective marker and press F", "Volg de doelmarker en druk F"));
                    break;
                case MissionPhase.BoardFerry:
                    objective.UpdateObjective(GameLocalization.Text("Reach the pier and board the ferry", "Ga naar de kade en stap in de veerboot"),
                        $"{DefenseSecondsRemaining.Value / 60:00}:{DefenseSecondsRemaining.Value % 60:00}",
                        GameLocalization.Text("Stand inside the glowing cyan zone on the boat deck", "Ga in de lichtblauwe zone op het dek staan"));
                    break;
                case MissionPhase.Defend:
                    bool allAboard = AllActivePlayersInsideExtraction();
                    bool countdownPaused = CountdownPaused.Value;
                    objective.UpdateObjective(
                        allAboard
                            ? GameLocalization.Text("Survive the demon assault", "Overleef de demonenaanval")
                            : GameLocalization.Text("Return to the ferry", "Keer terug naar de veerboot"),
                        $"{DefenseSecondsRemaining.Value / 60:00}:{DefenseSecondsRemaining.Value % 60:00}",
                        allAboard
                            ? GameLocalization.Text("Stay inside the boat until departure", "Blijf in de boot tot vertrek")
                            : countdownPaused
                                ? GameLocalization.Text("The countdown is paused", "De aftelling staat stil")
                                : GameLocalization.Text("Return within 4 seconds", "Keer binnen 4 seconden terug"));
                    break;
                case MissionPhase.Extract:
                    objective.UpdateObjective(GameLocalization.Text("Board the evacuation ferry", "Ga aan boord van de veerboot"),
                        string.Empty, GameLocalization.Text("All survivors must board the ferry", "Alle survivors moeten aan boord zijn"));
                    break;
                case MissionPhase.Complete:
                    if (!localCompleted)
                    {
                        localCompleted = true;
                        objective.CompleteObjective(string.Empty, string.Empty,
                            GameLocalization.Text("Harbor evacuated", "Haven geëvacueerd"));
                    }
                    break;
            }
        }
    }
}
