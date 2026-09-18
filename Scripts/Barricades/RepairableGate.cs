using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using ZombieTown.Multiplayer;

namespace ZombieTown.Foundation
{
    /// <summary>A damageable defense, independent of paid progression doors. Local +Z is safe.</summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(NetworkObject), typeof(Health))]
    public sealed class RepairableGate : NetworkBehaviour
    {
        [Header("Balance")]
        [Min(1)] public float maximumHealth = 240;
        [Min(0)] public float repairHealthPerSecond = 20;
        [Tooltip("Multiplies the attacking zombie's existing AttackDamage.")]
        [Min(0)] public float zombieDamageMultiplier = 1;
        [Range(.05f, .9f)] public float warningThreshold = .3f;
        [Min(.1f)] public float minimumWarningSeconds = 2;
        public bool allowRebuildAfterBreach = true;
        public DefenseSupplyProfile supplies;
        public bool requireCarriedPlanks;
        public GameObject[] reinforcementPlanks;
        public readonly NetworkVariable<float> PlankArmor=new();
        public bool TryInstallPlank(PlayerClassController player) {
            if(!IsServer || player==null || repairPoint==null || !IsSafeSide(player.transform.position) ||
                Vector3.Distance(player.transform.position,repairPoint.position)>interactionRadius || OpeningOccupied())return false;
            if(!IsConstructed && !Construct())return false;
            if(IsBreached){health.ReviveAndSetHealth(maximumHealth*.25f);SyncedHealth.Value=health.CurrentHealth;SetBreached(false);PlankPlacedRpc();return true;}
            if(CurrentHealth<maximumHealth){health.Heal(60);PlankPlacedRpc();return true;}
            float capacity=(reinforcementPlanks?.Length??0)*60;
            if(PlankArmor.Value>=capacity)return false;
            PlankArmor.Value=Mathf.Min(capacity,PlankArmor.Value+60);PlankPlacedRpc();return true;
        }
        [Rpc(SendTo.ClientsAndHost)]
        void PlankPlacedRpc()=>DefenseAudioSettings.Play(DefenseAudioSettings.Cue.Placed,transform.position);
        public bool requiresConstruction;
        [Tooltip("Use only for the single entrance of a sealed front wall, including players on its walkway.")]
        public bool defendEntireFront;
        public readonly NetworkVariable<bool> Constructed = new();
        public bool IsConstructed => !requiresConstruction || Constructed.Value;
        [Range(.05f, 1)] public float rebuildHealthFraction = .25f;

        [Header("Prefab-local references: +Z safe, -Z attack")]
        public Transform attackPoint;
        public Transform repairPoint;
        public BoxCollider passageBlocker;
        public BoxCollider openingClearance;
        public NavMeshObstacle navigationObstacle;
        public Transform gateVisual;
        public Renderer[] damageRenderers;
        public bool preserveMaterialColors;
        public AudioSource audioSource;
        public AudioClip hitSound, warningSound, breachSound;
        [Min(.5f)] public float interactionRadius = 2.5f;
        [Min(1)] public float zombieAcquireDistance = 12;
        [Min(.1f)] public float zombieAttackReach = 1.6f;

        public readonly NetworkVariable<float> SyncedHealth = new();
        public readonly NetworkVariable<bool> Breached = new();
        static readonly List<RepairableGate> Gates = new();
        readonly Dictionary<PlayerClassController, float> repairLeases = new();
        readonly List<PlayerClassController> expiredLeases = new();
        readonly Dictionary<ZombieAI, float> nextAttacks = new();
        readonly List<ZombieAI> staleAttackers = new();
        readonly Collider[] clearanceHits = new Collider[64];
        Health health;
        bool offlineBreached;
        float warningStarted = -1, impactTime = -10, nextHeartbeat, nextPrune;
        PlayerClassController localRepairer;
        PlayerInputHandler localInput;
        Vector3 visualPosition;
        Quaternion visualRotation;
        MaterialPropertyBlock materialProperties;

        public bool IsDamageAuthority => !IsNetworkRunning || IsServer;
        bool IsNetworkRunning => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        public bool IsBreached => !IsConstructed || (IsSpawned ? Breached.Value : offlineBreached);
        public float CurrentHealth => IsSpawned ? SyncedHealth.Value : health != null ? health.CurrentHealth : maximumHealth;
        public float HealthFraction => Mathf.Clamp01(CurrentHealth / Mathf.Max(1, maximumHealth));
        public bool IsWarning => !IsBreached && HealthFraction <= warningThreshold;

