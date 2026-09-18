using System.Collections;
using System.Collections.Generic;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using ZombieTown.Enemies;
using ZombieTown.LevelTwo;
using ZombieTown.Multiplayer;

namespace ZombieTown.LevelFour
{
    [RequireComponent(typeof(NetworkObject), typeof(Health), typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public sealed class FriendlyGuardAI : NetworkBehaviour
    {
        public const ulong UnhiredClientId = ulong.MaxValue;
        static readonly List<FriendlyGuardAI> Active = new();
        static readonly List<Health> CachedMonsters = new();
        static float nextMonsterCacheRefresh;
        public static IReadOnlyList<FriendlyGuardAI> ActiveGuards => Active;

        [Header("References")]
        [SerializeField] Animator guardAnimator;
        [SerializeField] Transform muzzle;
        [SerializeField] AudioSource shotAudio;
        [SerializeField] OutpostGuardWeaponIK weaponPose;

        [Header("Combat")]
        [SerializeField, Min(1f)] float maximumHealth = 180f;
        [SerializeField, Min(1f)] float weaponDamage = 24f;
        [SerializeField, Min(.1f)] float fireInterval = .42f;
        [SerializeField, Min(2f)] float attackRange = 28f;
        [SerializeField, Min(1f)] float combatLeash = 15f;

        [Header("Movement")]
        [SerializeField, Min(.5f)] float followDistance = 4.8f;
        [SerializeField, Min(.5f)] float movementSpeed = 4.1f;

        public readonly NetworkVariable<ulong> HiredClientId = new(UnhiredClientId);
        public readonly NetworkVariable<bool> IsHoldingPosition = new();
        public readonly NetworkVariable<Vector3> HoldPosition = new();
        public readonly NetworkVariable<bool> IsDead = new();
        public readonly NetworkVariable<float> SyncedHealth = new();
        public readonly NetworkVariable<int> SquadSlot = new();

        Health health;
        NavMeshAgent navigation;
        Collider bodyCollider;
        Health combatTarget;
        float nextTargetScan;
        float nextShot;
        float nextFollowRepath;
        Vector3 lastVisualPosition;
        GameObject commandIndicator;
        LineRenderer commandRing;

        static readonly int WalkingHash = Animator.StringToHash("IsWalking");
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int HitHash = Animator.StringToHash("Hit");
        static readonly int DeadHash = Animator.StringToHash("IsDead");
        static readonly int DieHash = Animator.StringToHash("Die");

        public bool IsHired => HiredClientId.Value != UnhiredClientId;
        public bool IsAlive => !IsDead.Value && SyncedHealth.Value > 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetRegistry()
        {
            Active.Clear();
            CachedMonsters.Clear();
            nextMonsterCacheRefresh = 0f;
        }

        public void ConfigurePresentation(Animator animator, Transform shotOrigin, AudioSource audio,
            OutpostGuardWeaponIK pose)
        {
            guardAnimator = animator;
            muzzle = shotOrigin;
            shotAudio = audio;
            weaponPose = pose;
        }

        void Awake()
        {
            health = GetComponent<Health>();
            navigation = GetComponent<NavMeshAgent>();
            bodyCollider = GetComponent<Collider>();
            lastVisualPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            if (!Active.Contains(this)) Active.Add(this);
            IsDead.OnValueChanged += OnDeadChanged;
            IsHoldingPosition.OnValueChanged += OnHoldingChanged;
            HoldPosition.OnValueChanged += OnHoldPositionChanged;
            if (IsServer)
            {
                health.MaxHealth = maximumHealth;
                health.ReviveAndSetHealth(maximumHealth);
                SyncedHealth.Value = health.CurrentHealth;
                health.OnDamaged += OnDamaged;
                health.OnDie += OnDied;
            }
            else if (navigation != null)
            {
                navigation.enabled = false;
            }

            ApplyDeadPresentation(IsDead.Value);
            RefreshCommandIndicator();
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);
            IsDead.OnValueChanged -= OnDeadChanged;
            IsHoldingPosition.OnValueChanged -= OnHoldingChanged;
            HoldPosition.OnValueChanged -= OnHoldPositionChanged;
            if (health != null)
            {
                health.OnDamaged -= OnDamaged;
                health.OnDie -= OnDied;
            }
            DestroyCommandIndicator();
        }

        public override void OnDestroy()
        {
            Active.Remove(this);
            DestroyCommandIndicator();
            base.OnDestroy();
        }

        public void InitializeHire(ulong clientId, Vector3 startPosition, int squadSlot)
        {
            if (!IsServer || IsDead.Value) return;
            HiredClientId.Value = clientId;
            SquadSlot.Value = Mathf.Max(0, squadSlot);
            IsHoldingPosition.Value = false;
            if (NavMesh.SamplePosition(startPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                if (navigation != null && navigation.enabled) navigation.Warp(hit.position);
            }
        }

        public void SetHoldCommand(bool hold, Vector3 worldPosition)
        {
            if (!IsServer || !IsHired || IsDead.Value) return;
            IsHoldingPosition.Value = hold;
            if (hold)
            {
                HoldPosition.Value = worldPosition;
                SetDestination(worldPosition, .25f);
            }
        }

        void Update()
        {
            UpdateAnimation();
            if (!IsServer || !IsSpawned || !IsHired || IsDead.Value || !NetworkRoundGate.IsOpen) return;

            Transform owner = ResolveOwner();
            if (owner == null) return;
            Vector3 anchor = IsHoldingPosition.Value ? HoldPosition.Value : GetFollowPoint(owner);

            if (Time.time >= nextTargetScan || !IsValidMonster(combatTarget))
            {
                nextTargetScan = Time.time + .3f;
                combatTarget = FindNearestMonster(anchor);
            }

            if (combatTarget != null)
            {
                Vector3 targetPoint = combatTarget.transform.position + Vector3.up;
                float targetDistance = Vector3.Distance(transform.position, targetPoint);
                Face(targetPoint);
                weaponPose?.SetAimPoint(targetPoint);
                if (targetDistance <= attackRange && Time.time >= nextShot && HasLineOfSight(targetPoint))
                {
                    nextShot = Time.time + fireInterval;
                    GameObject source = ResolveOwner()?.gameObject ?? gameObject;
                    combatTarget.TakeDamage(weaponDamage, source);
                    FirePresentationRpc(GetMuzzlePosition(), targetPoint);
                }
            }
            else
            {
                weaponPose?.ClearAimPoint();
            }

            float distanceToAnchor = Vector3.Distance(transform.position, anchor);
            float startMovingDistance = IsHoldingPosition.Value ? .55f : 1.45f;
            float stopDistance = IsHoldingPosition.Value ? .2f : .85f;
            if (distanceToAnchor > startMovingDistance &&
                (IsHoldingPosition.Value || Time.time >= nextFollowRepath))
            {
                nextFollowRepath = Time.time + .28f;
                SetDestination(anchor, stopDistance);
            }
            else if (distanceToAnchor <= stopDistance && navigation != null && navigation.enabled)
                navigation.isStopped = true;
        }

        Vector3 GetFollowPoint(Transform owner)
        {
            int slot = Mathf.Max(0, SquadSlot.Value);
            int row = slot / 2;
            float side = slot % 2 == 0 ? -1f : 1f;
            float lateral = side * (1.15f + row * .45f);
            float rearDistance = followDistance + row * 1.45f;
            Vector3 behind = owner.position - owner.forward * rearDistance + owner.right * lateral;
            return NavMesh.SamplePosition(behind, out NavMeshHit hit, 3f, NavMesh.AllAreas)
                ? hit.position : owner.position;
        }

        void SetDestination(Vector3 position, float stoppingDistance)
        {
            if (navigation == null || !navigation.enabled || !navigation.isOnNavMesh) return;
            navigation.speed = movementSpeed;
            navigation.stoppingDistance = stoppingDistance;
            navigation.isStopped = false;
            navigation.SetDestination(position);
        }

        Transform ResolveOwner()
        {
            if (NetworkManager == null || HiredClientId.Value == UnhiredClientId ||
                !NetworkManager.ConnectedClients.TryGetValue(HiredClientId.Value, out NetworkClient client) ||
                client.PlayerObject == null)
                return null;
            PlayerClassController player = client.PlayerObject.GetComponent<PlayerClassController>();
            return player != null && player.IsReady.Value && !player.IsDowned.Value ? player.transform : null;
        }

        Health FindNearestMonster(Vector3 anchor)
        {
            RefreshMonsterCache();
            Health best = null;
            float bestDistance = attackRange * attackRange;
            foreach (Health monster in CachedMonsters)
                EvaluateMonster(monster, anchor, ref best, ref bestDistance);
            return best;
        }

        static void RefreshMonsterCache()
        {
            if (Time.time < nextMonsterCacheRefresh) return;
            nextMonsterCacheRefresh = Time.time + .25f;
            CachedMonsters.Clear();
            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
            {
                Health candidate = zombie != null ? zombie.GetComponent<Health>() : null;
                if (candidate != null) CachedMonsters.Add(candidate);
            }
            foreach (FlyingRangedMonster demon in FindObjectsByType<FlyingRangedMonster>())
            {
                Health candidate = demon != null ? demon.GetComponent<Health>() : null;
                if (candidate != null && !CachedMonsters.Contains(candidate)) CachedMonsters.Add(candidate);
            }
        }

        void EvaluateMonster(Health candidate, Vector3 anchor, ref Health best, ref float bestDistance)
        {
            if (!IsValidMonster(candidate) || Vector3.Distance(anchor, candidate.transform.position) > combatLeash) return;
            float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (sqr >= bestDistance) return;
            bestDistance = sqr;
            best = candidate;
        }

        static bool IsValidMonster(Health candidate) => candidate != null && candidate.CurrentHealth > 0f &&
            (candidate.GetComponent<ZombieAI>() != null || candidate.GetComponent<FlyingRangedMonster>() != null);

        bool HasLineOfSight(Vector3 targetPoint)
        {
            Vector3 start = GetMuzzlePosition();
            Vector3 direction = targetPoint - start;
            float distance = direction.magnitude;
            if (distance < .1f) return true;
            if (!Physics.Raycast(start, direction / distance, out RaycastHit hit, distance, ~0,
                    QueryTriggerInteraction.Ignore)) return true;
            return hit.collider != null && (hit.collider.GetComponentInParent<ZombieAI>() != null ||
                                            hit.collider.GetComponentInParent<FlyingRangedMonster>() != null);
        }

        Vector3 GetMuzzlePosition() => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.35f;

        void Face(Vector3 point)
        {
            Vector3 look = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (look.sqrMagnitude > .01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look),
                    Time.deltaTime * 8f);
        }

        void OnDamaged(float amount, GameObject source)
        {
            if (!IsServer) return;
            SyncedHealth.Value = health.CurrentHealth;
            if (health.CurrentHealth > 0f) HitPresentationRpc();
        }

        void OnDied()
        {
            if (!IsServer || IsDead.Value) return;
            SyncedHealth.Value = 0f;
            IsDead.Value = true;
            StartCoroutine(DespawnAfterDelay());
        }

        IEnumerator DespawnAfterDelay()
        {
            yield return new WaitForSeconds(8f);
            if (IsSpawned) NetworkObject.Despawn(true);
        }

        void OnDeadChanged(bool previous, bool current) => ApplyDeadPresentation(current);

        void OnHoldingChanged(bool previous, bool current) => RefreshCommandIndicator();

        void OnHoldPositionChanged(Vector3 previous, Vector3 current) => RefreshCommandIndicator();

        void ApplyDeadPresentation(bool dead)
        {
            if (bodyCollider != null) bodyCollider.enabled = !dead;
            if (navigation != null && navigation.enabled)
            {
                navigation.isStopped = true;
                if (dead) navigation.enabled = false;
            }
            if (guardAnimator != null)
            {
                guardAnimator.SetBool(DeadHash, dead);
                if (dead) guardAnimator.SetTrigger(DieHash);
            }
            if (dead) weaponPose?.SetDead();
            RefreshCommandIndicator();
        }

        void RefreshCommandIndicator()
        {
            bool visible = IsSpawned && IsHoldingPosition.Value && !IsDead.Value &&
                           NetworkManager != null && HiredClientId.Value == NetworkManager.LocalClientId;
            if (!visible)
            {
                if (commandIndicator != null) commandIndicator.SetActive(false);
                return;
            }

            if (commandIndicator == null)
            {
                commandIndicator = new GameObject($"Guard Command Ring {NetworkObjectId}");
                commandRing = commandIndicator.AddComponent<LineRenderer>();
                commandRing.useWorldSpace = true;
                commandRing.loop = true;
                commandRing.positionCount = 32;
                commandRing.startWidth = .075f;
                commandRing.endWidth = .075f;
                commandRing.numCornerVertices = 3;
                commandRing.numCapVertices = 3;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) commandRing.material = new Material(shader);
                Color color = Color.Lerp(new Color(.1f, .75f, 1f, .92f),
                    new Color(1f, .72f, .12f, .92f), (SquadSlot.Value % 4) / 3f);
                commandRing.startColor = color;
                commandRing.endColor = color;
            }

            commandIndicator.SetActive(true);
            const float radius = .68f;
            Vector3 center = HoldPosition.Value + Vector3.up * .065f;
            for (int i = 0; i < commandRing.positionCount; i++)
            {
                float angle = i / (float)commandRing.positionCount * Mathf.PI * 2f;
                commandRing.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
        }

        void DestroyCommandIndicator()
        {
            if (commandRing != null && commandRing.material != null) Destroy(commandRing.material);
            if (commandIndicator != null) Destroy(commandIndicator);
            commandRing = null;
            commandIndicator = null;
        }

        void UpdateAnimation()
        {
            if (guardAnimator == null || IsDead.Value) return;
            float speed;
            if (IsServer && navigation != null && navigation.enabled) speed = navigation.velocity.magnitude;
            else speed = Vector3.Distance(transform.position, lastVisualPosition) / Mathf.Max(.001f, Time.deltaTime);
            lastVisualPosition = transform.position;
            guardAnimator.SetBool(WalkingHash, speed > .12f);
            guardAnimator.SetFloat(SpeedHash, speed);
        }

        [Rpc(SendTo.Everyone)]
        void FirePresentationRpc(Vector3 start, Vector3 end)
        {
            if (guardAnimator != null) guardAnimator.SetTrigger(AttackHash);
            weaponPose?.PlayShot(end);
            if (shotAudio != null) shotAudio.Play();
            StartCoroutine(ShowTracer(start, end));
        }

        [Rpc(SendTo.Everyone)]
        void HitPresentationRpc()
        {
            if (guardAnimator != null) guardAnimator.SetTrigger(HitHash);
        }

        static IEnumerator ShowTracer(Vector3 start, Vector3 end)
        {
            GameObject tracer = new("Friendly Guard Tracer");
            LineRenderer line = tracer.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = .035f;
            line.endWidth = .012f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = new Color(1f, .8f, .2f, .9f);
            line.endColor = new Color(1f, .25f, .08f, .15f);
            yield return new WaitForSeconds(.055f);
            Destroy(tracer);
        }
    }
}
