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
using ZombieTown.Foundation;
using ZombieTown.Multiplayer;

namespace ZombieTown.LevelFour
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed partial class RetreatDefenseMissionController : NetworkBehaviour
    {
        public enum MissionPhase : byte { Defend, UnlockGate, FallBack, Complete, Prepare }

        [System.Serializable]
        public sealed class SpawnAnchorGroup
        {
            [SerializeField] List<Transform> anchors = new();

            public IReadOnlyList<Transform> Anchors => anchors;
        }

        [SerializeField] RetreatDefenseObjective objective;
        [SerializeField] List<Collider> defenseZones = new();
        [SerializeField] List<Transform> gates = new();
        [SerializeField] List<GameObject> gateMarkers = new();
        // 2.5x Sept 2026 pass: doors/gates cost much more to unlock.
        [SerializeField] List<int> gateCosts = new() { 300, 625, 1125 };
        [SerializeField] List<float> defenseDurations = new() { 75f, 100f, 160f, 240f };
        [SerializeField] List<int> pressureThresholds = new() { 24, 34, 44, 60 };
        [SerializeField] List<SpawnAnchorGroup> enemySpawnAnchorsByDefenseLine = new();
        // Kept as a backwards-compatible fallback for older Level 4 scenes.
        [SerializeField] List<Transform> enemySpawnAnchors = new();
        [SerializeField] List<GameObject> zombiePrefabs = new();
        [SerializeField] GameObject heavyZombiePrefab;
        [SerializeField] GameObject flyingDemonPrefab;
        [SerializeField] bool spawnFlyingDemons;
        [Header("Small Orc Waves")]
        [SerializeField] bool spawnSmallOrcs = true;
        [SerializeField, Min(0f)] float smallOrcStartDelay = 400f;
        [SerializeField, Min(1f)] float smallOrcSpawnInterval = 25f;
        [SerializeField, Min(1)] int smallOrcsPerPlayer = 1;
        [SerializeField, Range(.25f, 1f)] float smallOrcScaleMultiplier = .7f;
        [SerializeField, Range(.1f, 1f)] float smallOrcHealthMultiplier = .55f;
        [SerializeField] GameObject friendlyGuardPrefab;
        [SerializeField] Transform guardHireTerminal;
        [SerializeField] Transform guardSpawnPoint;
        [SerializeField] List<Transform> guardHireTerminals = new();
        [SerializeField] List<Transform> guardSpawnPoints = new();
        [SerializeField, Min(0)] int guardHirePrice = 400;
        [SerializeField, Min(1f)] float guardHireDistance = 3.6f;
        [SerializeField, Range(2, 12)] int maximumGuardsPerPlayer = 6;
        [SerializeField, Min(1)] int finalLineMaximumDemons = 8;
        [SerializeField, Min(1f)] float finalLineDemonInterval = 5.5f;
        [SerializeField, Min(10f)] float minimumHoldBeforePressureRetreat = 24f;
        [SerializeField, Min(.25f)] float baseSpawnInterval = 1.35f;
        [SerializeField, Min(5)] int maximumLivingEnemies = 200;
        [SerializeField, Min(10)] int maximumOvertimeLivingEnemies = 108;
        [SerializeField, Min(30f)] float fullOvertimePressureSeconds = 90f;
        [SerializeField, Range(1f, 3f)] float finalLineZombieHealthMultiplier = 2f;
        [SerializeField, Min(1f)] float gateInteractionDistance = 4f;
        [SerializeField, Min(.5f)] float gateOpenHeight = 6.5f;

        public readonly NetworkVariable<byte> Phase = new((byte)MissionPhase.Defend);
        public readonly NetworkVariable<int> CurrentLine = new();
        public readonly NetworkVariable<int> SecondsRemaining = new();
        public readonly NetworkVariable<int> OpenGateMask = new();

        readonly List<Vector3> closedGatePositions = new();
        float secondsLeft;
        float elapsedAtLine;
        float nextSpawnTime;
        float nextDemonSpawnTime;
        float nextSmallOrcSpawnTime;
        float missionElapsed;
        [SerializeField, Min(0)] float gatePreparationSeconds = 30f;
        float gatePreparationRemaining;
        bool gatePreparationStarted;
        double lastServerTime;
        bool initialized;
        bool localCompleted;
        Canvas interactionCanvas;
        Text interactionText;
        readonly List<FriendlyGuardAI> localSquad = new();
        readonly List<FriendlyGuardAI> serverSquad = new();

        MissionPhase CurrentPhase => (MissionPhase)Phase.Value;

        void Start()
        {
            CacheGatePositions();
            ResolveFriendlyGuardReferences();
            ApplyGatePresentation();
            RefreshObjective();
        }

        public override void OnNetworkSpawn()
        {
            Phase.OnValueChanged += OnByteChanged;
            CurrentLine.OnValueChanged += OnIntChanged;
            SecondsRemaining.OnValueChanged += OnIntChanged;
            OpenGateMask.OnValueChanged += OnIntChanged;
            if (IsServer) InitializeAuthority();
            ResolveFriendlyGuardReferences();
            RefreshObjective();
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnByteChanged;
            CurrentLine.OnValueChanged -= OnIntChanged;
            SecondsRemaining.OnValueChanged -= OnIntChanged;
            OpenGateMask.OnValueChanged -= OnIntChanged;
        }

        void InitializeAuthority()
        {
            if (initialized) return;
            initialized = true;
            ResolveFlyingDemonPrefab();
            ResolveFriendlyGuardPrefab();
            CurrentLine.Value = 0;
            OpenGateMask.Value = 0;
            missionElapsed = 0f;
            BeginDefense(0);
        }

        void ResolveFlyingDemonPrefab()
        {
            if (flyingDemonPrefab != null || NetworkManager == null || NetworkManager.NetworkConfig?.Prefabs == null)
                return;
            foreach (NetworkPrefabsList prefabList in NetworkManager.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                if (prefabList == null) continue;
                foreach (NetworkPrefab entry in prefabList.PrefabList)
                {
                    GameObject candidate = entry?.Prefab;
                    if (candidate == null || candidate.GetComponent<FlyingRangedMonster>() == null) continue;
                    flyingDemonPrefab = candidate;
                    return;
                }
            }
        }

        void Update()
        {
            AnimateGates();
            UpdateLocalInteraction();
            if (!IsServer || NetworkManager==null || !NetworkManager.IsListening || NetworkManager.ShutdownInProgress || CurrentPhase == MissionPhase.Complete) return;
            if (!NetworkRoundGate.IsOpen) { lastServerTime=NetworkManager.ServerTime.Time; return; }

            double now = NetworkManager.ServerTime.Time;
            float delta = lastServerTime > 0d ? Mathf.Max(0f, (float)(now - lastServerTime)) : 0f;
            lastServerTime = now;
            missionElapsed += delta;
            elapsedAtLine += delta;
            gatePreparationRemaining=Mathf.Max(0,gatePreparationRemaining-delta);

            if(IsSiege && CurrentLine.Value < defenseZones.Count-1 && (OpenGateMask.Value & (1<<CurrentLine.Value))!=0 && AnyActivePlayerInsideZone(CurrentLine.Value+1)) {BeginDefense(CurrentLine.Value+1);return;}
            if(CurrentPhase==MissionPhase.Prepare) {
                secondsLeft=Mathf.Max(0,secondsLeft-delta);SecondsRemaining.Value=Mathf.CeilToInt(secondsLeft);
                if(secondsLeft<=0){secondsLeft=GetDuration(CurrentLine.Value);elapsedAtLine=0;Phase.Value=(byte)MissionPhase.Defend;}
                return;
            }
            if (CurrentPhase == MissionPhase.Defend)
            {
                secondsLeft = Mathf.Max(0f, secondsLeft - delta);
                SecondsRemaining.Value = Mathf.CeilToInt(secondsLeft);

                bool finalLine = CurrentLine.Value >= defenseZones.Count - 1;
                bool overwhelmed = !finalLine && elapsedAtLine >= minimumHoldBeforePressureRetreat &&
                                  CountLivingEnemies() >= GetPressureThreshold(CurrentLine.Value);
                if (secondsLeft <= 0f || overwhelmed)
                {
                    if (finalLine) CompleteMission();
                    else Phase.Value = (byte)MissionPhase.UnlockGate;
                }
            }

            if (CurrentPhase == MissionPhase.Defend || CurrentPhase == MissionPhase.UnlockGate ||
                CurrentPhase == MissionPhase.FallBack)
            {
                if (Time.time >= nextSpawnTime && CountLivingEnemies() < GetLivingEnemyLimit())
                    SpawnEnemy(GetSpawnIntervalMultiplier());
            }

            if (CurrentPhase == MissionPhase.Defend && ShouldSpawnFlyingDemons() &&
                Time.time >= nextDemonSpawnTime && CountLivingDemons() < GetDemonLimit())
                SpawnFlyingDemon();

            if (!UsesFoundation && spawnSmallOrcs && heavyZombiePrefab != null && missionElapsed >= smallOrcStartDelay &&
                Time.time >= nextSmallOrcSpawnTime)
                SpawnSmallOrcWave();

            if (CurrentPhase == MissionPhase.FallBack && AnyActivePlayerInsideZone(CurrentLine.Value))
                BeginDefense(CurrentLine.Value);
        }

        void BeginDefense(int line)
        {
            stageSpawned=0;
            CurrentLine.Value = Mathf.Clamp(line, 0, Mathf.Max(0, defenseZones.Count - 1));
            secondsLeft = GetDuration(CurrentLine.Value);
            elapsedAtLine = 0f;
            SecondsRemaining.Value = Mathf.CeilToInt(secondsLeft);
            lastServerTime = NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;
            nextSpawnTime = Time.time + .5f;
            nextDemonSpawnTime = Time.time + (line >= defenseZones.Count - 1 ? 2f : 7f);
            if (nextSmallOrcSpawnTime <= 0f)
                nextSmallOrcSpawnTime = Time.time + smallOrcStartDelay;
            Phase.Value = (byte)MissionPhase.Defend;
            if(IsSiege) {secondsLeft=line==0?gameplay.balance.defenseSupplies.preparationSeconds:gatePreparationStarted?gatePreparationRemaining:gatePreparationSeconds;SecondsRemaining.Value=Mathf.CeilToInt(secondsLeft);Phase.Value=(byte)MissionPhase.Prepare;}
            if (line >= defenseZones.Count - 1)
            {
                SpawnFlyingDemon();
                SpawnFlyingDemon();
            }
        }

        void CompleteMission()
        {
            SecondsRemaining.Value = 0;
            Phase.Value = (byte)MissionPhase.Complete;
        }

        void UpdateLocalInteraction()
        {
            PlayerClassController player = FindLocalPlayer();
            if (player == null || player.IsUsingMountedGun || player.IsRepairingDefense || Keyboard.current == null || Time.timeScale <= 0f)
            {
                HideInteractionPrompt();
                return;
            }

            CollectOwnedGuards(player.OwnerClientId, localSquad);
            if (Keyboard.current.gKey.wasPressedThisFrame && localSquad.Count > 0)
            {
                bool release = AreAllHolding(localSquad);
                if (release) CommandGuardSquadRpc(false, Vector3.zero);
                else if (TryGetCommandPoint(player, out Vector3 commandPoint))
                    CommandGuardSquadRpc(true, commandPoint);
            }

            int gateIndex = CurrentLine.Value;
            if ((CurrentPhase == MissionPhase.UnlockGate || IsSiege) && (OpenGateMask.Value & (1<<gateIndex))==0 && gateIndex >= 0 && gateIndex < gates.Count &&
                gates[gateIndex] != null &&
                IsPlayerNearGate(player.transform.position, gates[gateIndex], gateIndex))
            {
                int price = GetGateCost(gateIndex);
                string message = player.CurrentPoints >= price
                    ? GameLocalization.Text($"F  PAY ${price} TO UNLOCK DOOR", $"F  BETAAL ${price} OM DE DEUR TE OPENEN")
                    : GameLocalization.Text($"UNLOCK DOOR  -  ${price}  (NEED ${price - player.CurrentPoints})",
                        $"DEUR OPENEN  -  ${price}  (NOG ${price - player.CurrentPoints})");
                ShowInteractionPrompt(message);
                if (player.CurrentPoints >= price && Unity.FPS.Game.GameplayInteraction.Pressed)
                    PurchaseGateRpc(gateIndex);
                return;
            }

            int terminalIndex = FindNearestHireTerminal(player.transform.position);
            if (terminalIndex >= 0)
            {
                string message;
                if (localSquad.Count >= maximumGuardsPerPlayer)
                    message = GameLocalization.Text($"SQUAD FULL  -  {localSquad.Count}/{maximumGuardsPerPlayer}",
                        $"SQUAD VOL  -  {localSquad.Count}/{maximumGuardsPerPlayer}");
                else if (player.CurrentPoints >= GetGuardHirePrice())
                    message = GameLocalization.Text($"F  HIRE GUARD  -  ${GetGuardHirePrice()}  |  SQUAD {localSquad.Count}/{maximumGuardsPerPlayer}",
                        $"F  HUUR GUARD  -  ${GetGuardHirePrice()}  |  SQUAD {localSquad.Count}/{maximumGuardsPerPlayer}");
                else
                    message = GameLocalization.Text($"HIRE GUARD  -  ${GetGuardHirePrice()}  (NEED ${GetGuardHirePrice() - player.CurrentPoints})",
                        $"HUUR GUARD  -  ${GetGuardHirePrice()}  (NOG ${GetGuardHirePrice() - player.CurrentPoints})");
                ShowInteractionPrompt(message);
                if (localSquad.Count < maximumGuardsPerPlayer && player.CurrentPoints >= GetGuardHirePrice() &&
                    Unity.FPS.Game.GameplayInteraction.Pressed)
                    HireGuardRpc(terminalIndex);
                return;
            }

            if (localSquad.Count > 0)
            {
                ShowInteractionPrompt(AreAllHolding(localSquad)
                    ? GameLocalization.Text($"G  RELEASE SQUAD TO FOLLOW  ({localSquad.Count})",
                        $"G  LAAT SQUAD WEER VOLGEN  ({localSquad.Count})")
                    : GameLocalization.Text($"G  DEPLOY SQUAD AT CROSSHAIR  ({localSquad.Count})",
                        $"G  PLAATS SQUAD OP RICHTPUNT  ({localSquad.Count})"));
                return;
            }

            HideInteractionPrompt();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void HireGuardRpc(int terminalIndex, RpcParams rpcParams = default)
        {
            ResolveFriendlyGuardReferences();
            ResolveFriendlyGuardPrefab();
            if (friendlyGuardPrefab == null || terminalIndex < 0 || terminalIndex >= guardHireTerminals.Count ||
                guardHireTerminals[terminalIndex] == null ||
                !TryGetRequestPlayer(rpcParams, out PlayerClassController player) ||
                Vector3.Distance(player.transform.position, guardHireTerminals[terminalIndex].position) > guardHireDistance + 1f ||
                player.Points.Value < GetGuardHirePrice())
                return;

            CollectOwnedGuards(rpcParams.Receive.SenderClientId, serverSquad);
            if (serverSquad.Count >= maximumGuardsPerPlayer) return;
            int squadSlot = FindFreeSquadSlot(serverSquad);

            Transform terminal = guardHireTerminals[terminalIndex];
            Transform configuredSpawn = terminalIndex < guardSpawnPoints.Count ? guardSpawnPoints[terminalIndex] : null;
            Vector3 spawn = configuredSpawn != null ? configuredSpawn.position : terminal.position + terminal.forward * 2f;
            if (NavMesh.SamplePosition(spawn, out NavMeshHit hit, 5f, NavMesh.AllAreas)) spawn = hit.position;
            GameObject instance = Instantiate(friendlyGuardPrefab, spawn, terminal.rotation);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            FriendlyGuardAI guard = instance.GetComponent<FriendlyGuardAI>();
            if (networkObject == null || guard == null)
            {
                Destroy(instance);
                return;
            }

            player.Points.Value -= GetGuardHirePrice();
            networkObject.Spawn(true);
            guard.InitializeHire(rpcParams.Receive.SenderClientId, spawn, squadSlot);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void CommandGuardSquadRpc(bool hold, Vector3 requestedPosition, RpcParams rpcParams = default)
        {
            if (!TryGetRequestPlayer(rpcParams, out PlayerClassController player)) return;
            CollectOwnedGuards(rpcParams.Receive.SenderClientId, serverSquad);
            if (serverSquad.Count == 0) return;

            if (!hold)
            {
                foreach (FriendlyGuardAI guard in serverSquad) guard.SetHoldCommand(false, Vector3.zero);
                return;
            }

            if (!IsFinite(requestedPosition) || Vector3.Distance(player.transform.position, requestedPosition) > 50f ||
                !NavMesh.SamplePosition(requestedPosition, out NavMeshHit hit, 4f, NavMesh.AllAreas)) return;
            serverSquad.Sort((left, right) => left.NetworkObjectId.CompareTo(right.NetworkObjectId));
            Vector3 forward = Vector3.ProjectOnPlane(hit.position - player.transform.position, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = player.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < serverSquad.Count; i++)
            {
                Vector2 formation = GetFormationOffset(i, serverSquad.Count);
                Vector3 desired = hit.position + right * formation.x + forward * formation.y;
                Vector3 slot = NavMesh.SamplePosition(desired, out NavMeshHit slotHit, 2.5f, NavMesh.AllAreas)
                    ? slotHit.position : hit.position;
                serverSquad[i].SetHoldCommand(true, slot);
            }
        }

        static Vector2 GetFormationOffset(int index, int count)
        {
            if (count <= 1) return Vector2.zero;
            int ring = index / 6;
            int indexInRing = index % 6;
            int ringCount = Mathf.Min(6, count - ring * 6);
            float radius = 1.45f + ring * 1.65f;
            float angle = (indexInRing / (float)ringCount) * Mathf.PI * 2f + Mathf.PI * .5f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        static bool TryGetCommandPoint(PlayerClassController player, out Vector3 point)
        {
            point = default;
            PlayerCharacterController controller = player != null ? player.GetComponent<PlayerCharacterController>() : null;
            Camera camera = controller != null ? controller.PlayerCamera : null;
            if (camera == null) return false;
            Ray ray = new(camera.transform.position, camera.transform.forward);
            Vector3 guess = Physics.Raycast(ray, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point : ray.GetPoint(35f);
            if (!NavMesh.SamplePosition(guess, out NavMeshHit navHit, 5f, NavMesh.AllAreas)) return false;
            point = navHit.position;
            return true;
        }

        static bool IsFinite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) &&
                                                float.IsFinite(value.z);

        static void CollectOwnedGuards(ulong clientId, List<FriendlyGuardAI> result)
        {
            result.Clear();
            foreach (FriendlyGuardAI guard in FriendlyGuardAI.ActiveGuards)
                if (guard != null && guard.IsSpawned && guard.IsAlive && guard.HiredClientId.Value == clientId)
                    result.Add(guard);
        }

        static bool AreAllHolding(List<FriendlyGuardAI> squad)
        {
            if (squad.Count == 0) return false;
            foreach (FriendlyGuardAI guard in squad)
                if (guard == null || !guard.IsHoldingPosition.Value) return false;
            return true;
        }

        static int FindFreeSquadSlot(List<FriendlyGuardAI> squad)
        {
            for (int slot = 0; slot < 32; slot++)
            {
                bool used = false;
                foreach (FriendlyGuardAI guard in squad)
                    if (guard != null && guard.SquadSlot.Value == slot) { used = true; break; }
                if (!used) return slot;
            }
            return squad.Count;
        }

        int FindNearestHireTerminal(Vector3 playerPosition)
        {
            int best = -1;
            float bestSqr = guardHireDistance * guardHireDistance;
            for (int i = 0; i < guardHireTerminals.Count; i++)
            {
                Transform terminal = guardHireTerminals[i];
                if (terminal == null) continue;
                float sqr = (terminal.position - playerPosition).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                best = i;
            }
            return best;
        }

        void ResolveFriendlyGuardReferences()
        {
            if (guardHireTerminals == null) guardHireTerminals = new List<Transform>();
            if (guardSpawnPoints == null) guardSpawnPoints = new List<Transform>();
            if (guardHireTerminal == null)
            {
                GameObject terminal = GameObject.Find("Level 4 Hire Guard Wall Buy");
                if (terminal != null) guardHireTerminal = terminal.transform;
            }
            if (guardSpawnPoint == null && guardHireTerminal != null)
            {
                Transform child = guardHireTerminal.Find("Guard Spawn Point");
                if (child != null) guardSpawnPoint = child;
            }
            if (guardHireTerminal != null && !guardHireTerminals.Contains(guardHireTerminal))
                guardHireTerminals.Add(guardHireTerminal);
            if (guardSpawnPoint != null && !guardSpawnPoints.Contains(guardSpawnPoint))
                guardSpawnPoints.Add(guardSpawnPoint);
        }

        void ResolveFriendlyGuardPrefab()
        {
            if (friendlyGuardPrefab != null || NetworkManager == null || NetworkManager.NetworkConfig?.Prefabs == null)
                return;
            foreach (NetworkPrefabsList prefabList in NetworkManager.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                if (prefabList == null) continue;
                foreach (NetworkPrefab entry in prefabList.PrefabList)
                {
                    GameObject candidate = entry?.Prefab;
                    if (candidate == null || candidate.GetComponent<FriendlyGuardAI>() == null) continue;
                    friendlyGuardPrefab = candidate;
                    return;
                }
            }
        }

        void ShowInteractionPrompt(string message)
        {
            if (interactionCanvas == null)
            {
                interactionCanvas = RuntimeMenuUI.CreateCanvas("Level 4 Interaction Prompt", 870);
                RectTransform panel = RuntimeMenuUI.Block("Prompt", interactionCanvas.transform,
                    new Color(.025f, .04f, .055f, .94f));
                panel.anchorMin = new Vector2(.24f, .19f);
                panel.anchorMax = new Vector2(.76f, .255f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                interactionText = RuntimeMenuUI.Label("Text", panel, string.Empty, 18,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(interactionText.rectTransform, 12, 12, 4, 4);
            }
            interactionCanvas.gameObject.SetActive(true);
            interactionText.text = message;
        }

        void HideInteractionPrompt()
        {
            if (interactionCanvas != null) interactionCanvas.gameObject.SetActive(false);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PurchaseGateRpc(int gateIndex, RpcParams rpcParams = default)
        {
            if ((!IsSiege && CurrentPhase != MissionPhase.UnlockGate) || gateIndex != CurrentLine.Value ||
                gateIndex < 0 || gateIndex >= gates.Count || gates[gateIndex] == null ||
                !TryGetRequestPlayer(rpcParams, out PlayerClassController player) ||
                !IsPlayerNearGate(player.transform.position, gates[gateIndex], gateIndex, 1f))
                return;

            var authored=gates[gateIndex].GetComponent<ZombieTown.Foundation.ProgressionGate>();
            if (UsesFoundation && authored!=null) { TryPurchaseAuthoredGate(authored,player); return; }
            int price = GetGateCost(gateIndex);
            if (player.Points.Value < price) return;
            player.Points.Value -= price;
            OpenGateMask.Value |= 1 << gateIndex;
            CurrentLine.Value = Mathf.Min(gateIndex + 1, defenseZones.Count - 1);
            Phase.Value = (byte)MissionPhase.FallBack;
        }

        bool IsPlayerNearGate(Vector3 playerPosition, Transform gate, int gateIndex, float extraDistance = 0f)
        {
            if (gate == null) return false;
            Collider collider = gate.GetComponentInChildren<Collider>();
            Vector3 closest = collider != null ? collider.ClosestPoint(playerPosition) : gate.position;
            float radius=gate.TryGetComponent<ZombieTown.Foundation.ProgressionGate>(out var authored)?authored.Radius:gateInteractionDistance;
            if (Vector3.Distance(playerPosition, closest) > radius + extraDistance) return false;

            Vector3 approachDirection = GetGateApproachDirection(gateIndex, gate);
            Vector3 fromGate = Vector3.ProjectOnPlane(playerPosition - gate.position, Vector3.up);
            return fromGate.sqrMagnitude < .01f || Vector3.Dot(fromGate.normalized, approachDirection) >= -.15f;
        }

        Vector3 GetGateApproachDirection(int gateIndex, Transform gate)
        {
            if (gateIndex >= 0 && gateIndex < defenseZones.Count && defenseZones[gateIndex] != null)
            {
                Vector3 direction = Vector3.ProjectOnPlane(
                    defenseZones[gateIndex].bounds.center - gate.position, Vector3.up);
                if (direction.sqrMagnitude > .01f) return direction.normalized;
            }

            Vector3 fallback = Vector3.ProjectOnPlane(-gate.forward, Vector3.up);
            return fallback.sqrMagnitude > .01f ? fallback.normalized : Vector3.back;
        }

        bool TryGetRequestPlayer(RpcParams rpcParams, out PlayerClassController player)
        {
            player = null;
            if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(
                    rpcParams.Receive.SenderClientId, out NetworkClient client) || client.PlayerObject == null)
                return false;
            player = client.PlayerObject.GetComponent<PlayerClassController>();
            return player != null && player.IsReady.Value && !player.IsDowned.Value;
        }

        void SpawnEnemy(float intervalMultiplier)
        {
            if (UsesFoundation) { SpawnAuthoredEnemy(); return; }
            int line = Mathf.Clamp(CurrentLine.Value, 0, Mathf.Max(0, defenseZones.Count - 1));
            nextSpawnTime = Time.time + baseSpawnInterval * intervalMultiplier * Random.Range(.75f, 1.18f) /
                            Mathf.Lerp(1f, 1.55f, line / 3f);
            IReadOnlyList<Transform> anchors = GetSpawnAnchorsForLine(line);
            if (zombiePrefabs.Count == 0 || anchors.Count == 0) return;

            float overtime = GetOvertimePressure();
            float longRunPressure = Mathf.Clamp01(missionElapsed / 360f);
            float heavyChance = .08f + line * .035f + overtime * .16f + longRunPressure * .1f;
            bool spawnHeavy = heavyZombiePrefab != null && (line >= 1 || overtime >= .45f) &&
                              Random.value < Mathf.Min(.48f, heavyChance);
            GameObject prefab = spawnHeavy ? heavyZombiePrefab : zombiePrefabs[Random.Range(0, zombiePrefabs.Count)];
            Transform anchor = ChooseSpawnAnchor(anchors);
            if (prefab == null || anchor == null) return;
            Vector2 offset = Random.insideUnitCircle * 1.75f;
            Vector3 guess = anchor.position + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return;
            GameObject instance = Instantiate(prefab, hit.position + Vector3.up * .05f, anchor.rotation);
            Health spawnedHealth = instance.GetComponent<Health>();
            if (spawnedHealth != null)
            {
                spawnedHealth.MaxHealth *= GetZombieHealthMultiplier(line);
                spawnedHealth.CurrentHealth = spawnedHealth.MaxHealth;
            }
            ZombieAI zombie = instance.GetComponent<ZombieAI>();
            if (zombie != null)
            {
                zombie.MoveSpeed *= GetZombieSpeedMultiplier(line);
                NavMeshAgent navigation = instance.GetComponent<NavMeshAgent>();
                if (navigation != null) navigation.speed = zombie.MoveSpeed;
            }
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
        }

        float GetZombieSpeedMultiplier(int line)
        {
            float lineProgress = defenseZones.Count > 1 ? line / (float)(defenseZones.Count - 1) : 1f;
            float waveProgress = Mathf.Clamp01(elapsedAtLine / Mathf.Max(1f, GetDuration(line)));
            float longRunPressure = Mathf.Lerp(1f, 1.38f, Mathf.Clamp01(missionElapsed / 360f));
            float overtimePressure = Mathf.Lerp(1f, 1.65f, GetOvertimePressure());
            return Mathf.Lerp(1f, 1.62f, lineProgress) * Mathf.Lerp(1f, 1.14f, waveProgress) *
                   longRunPressure * overtimePressure;
        }

        float GetZombieHealthMultiplier(int line)
        {
            float progress = defenseZones.Count > 1
                ? Mathf.Clamp01(line / (float)(defenseZones.Count - 1))
                : 1f;
            return Mathf.Lerp(1f, Mathf.Max(1f, finalLineZombieHealthMultiplier), progress);
        }

        float GetOvertimePressure()
        {
            float overtime = Mathf.Max(0f, elapsedAtLine - GetDuration(CurrentLine.Value));
            return Mathf.Clamp01(overtime / Mathf.Max(30f, fullOvertimePressureSeconds));
        }

        float GetSpawnIntervalMultiplier()
        {
            float longRunPressure = Mathf.Clamp01(missionElapsed / 360f);
            float intensity = 1f + longRunPressure * .55f + GetOvertimePressure() * 1.25f;
            return Mathf.Clamp(1f / intensity, .34f, 1f);
        }

        int GetLivingEnemyLimit()
        {
            if (UsesFoundation) { int cap=gameplay.balance.playerScaling.Get(gameplay.PlayerCount).maxAliveEnemies; int local=gameplay.encounter.Get(CurrentLine.Value+1)?.baseMaxAliveOverride??0; return IsSiege && local>0?Mathf.Clamp(local+10*(gameplay.PlayerCount-1),local,180):local>0?Mathf.Min(local,cap):cap; }
            int activePlayers = GetActivePlayerCount();
            float playerPressure = activePlayers <= 1 ? .72f : activePlayers >= 4 ? 1.15f : 1f;
            float pressure = Mathf.Max(GetOvertimePressure(), Mathf.Clamp01(missionElapsed / 480f));
            int baseLimit = Mathf.RoundToInt(maximumLivingEnemies * playerPressure);
            int overtimeLimit = Mathf.RoundToInt(Mathf.Max(baseLimit, maximumOvertimeLivingEnemies * playerPressure));
            return Mathf.RoundToInt(Mathf.Lerp(baseLimit, overtimeLimit, pressure));
        }

        bool ShouldSpawnFlyingDemons()
        {
            if (UsesFoundation || !spawnFlyingDemons || flyingDemonPrefab == null || defenseZones.Count == 0) return false;
            return CurrentLine.Value >= Mathf.Max(2, defenseZones.Count - 2);
        }

        int GetDemonLimit()
        {
            bool finalLine = CurrentLine.Value >= defenseZones.Count - 1;
            return finalLine ? Mathf.Max(3, finalLineMaximumDemons) : Mathf.Max(2, finalLineMaximumDemons / 3);
        }

        void SpawnFlyingDemon()
        {
            int line = Mathf.Clamp(CurrentLine.Value, 0, Mathf.Max(0, defenseZones.Count - 1));
            IReadOnlyList<Transform> anchors = GetSpawnAnchorsForLine(line);
            if (!ShouldSpawnFlyingDemons() || anchors.Count == 0) return;
            bool finalLine = CurrentLine.Value >= defenseZones.Count - 1;
            nextDemonSpawnTime = Time.time + (finalLine ? finalLineDemonInterval : finalLineDemonInterval * 1.75f) *
                                 Random.Range(.82f, 1.18f);
            Transform anchor = ChooseSpawnAnchor(anchors);
            if (anchor == null) return;
            Vector2 offset = Random.insideUnitCircle * 2.5f;
            Vector3 guess = anchor.position + new Vector3(offset.x, 0f, offset.y);
            Vector3 position = NavMesh.SamplePosition(guess, out NavMeshHit hit, 4f, NavMesh.AllAreas)
                ? hit.position : anchor.position;
            GameObject instance = Instantiate(flyingDemonPrefab, position, anchor.rotation);
            instance.GetComponent<FlyingRangedMonster>()?.ConfigureFlying();
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) networkObject.Spawn(true);
        }

        void SpawnSmallOrcWave()
        {
            nextSmallOrcSpawnTime = Time.time + smallOrcSpawnInterval;
            int count = Mathf.Max(1, GetActivePlayerCount()) * Mathf.Max(1, smallOrcsPerPlayer);
            int line = Mathf.Clamp(CurrentLine.Value, 0, Mathf.Max(0, defenseZones.Count - 1));
            IReadOnlyList<Transform> anchors = GetSpawnAnchorsForLine(line);
            if (anchors.Count == 0) return;
            for (int i = 0; i < count; i++)
            {
                Transform anchor = ChooseSpawnAnchor(anchors);
                if (anchor == null) continue;
                Vector2 offset = Random.insideUnitCircle * 2f;
                Vector3 guess = anchor.position + new Vector3(offset.x, 0f, offset.y);
                if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                GameObject instance = Instantiate(heavyZombiePrefab, hit.position + Vector3.up * .05f, anchor.rotation);
                instance.transform.localScale *= smallOrcScaleMultiplier;
                Health health = instance.GetComponent<Health>();
                if (health != null)
                {
                    health.MaxHealth *= smallOrcHealthMultiplier;
                    health.CurrentHealth = health.MaxHealth;
                }
                ZombieAI zombie = instance.GetComponent<ZombieAI>();
                if (zombie != null)
                {
                    zombie.GroundOffset *= smallOrcScaleMultiplier;
                    zombie.ConfigureLargeZombie();
                }
                NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject != null) networkObject.Spawn(true);
            }
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

        static int GetActivePlayerCount()
        {
            return MultiplayerPriceScaling.GetActivePlayerCount();
        }

        int CountLivingEnemies()
        {
            int count = 0;
            foreach (ZombieAI zombie in ZombieAI.ActiveZombies)
            {
                Health health = zombie != null ? zombie.GetComponent<Health>() : null;
                if (health != null && health.CurrentHealth > 0f) count++;
            }
            return count;
        }

        bool AnyActivePlayerInsideZone(int index)
        {
            if (index < 0 || index >= defenseZones.Count || defenseZones[index] == null) return true;
            Collider zone = defenseZones[index];
            foreach (PlayerClassController player in FindObjectsByType<PlayerClassController>())
            {
                if (player == null || !player.IsSpawned || !player.IsReady.Value || player.IsDowned.Value) continue;
                Vector3 point = player.transform.position + Vector3.up;
                if ((zone.ClosestPoint(point) - point).sqrMagnitude <= .5f * .5f) return true;
            }
            return false;
        }

        PlayerClassController FindLocalPlayer()
        {
            var player=NetworkManager?.LocalClient?.PlayerObject?.GetComponent<PlayerClassController>();
            return player!=null && player.IsReady.Value && !player.IsDowned.Value?player:null;
        }

        void CacheGatePositions()
        {
            if (closedGatePositions.Count == gates.Count) return;
            closedGatePositions.Clear();
            foreach (Transform gate in gates) closedGatePositions.Add(gate != null ? gate.localPosition : Vector3.zero);
        }

        void AnimateGates()
        {
            CacheGatePositions();
            for (int i = 0; i < gates.Count; i++)
            {
                if (gates[i] == null) continue;
                bool open = (OpenGateMask.Value & (1 << i)) != 0;
                var authored=gates[i].GetComponent<ZombieTown.Foundation.ProgressionGate>();
                if (authored!=null) { authored.ApplyMissionState(open); continue; }
                Vector3 target = closedGatePositions[i] + (open ? Vector3.up * gateOpenHeight : Vector3.zero);
                gates[i].localPosition = Vector3.MoveTowards(gates[i].localPosition, target, Time.deltaTime * 4.5f);
                foreach (NavMeshObstacle obstacle in gates[i].GetComponentsInChildren<NavMeshObstacle>(true))
                    obstacle.enabled = !open;
            }
        }

        void ApplyGatePresentation()
        {
            for (int i = 0; i < gateMarkers.Count; i++)
                if (gateMarkers[i] != null)
                {
                    if (i < gates.Count && gates[i] != null)
                        gateMarkers[i].transform.position = gates[i].position +
                            GetGateApproachDirection(i, gates[i]) * 1.5f + Vector3.up * 2.7f;
                    gateMarkers[i].SetActive(CurrentPhase == MissionPhase.UnlockGate && i == CurrentLine.Value);
                }
            for (int i = 0; i < gates.Count; i++)
            {
                bool open = (OpenGateMask.Value & (1 << i)) != 0;
                if (gates[i] == null) continue;
                foreach (NavMeshObstacle obstacle in gates[i].GetComponentsInChildren<NavMeshObstacle>(true))
                    obstacle.enabled = !open;
            }
        }

        IReadOnlyList<Transform> GetSpawnAnchorsForLine(int line)
        {
            if (line >= 0 && line < enemySpawnAnchorsByDefenseLine.Count)
            {
                SpawnAnchorGroup group = enemySpawnAnchorsByDefenseLine[line];
                if (group != null && group.Anchors != null && group.Anchors.Count > 0)
                    return group.Anchors;
            }

            if (line >= 0 && line < enemySpawnAnchors.Count && enemySpawnAnchors[line] != null)
                return new[] { enemySpawnAnchors[line] };
            return System.Array.Empty<Transform>();
        }

        static Transform ChooseSpawnAnchor(IReadOnlyList<Transform> anchors)
        {
            if (anchors == null || anchors.Count == 0) return null;
            int start = Random.Range(0, anchors.Count);
            for (int offset = 0; offset < anchors.Count; offset++)
            {
                Transform candidate = anchors[(start + offset) % anchors.Count];
                if (candidate != null && candidate.gameObject.activeInHierarchy) return candidate;
            }
            return null;
        }

        float GetDuration(int line) => UsesFoundation ? Mathf.Max(1,gameplay.encounter.Get(line+1).waveDuration) : line >= 0 && line < defenseDurations.Count
            ? Mathf.Max(10f, defenseDurations[line]) : 60f;
        int GetPressureThreshold(int line) => line >= 0 && line < pressureThresholds.Count
            ? Mathf.Max(5, pressureThresholds[line]) : 40;
        int GetGateCost(int gate)
        {
            if (UsesFoundation && gate>=0 && gate<gates.Count && gates[gate]!=null && gates[gate].TryGetComponent<ZombieTown.Foundation.ProgressionGate>(out var authored)) return authored.ResolveCost(gameplay.PlayerCount);
            int baseCost = gate >= 0 && gate < gateCosts.Count ? Mathf.Max(0, gateCosts[gate]) : 200;
            int activePlayers = GetActivePlayerCount();
            float multiplier = activePlayers <= 1 ? .5f : activePlayers >= 4 ? 2f : 1f;
            return Mathf.Max(0, Mathf.RoundToInt(baseCost * multiplier));
        }

        int GetGuardHirePrice()
        {
            return MultiplayerPriceScaling.Scale(guardHirePrice, GetActivePlayerCount());
        }

        void OnByteChanged(byte previous, byte current)
        {
            ApplyGatePresentation();
            RefreshObjective();
        }

        void OnIntChanged(int previous, int current)
        {
            ApplyGatePresentation();
            RefreshObjective();
        }

        void RefreshObjective()
        {
            if (objective == null) return;
            string lineName = CurrentLine.Value switch
            {
                0 => GameLocalization.Text("Forward yard", "Voorplein"),
                1 => GameLocalization.Text("Concrete checkpoint", "Betonnen checkpoint"),
                2 => GameLocalization.Text("Trench line", "Loopgraaflinie"),
                _ => GameLocalization.Text("Last bunker", "Laatste bunker")
            };

            switch (CurrentPhase)
            {
                case MissionPhase.Prepare:
                    objective.UpdateObjective(GameLocalization.Text("PREPARE: ","VOORBEREIDEN: ")+lineName, SecondsRemaining.Value+"s", GameLocalization.Text("Place defenses, buy supplies and prepare your fallback route","Plaats verdediging, koop voorraad en bereid je terugtrekroute voor"));
                    break;
                case MissionPhase.Defend:
                    objective.UpdateObjective(
                        GameLocalization.Text($"Defend: {lineName}", $"Verdedig: {lineName}"),
                        $"{SecondsRemaining.Value / 60:00}:{SecondsRemaining.Value % 60:00}",
                        CurrentLine.Value >= defenseZones.Count - 1
                            ? GameLocalization.Text("Hold the last bunker", "Houd de laatste bunker")
                            : GameLocalization.Text("If pressure gets too high, fall back", "Trek terug als de druk te hoog wordt"));
                    break;
                case MissionPhase.UnlockGate:
                    int gate = CurrentLine.Value;
                    objective.UpdateObjective(
                        GameLocalization.Text("Unlock the fallback gate", "Ontgrendel de terugtrekpoort"),
                        $"$ {GetGateCost(gate)}",
                        GameLocalization.Text("Follow the yellow marker and press F", "Volg de gele marker en druk F"));
                    break;
                case MissionPhase.FallBack:
                    objective.UpdateObjective(
                        GameLocalization.Text($"Fall back to: {lineName}", $"Trek terug naar: {lineName}"),
                        string.Empty,
                        GameLocalization.Text("Enter the next marked defense area", "Ga het volgende gemarkeerde verdedigingsvak in"));
                    break;
                case MissionPhase.Complete:
                    if (!localCompleted)
                    {
                        localCompleted = true;
                        objective.CompleteObjective(string.Empty, string.Empty,
                            GameLocalization.Text("Fallback line secured", "Terugtreklinie veiliggesteld"));
                    }
                    break;
            }
        }
    }
}