        void Awake()
        {
            health = GetComponent<Health>();
            health.MaxHealth = Mathf.Max(1, maximumHealth);
            health.CurrentHealth = health.MaxHealth;
            health.OnDamaged += OnDamaged;
            health.OnHealed += OnHealed;
            if (gateVisual != null) { visualPosition = gateVisual.localPosition; visualRotation = gateVisual.localRotation; }
            materialProperties = new MaterialPropertyBlock();
        }

        void OnEnable() { if (!Gates.Contains(this)) Gates.Add(this); }
        void OnDisable()
        {
            Gates.Remove(this);
            ReleaseLocalRepair();
            foreach (var lease in repairLeases) if (lease.Key != null) lease.Key.EndGateRepair(this);
            repairLeases.Clear();
        }
        public override void OnDestroy()
        {
            if (health != null) { health.OnDamaged -= OnDamaged; health.OnHealed -= OnHealed; }
            base.OnDestroy();
        }
        public override void OnNetworkSpawn()
        {
            if (IsServer) { SyncedHealth.Value = health.CurrentHealth; Breached.Value = offlineBreached; }
            Constructed.OnValueChanged += ConstructionChanged;
            PlankArmor.OnValueChanged += OnArmorChanged;
            SyncedHealth.OnValueChanged += OnHealthChanged;
            Breached.OnValueChanged += OnBreachChanged;
            ApplyPresentation();
        }
        public override void OnNetworkDespawn()
        {
            Constructed.OnValueChanged -= ConstructionChanged;
            PlankArmor.OnValueChanged -= OnArmorChanged;
            SyncedHealth.OnValueChanged -= OnHealthChanged;
            Breached.OnValueChanged -= OnBreachChanged;
            ReleaseLocalRepair();
            foreach (var lease in repairLeases) if (lease.Key != null) lease.Key.EndGateRepair(this);
            repairLeases.Clear();
        }
        void OnHealthChanged(float oldValue, float value)
        {
            if (!IsServer) health.CurrentHealth = value;
            if (value < oldValue) { impactTime = Time.time; Play(hitSound); }
            if (DefenseAudioSettings.CrossedDamageStep(oldValue,value,maximumHealth)) DefenseAudioSettings.Play(DefenseAudioSettings.Cue.Gate,transform.position);
            if (oldValue / maximumHealth > warningThreshold && value / maximumHealth <= warningThreshold) Play(warningSound);
            ApplyPresentation();
        }
        void OnBreachChanged(bool before, bool after) { if (after) Play(breachSound); ApplyPresentation(); }
        void OnDamaged(float amount, GameObject source)
        {
            if (!IsDamageAuthority) return;
            if (IsSpawned) SyncedHealth.Value = health.CurrentHealth;
            else { impactTime = Time.time; Play(hitSound); if(DefenseAudioSettings.CrossedDamageStep(health.CurrentHealth+amount,health.CurrentHealth,maximumHealth)) DefenseAudioSettings.Play(DefenseAudioSettings.Cue.Gate,transform.position); }
            if (health.CurrentHealth <= 0) SetBreached(true);
            ApplyPresentation();
        }
        void OnHealed(float amount) { if (IsDamageAuthority && IsSpawned) SyncedHealth.Value = health.CurrentHealth; ApplyPresentation(); }
        void SetBreached(bool value)
        {
            offlineBreached = value;
            if (IsSpawned && IsServer) Breached.Value = value;
            if (!IsSpawned && value) Play(breachSound);
            ApplyPresentation();
        }
        void Play(AudioClip clip) { if (clip != null && audioSource != null) audioSource.PlayOneShot(clip); }

        public bool OpeningOccupied()
        {
            if (openingClearance == null) return true; // Fail closed when the authoring reference is missing.
            Vector3 half = Vector3.Scale(openingClearance.size * .5f, Abs(openingClearance.transform.lossyScale));
            int count = Physics.OverlapBoxNonAlloc(openingClearance.transform.TransformPoint(openingClearance.center), half,
                clearanceHits, openingClearance.transform.rotation, ~0, QueryTriggerInteraction.Collide);
            if (count == clearanceHits.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var hit = clearanceHits[i];
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                if (hit.GetComponentInParent<ZombieAI>() != null || hit.GetComponentInParent<PlayerCharacterController>() != null) return true;
            }
            return false;
        }
        static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        public bool IsSafeSide(Vector3 position) => transform.InverseTransformPoint(position).z > .1f;
        public bool CanRepairAt(Vector3 position)
        {
            return IsConstructed && repairPoint != null && IsSafeSide(position) && Vector3.Distance(position, repairPoint.position) <= interactionRadius &&
                   CurrentHealth < maximumHealth && (!IsBreached || (allowRebuildAfterBreach && !OpeningOccupied()));
        }

        // All entry points resolve damage and rate from the server's own zombie, never a client float.
        public bool TryZombieAttack(ZombieAI zombie)
        {
            if (!IsDamageAuthority || IsBreached || zombie == null || attackPoint == null ||
                zombie.GetComponent<Health>() is not Health zombieHealth || zombieHealth.CurrentHealth <= 0 ||
                IsSafeSide(zombie.transform.position) || Vector3.Distance(zombie.transform.position, attackPoint.position) > zombieAttackReach) return false;
            if (nextAttacks.TryGetValue(zombie, out float next) && Time.time < next) return false;
            nextAttacks[zombie] = Time.time + Mathf.Max(.2f, zombie.AttackInterval);
            zombie.GetComponent<NetworkZombieAnimator>()?.PlayAttack();
            float damage = Mathf.Max(0, zombie.AttackDamage * zombieDamageMultiplier);
            if(IsServer && PlankArmor.Value>0){float absorbed=Mathf.Min(damage,PlankArmor.Value);PlankArmor.Value-=absorbed;damage-=absorbed;}
            float warningHealth = maximumHealth * warningThreshold;
            if (health.CurrentHealth - damage <= warningHealth && warningStarted < 0)
            {
                warningStarted = Time.time;
                damage = Mathf.Min(damage, Mathf.Max(0, health.CurrentHealth - warningHealth));
                if (!IsSpawned) Play(warningSound);
            }
            if (warningStarted >= 0 && Time.time - warningStarted < minimumWarningSeconds)
                damage = Mathf.Min(damage, Mathf.Max(0, health.CurrentHealth - 1));
            health.TakeDamage(damage, zombie.gameObject);
            return true;
        }

        public static bool TryHandleZombie(ZombieAI zombie, Transform target, NavMeshAgent agent)
        {
            if (zombie == null || target == null || agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
            RepairableGate nearest = null; float bestDistance = float.PositiveInfinity;
            foreach (var gate in Gates)
            {
                if (gate == null || !gate.IsDamageAuthority || gate.IsBreached || gate.attackPoint == null || gate.passageBlocker == null) continue;
                Vector3 a = gate.transform.InverseTransformPoint(zombie.transform.position);
                Vector3 b = gate.transform.InverseTransformPoint(target.position);
                if (a.z > .1f || b.z <= .1f || Mathf.Abs(a.y) > 3) continue;
                float t = -a.z / (b.z - a.z);
                Vector3 crossing = Vector3.Lerp(a, b, t);
                // Only intercept a route that actually crosses this opening, not another lane or side room.
                Vector3 point = gate.passageBlocker.transform.InverseTransformPoint(gate.transform.TransformPoint(crossing));
                if (!gate.defendEntireFront && Mathf.Abs(point.x - gate.passageBlocker.center.x) > gate.passageBlocker.size.x * .5f + .5f) continue;
                float distance = Vector3.Distance(zombie.transform.position, gate.attackPoint.position);
                if (distance <= gate.zombieAcquireDistance && distance < bestDistance) { nearest = gate; bestDistance = distance; }
            }
            if (nearest == null) return false;
            agent.stoppingDistance = .3f;
            if (bestDistance > nearest.zombieAttackReach)
            {
                agent.isStopped = false; agent.SetDestination(nearest.attackPoint.position);
                if (!agent.updatePosition) zombie.transform.position = agent.nextPosition + Vector3.up * zombie.GroundOffset;
                return true;
            }
            agent.isStopped = true;
            Vector3 direction = nearest.transform.forward; direction.y = 0;
            zombie.transform.rotation = Quaternion.RotateTowards(zombie.transform.rotation, Quaternion.LookRotation(direction), 360 * Time.deltaTime);
            nearest.TryZombieAttack(zombie);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetRepairHeldRpc(bool held, RpcParams rpc = default)
        {
            if(requireCarriedPlanks && held)return;
            if (!held)
            {
                foreach (var p in repairLeases.Keys) if (p != null && p.OwnerClientId == rpc.Receive.SenderClientId) { p.EndGateRepair(this); repairLeases.Remove(p); break; }
                return;
            }
            if (repairPoint == null || !FoundationInteraction.TryPlayer(rpc.Receive.SenderClientId, repairPoint.position, interactionRadius, out var player) || !CanRepairAt(player.transform.position)) return;
            if (!player.BeginGateRepair(this)) return;
            repairLeases[player] = Time.time + .3f;
        }
        public bool HasRepairLease(PlayerClassController player) => player != null && repairLeases.TryGetValue(player, out float until) && until >= Time.time && CanRepairAt(player.transform.position);

        // Used by the same timed server loop for both intact repairs and deliberate reconstruction.
        public bool RepairForDuration(float seconds, PlayerClassController payer = null)
        {
            if (!IsConstructed || !IsDamageAuthority || seconds <= 0 || CurrentHealth >= maximumHealth || (IsBreached && (!allowRebuildAfterBreach || OpeningOccupied()))) return false;
            float amount = Mathf.Min(maximumHealth - CurrentHealth, repairHealthPerSecond * Mathf.Min(seconds, .1f));
            if (amount <= 0) return false;
            if(supplies!=null && (payer==null || !payer.TryPayRepair(amount,supplies)))return false;
            if (health.CurrentHealth <= 0) health.ReviveAndSetHealth(amount);
            else health.Heal(amount);
            if (IsSpawned) SyncedHealth.Value = health.CurrentHealth;
            if (IsBreached && health.CurrentHealth >= maximumHealth * rebuildHealthFraction && !OpeningOccupied()) SetBreached(false);
            if (HealthFraction > warningThreshold) warningStarted = -1;
            ApplyPresentation();
            return true;
        }

        void Update()
        {
            if (IsSpawned)
            {
                var player = repairPoint != null ? FoundationInteraction.Local(repairPoint.position, interactionRadius) : null;
                var input = player != null ? player.GetComponent<PlayerInputHandler>() : null;
                bool held = !requireCarriedPlanks && player != null && !player.IsUsingMountedGun && input != null && input.CanProcessInput() && FoundationInteraction.Held &&
                            CanRepairAt(player.transform.position) && IsNearestRepairGate(player.transform.position);
                if (held)
                {
                    localRepairer = player; localInput = input; input.SetRepairFireBlocked(true);
                    if (Time.time >= nextHeartbeat) { nextHeartbeat = Time.time + .1f; SetRepairHeldRpc(true); }
                }
                else ReleaseLocalRepair();
            }
            if (IsDamageAuthority)
            {
                expiredLeases.Clear(); PlayerClassController payer = null;
                foreach (var lease in repairLeases)
                {
                    if (lease.Key == null || !lease.Key.IsSpawned || lease.Key.IsDowned.Value || !lease.Key.IsReady.Value || !lease.Key.RoundStarted.Value || !HasRepairLease(lease.Key)) expiredLeases.Add(lease.Key);
                    else if(payer==null)payer=lease.Key;
                }
                foreach (var p in expiredLeases) { if (p != null) p.EndGateRepair(this); repairLeases.Remove(p); }
                // Multiple players do not multiply free repair throughput.
                if (payer!=null) RepairForDuration(Time.deltaTime,payer);
                if (Time.time >= nextPrune)
                {
                    nextPrune = Time.time + 2;
                    staleAttackers.Clear(); foreach (var pair in nextAttacks) if (pair.Key == null || pair.Value + 5 < Time.time) staleAttackers.Add(pair.Key);
                    foreach (var zombie in staleAttackers) nextAttacks.Remove(zombie);
                }
            }
            if (gateVisual != null && !IsBreached)
            {
                float shake = IsWarning ? Mathf.Sin(Time.time * 24) * .025f : Mathf.Max(0, .15f - (Time.time - impactTime)) * .18f;
                gateVisual.localPosition = visualPosition + Vector3.forward * shake;
                gateVisual.localRotation = visualRotation * Quaternion.Euler(0, 0, (1 - HealthFraction) * 4);
            }
        }
        bool IsNearestRepairGate(Vector3 position)
        {
            float distance = (repairPoint.position - position).sqrMagnitude;
            foreach (var other in Gates)
                if (other != null && other != this && other.CanRepairAt(position) &&
                    (other.repairPoint.position - position).sqrMagnitude < distance) return false;
            return true;
        }
        void ReleaseLocalRepair()
        {
            if (localInput != null) localInput.SetRepairFireBlocked(false);
            if (localRepairer != null && IsSpawned && IsClient) SetRepairHeldRpc(false);
            localRepairer = null; localInput = null;
        }
        public bool Construct() {
            if(!IsServer || IsConstructed || OpeningOccupied())return false;
            Constructed.Value=true;health.ReviveAndSetHealth(maximumHealth);SyncedHealth.Value=maximumHealth;SetBreached(false);return true;
        }
        void OnArmorChanged(float before,float after)=>ApplyPresentation();
        void ConstructionChanged(bool before,bool after)=>ApplyPresentation();
        void ApplyPresentation()
        {
            if(reinforcementPlanks!=null)for(int i=0;i<reinforcementPlanks.Length;i++)if(reinforcementPlanks[i]!=null)reinforcementPlanks[i].SetActive(PlankArmor.Value>i*60);
            bool blocked = !IsBreached;
            if (passageBlocker != null) passageBlocker.enabled = blocked;
            if (navigationObstacle != null) navigationObstacle.enabled = blocked;
            if (gateVisual != null) gateVisual.gameObject.SetActive(blocked);
            if (damageRenderers == null || materialProperties == null) return;
            Color color = IsWarning ? new Color(.85f, .15f, .035f) : Color.Lerp(new Color(.52f, .27f, .09f), Color.white, HealthFraction);
            materialProperties.SetColor("_BaseColor", color); materialProperties.SetColor("_Color", color);
            foreach (var renderer in damageRenderers) if (renderer != null) {
                Color tint=color;
                if(preserveMaterialColors && renderer.sharedMaterial!=null) {
                    var mat=renderer.sharedMaterial;
                    if(mat.HasProperty("_BaseColor"))tint*=mat.GetColor("_BaseColor");
                    else if(mat.HasProperty("_Color"))tint*=mat.GetColor("_Color");
                }
                materialProperties.SetColor("_BaseColor",tint);materialProperties.SetColor("_Color",tint);
                renderer.SetPropertyBlock(materialProperties);
            }
        }
        void OnGUI()
        {
            var player = repairPoint != null ? FoundationInteraction.Local(repairPoint.position, interactionRadius) : null;
            if (!IsConstructed || player == null || player.IsUsingMountedGun || !IsSafeSide(player.transform.position)) return;
            if(requireCarriedPlanks){FoundationInteraction.Prompt(GameLocalization.Text("Bring a plank; F to reinforce or repair this gate","Breng een plank; F om deze poort te versterken of repareren"));return;}
            string hint = IsBreached ? !allowRebuildAfterBreach ? "BREACHED - FALL BACK" : OpeningOccupied() ? "OPENING BLOCKED - CANNOT REBUILD" : "HOLD F TO REBUILD" : HealthFraction >= 1 ? "DEFENSE READY" : "HOLD F TO REPAIR - FIRING DISABLED";
            FoundationInteraction.Prompt($"{(IsWarning ? "WARNING: GATE ABOUT TO BREAK\n" : "")}{CurrentHealth:0}/{maximumHealth:0}\n{hint}");
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red; if (attackPoint != null) { Gizmos.DrawWireSphere(attackPoint.position, .35f); Gizmos.DrawLine(attackPoint.position, transform.position); }
            Gizmos.color = Color.green; if (repairPoint != null) Gizmos.DrawWireSphere(repairPoint.position, interactionRadius);
#if UNITY_EDITOR
            if (attackPoint != null) UnityEditor.Handles.Label(attackPoint.position + Vector3.up, "ATTACK SIDE (-Z)");
            if (repairPoint != null) UnityEditor.Handles.Label(repairPoint.position + Vector3.up, "SAFE SIDE (+Z) / HOLD F");
#endif
        }
    }
}
