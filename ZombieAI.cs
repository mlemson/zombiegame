using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using NavigationLink = Unity.AI.Navigation.NavMeshLink;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using ZombieTown.Multiplayer;
using ZombieTown.Progression;
using ZombieTown.LevelFour;

namespace Unity.FPS.AI
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Actor))]
    [RequireComponent(typeof(Animator))]
    public partial class ZombieAI : NetworkBehaviour
    {
        [Header("Zombie Settings")]
        public float MoveSpeed = 2.5f;
        public float AttackDistance = 1.8f;
        public float AttackDamage = 15f;
        public float AttackInterval = 1.1f;
        public float AttackStartupDelay = 0.15f;
        public float AttackHitDelay = 0.28f;
        public float AttackAnimationDuration = 0.8f;
        public float DeathAnimationDuration = 3.3f;
        [Range(0f, 0.5f)] public float DeathAnimationStartOffset = 0.47f;
        public string DeathStateName = "Base Layer.Die";
        public float GroundOffset = 0.18f;
        public static readonly System.Collections.Generic.HashSet<ZombieAI> ActiveZombies = new();
        void OnEnable()
        {
            ActiveZombies.Add(this);
            // Wheel suspension queries need a layer filter in addition to pair isolation.
            int bodyLayer = LayerMask.NameToLayer("ZombieBody");
            if (bodyLayer >= 0)
                foreach (var collider in GetComponentsInChildren<Collider>(true))
                    if (!collider.isTrigger) collider.gameObject.layer = bodyLayer;
        }
        void OnDisable() { ActiveZombies.Remove(this); ReleaseBusOpening(); }

        [Header("Distance Simulation")]
        [Tooltip("Zombies farther than this from every living player or hired guard stop pathfinding and animation until somebody returns.")]
        [Min(25f)] public float MaximumActiveDistance = 100f;
        [Tooltip("How often a distance-idle zombie searches for a nearby target.")]
        [Min(.25f)] public float DistanceIdleCheckInterval = .75f;

        [Header("Posture & Animation Options")]
        [Tooltip("Starts this zombie in the crouched locomotion branch. Can also be changed at runtime through SetCrouching.")]
        public bool StartsCrouched;
        [Range(0.2f, 1f)] public float CrouchSpeedMultiplier = 0.55f;
        public bool IsCrouching { get; private set; }
        public bool IsClimbing { get; private set; }

        [Header("Navigation Link Traversal")]
        [Tooltip("Manually traverses NavMesh links so coarse building colliders cannot block window entry.")]
        public bool ManuallyTraverseNavMeshLinks = true;
        public bool CanUseNarrowOpenings = true;
        [Min(.2f)] public float LinkTraversalDuration = .9f;
        [Min(0f)] public float LinkTraversalArcHeight = .45f;
        [Header("Traversal Posture")]
        [Min(0f)] public float TraversalCrouchHoldDuration = .55f;
        [Min(0.05f)] public float TraversalStandUpDuration = .75f;

        [Header("Headshots")]
        public GameObject HeadGibPrefab;
        public float HeadshotMultiplier = 2f;
        [Range(0.15f, 0.4f)] public float HeadHitboxRadius = 0.25f;
        public float HeadGibForce = 6f;
        [Tooltip("Optional Animator state used for lethal headshots. Falls back to Die.")]
        public string HeadshotDeathStateName = "Base Layer.HeadshotDie";
        [Tooltip("Normalized point where a custom headshot clip starts. Raise this when the source clip contains an idle lead-in.")]
        [Range(0f, .9f)] public float HeadshotDeathAnimationStartOffset = .1f;

        [Header("Knockback")]
        [Min(.75f)] public float KnockbackRecoveryDuration = 1.1f;

        [Header("Vehicle Collision")]
        [Tooltip("Small one-way geometry skin used when zombie physics is isolated from vehicles.")]
        [Range(0.005f, 0.15f)] public float VehicleGeometrySkin = 0.04f;
        [Tooltip("Small-radius probes used to treat vehicle bodywork like static building walls without making door openings artificially too narrow.")]
        [Range(0.05f, 0.30f)] public float VehicleWallProbeRadius = 0.14f;
        [Tooltip("How close a zombie may get to an authored Door/Window/Ladder link endpoint before vehicle wall blocking yields to that authored access route.")]
        [Range(0.35f, 5f)] public float VehicleAccessLinkApproachRadius = 2.75f;
        [Tooltip("Ignore nearly vertical depenetration from vehicle floors/steps; NavMesh owns vertical placement there.")]
        [Range(0.5f, 0.95f)] public float VehicleFloorNormalThreshold = 0.72f;
        [Tooltip("Extra margin around an explicit VehicleZombieAccessZone where the vehicle shell guard is allowed to yield.")]
        [Range(0.05f, 0.6f)] public float VehicleAccessPassMargin = 0.22f;
        [Tooltip("How close the agent may stop at a NavMesh edge before explicit portal traversal starts.")]
        [Range(.2f, 1.2f)] public float VehicleAccessTraversalStartMargin = .8f;
        [Tooltip("How long a chosen vehicle opening stays targeted before the AI is allowed to choose again.")]
        [Range(0.5f, 8f)] public float VehicleAccessTargetTimeout = 4f;
        [Tooltip("Distance from a Window access zone where the existing crouch locomotion is enabled.")]
        [Range(0.2f, 1.5f)] public float VehicleWindowCrouchDistance = 0.75f;
        [Tooltip("Allows this zombie to jump onto a moving vehicle through an authored Window zone.")]
        public bool CanBoardMovingVehicles = true;
        [Tooltip("Maximum distance from a moving window at which boarding can start.")]
        [Range(.35f, 2f)] public float MovingWindowBoardingRange = 1.15f;
        [Tooltip("Zombies will not attempt to board vehicles moving faster than this.")]
        [Range(1f, 12f)] public float MaximumMovingBoardingSpeed = 7f;
        [Min(.25f)] public float MovingWindowBoardingDuration = .85f;
        [Min(0f)] public float MovingWindowJumpArc = .6f;
        [Tooltip("Hoe lang de zombie kort stopt nadat een voertuig zijn pad blokkeert.")]
        [Min(0.05f)] public float VehicleYieldDuration = 0.45f;
        [Tooltip("Minimale terugduw-afstand wanneer een zombie tegen een stilstaand voertuig loopt.")]
        [Min(0.05f)] public float StationaryVehiclePushDistance = 1.05f;
        [Tooltip("Extra terugduw-afstand per m/s voertuigsnelheid.")]
        [Min(0f)] public float VehiclePushPerMetrePerSecond = 0.9f;
        [Tooltip("Maximale knockback-afstand bij een voertuigbotsing.")]
        [Min(0.1f)] public float MaximumVehiclePushDistance = 6.0f;
        [Tooltip("Voorkomt dat dezelfde zombie tientallen keren per seconde opnieuw wordt weggewarpt.")]
        [Min(0.02f)] public float VehicleReactionCooldown = 0.06f;
        [Tooltip("Vanaf deze voertuigsnelheid (m/s) gebruikt de zombie de bestaande Knockback/val-reactie. 3.5 m/s is ongeveer 12.6 km/u.")]
        [Min(0f)] public float VehicleFallSpeed = 2.5f;

        [Header("Loot Settings")]
        [Range(0, 1)]
        public float AmmoDropChance = 0.0625f;
        public GameObject AmmoLootPrefab;
        [Range(0, 1)]
        public float HealthDropChance = 0.08f;
        [Range(0, 1)]
        public float StrikerHealthDropChance = 0.30f;
        public GameObject HealthLootPrefab;

        [Header("Audio & FX")]
        public AudioClip AttackSfx;
        public AudioClip DeathSfx;

        private NavMeshAgent m_NavAgent;
        private Collider m_BodyCollider;
        private readonly RaycastHit[] m_VehicleCastHits = new RaycastHit[64];
        private readonly Collider[] m_VehicleOverlapHits = new Collider[64];
        private Health m_Health;
        private Animator m_Animator;
        private ZombieAudio m_ZombieAudio;
        private Transform m_PlayerTransform;
        private Health m_PlayerHealth;
        private float m_NextCombatTargetSearch;
        private float m_NextDistanceIdleCheck;
        private bool m_IsDistanceIdling;
        private float m_NextAttackTime = float.NegativeInfinity;
        private bool m_IsAttacking;
        private bool m_IsDead;
        public readonly NetworkVariable<float> SyncedHealth = new();
        public readonly NetworkVariable<bool> IsDeadNetworkState = new(false);
        private Coroutine m_AttackRoutine;
        private Transform m_HeadTransform;
        private bool m_HasPendingHeadshot;
        private bool m_WasLethalHeadshot;
        private Vector3 m_HeadshotDirection;
        private Vector3 m_HeadshotPoint;
        private GameObject m_LastDamageSource;
        private float m_KnockbackUntil = float.NegativeInfinity;
        private float m_VehicleYieldUntil = float.NegativeInfinity;
        private float m_NextVehicleReactionTime = float.NegativeInfinity;
        private Vector3 m_LastVehicleEscapeDirection;
        private Coroutine m_LinkTraversalRoutine;
        private Coroutine m_TraversalPostureRoutine;
        private VehicleZombieAccessZone m_VehicleAccessTarget;
        private float m_VehicleAccessTargetUntil = float.NegativeInfinity;
        private bool m_VehicleAccessCrossing;
        private bool m_VehicleAccessForcedCrouch;
        private bool m_CrouchBeforeVehicleAccess;
        private Transform m_BoardedVehicle;
        private Vector3 m_BoardedLocalPosition;
        private float m_NextStationaryVehicleAccessSearchTime =
            float.NegativeInfinity;
        private readonly System.Collections.Generic.List<Collider>
            m_VehicleBoundsColliders = new();

        public bool WasLethalHeadshot => m_WasLethalHeadshot;
        bool IsCombatAuthority => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;

        private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private static readonly int DieTriggerHash = Animator.StringToHash("Die");
        private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");
        private static readonly int CrouchSpeedHash = Animator.StringToHash("CrouchSpeed");
        private static readonly int IsClimbingHash = Animator.StringToHash("IsClimbing");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int KnockbackHash = Animator.StringToHash("Knockback");
        private static readonly int StaggerHash = Animator.StringToHash("Stagger");
        private static readonly int SpecialAttackHash = Animator.StringToHash("SpecialAttack");
        private static readonly int HeadshotDieHash = Animator.StringToHash("HeadshotDie");

        public static float CampaignSpeedMultiplier(string sceneName,float increasePerLevel=.06f)
        {
            int level=sceneName switch { "RadioOutpostScene"=>2, "HarborEvacuationScene"=>3, "RetreatDefenseScene"=>4, "HarborViewCityScene"=>5, "DeadOrbitScene"=>6, _=>1 };
            return 1+(level-1)*Mathf.Clamp(increasePerLevel,0,.15f);
        }

        public override void OnNetworkSpawn()
        {
            SyncedHealth.OnValueChanged += OnSyncedHealthChanged;
            IsDeadNetworkState.OnValueChanged += OnDeadNetworkStateChanged;

            if (IsServer && m_Health != null)
            {
                SyncedHealth.Value = m_Health.CurrentHealth;
                IsDeadNetworkState.Value = m_IsDead;
            }
            else if (m_Health != null)
            {
                m_Health.CurrentHealth = Mathf.Clamp(SyncedHealth.Value, 0f, m_Health.MaxHealth);
                if (IsDeadNetworkState.Value && !m_IsDead)
                    ApplyRemoteDeathVisuals();
            }
        }

        public override void OnNetworkDespawn()
        {
            SyncedHealth.OnValueChanged -= OnSyncedHealthChanged;
            IsDeadNetworkState.OnValueChanged -= OnDeadNetworkStateChanged;
            base.OnNetworkDespawn();
        }

        void OnSyncedHealthChanged(float previous, float current)
        {
            if (!IsServer && m_Health != null)
            {
                m_Health.CurrentHealth = Mathf.Clamp(current, 0f, m_Health.MaxHealth);
                if (current <= 0f && !m_IsDead)
                    ApplyRemoteDeathVisuals();
            }
        }

        void OnDeadNetworkStateChanged(bool previous, bool current)
        {
            if (!IsServer && current && !m_IsDead)
                ApplyRemoteDeathVisuals();
        }

        void Awake()
        {
            m_NavAgent = GetComponent<NavMeshAgent>();
            m_BodyCollider = GetComponent<Collider>();
            m_Health = GetComponent<Health>();
            m_Animator = GetComponent<Animator>();
            m_ZombieAudio = GetComponent<ZombieAudio>();

            if (m_Animator != null)
                m_Animator.applyRootMotion = false;
        }

        void Start()
        {
            // Start runs once, so re-enabling a zombie cannot compound this multiplier.
            var balance=ZombieTown.Foundation.GameplaySceneContext.Active?.balance;
            MoveSpeed *= CampaignSpeedMultiplier(gameObject.scene.name,balance!=null?balance.zombieSpeedIncreasePerLevel:.06f);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
            {
                m_NavAgent.enabled = false;
                enabled = false;
                return;
            }

            EnemyPointReward.Ensure(gameObject, GetComponent<Damageable>()?.DamageMultiplier < 1f ? 10 : 5, 5);

            SetupHeadHitbox();

            AlignToGround();
            m_NavAgent.updatePosition = false;
            m_NavAgent.autoTraverseOffMeshLink = !ManuallyTraverseNavMeshLinks;

            m_NavAgent.speed = MoveSpeed;
            m_NavAgent.acceleration = Mathf.Max(m_NavAgent.acceleration * 0.7f, MoveSpeed * 2f);
            m_NavAgent.angularSpeed = Mathf.Min(m_NavAgent.angularSpeed, 360f);
            m_NavAgent.stoppingDistance = AttackDistance * 0.8f;
            SetCrouching(StartsCrouched);

            m_Health.OnDie += OnDeath;
            m_Health.OnDamaged += OnDamaged;

            // Find player
            Transform initialTarget = FindCombatTarget();
            if (initialTarget != null)
            {
                m_PlayerTransform = initialTarget;
                m_PlayerHealth = initialTarget.GetComponent<Health>();
            }
        }

        ZombieTown.Foundation.ZombieBreachTraversal foundationTraversal;
        void Update()
        {
            if (m_IsDead)
                return;

            if (foundationTraversal == null) TryGetComponent(out foundationTraversal);
            if (foundationTraversal != null && foundationTraversal.Tick()) return;

            if (ShouldIdleForDistance())
                return;

            // A downed player remains spawned, but is no longer a valid prey target.
            // Revalidate retained targets too, otherwise zombies keep surrounding and
            // even finish an attack that was started before the player went down.
            if (m_PlayerTransform != null && !IsValidCombatTarget(m_PlayerTransform))
            {
                ClearPlayerTarget();
            }

            // Reconsider nearby targets periodically. This lets zombies peel away from a
            // player to attack a hired guard that is actively holding the front line.
            if (!m_IsAttacking && Time.time >= m_NextCombatTargetSearch)
            {
                m_NextCombatTargetSearch = Time.time + .65f;
                FriendlyGuardAI nearestGuard = FindNearerFriendlyGuard(m_PlayerTransform);
                if (nearestGuard != null && nearestGuard.transform != m_PlayerTransform)
                {
                    m_PlayerTransform = nearestGuard.transform;
                    m_PlayerHealth = nearestGuard.GetComponent<Health>();
                    m_NextAttackTime = float.NegativeInfinity;
                }
            }

            if (m_PlayerTransform == null)
            {
                Transform target = FindCombatTarget();
                if (target != null)
                {
                    m_PlayerTransform = target;
                    m_PlayerHealth = target.GetComponent<Health>();
                }
                else
                {
                    return;
                }
            }
            if (!NetworkRoundGate.IsOpen)
            {
                if (m_NavAgent != null && m_NavAgent.enabled) m_NavAgent.isStopped = true;
                if (m_Animator != null)
                {
                    m_Animator.SetBool(IsWalkingHash, false);
                    m_Animator.SetFloat(SpeedHash, 0f);
                }
                return;
            }

            if (m_BoardedVehicle != null)
            {
                // A portal traversal coroutine owns the transform while entering or
                // leaving. Running boarded movement at the same time would reapply the
                // vehicle-floor height every frame.
                if (m_LinkTraversalRoutine == null)
                    UpdateBoardedVehicle();
                return;
            }

            if (clingingOpening != null) return;
            if (TryStartMovingVehicleBoarding(null))
                return;

            if (Time.time < m_KnockbackUntil)
            {
                if (m_NavAgent != null && m_NavAgent.enabled) m_NavAgent.isStopped = true;
                if (m_Animator != null)
                {
                    m_Animator.SetBool(IsWalkingHash, false);
                    m_Animator.SetFloat(SpeedHash, 0f);
                }
                return;
            }

            // A NavMeshAgent is not mass-based like a Rigidbody. Without this yield window
            // a zombie can keep correcting its transform into a vehicle and effectively
            // "push" a multi-ton bus/forklift sideways. While a vehicle blocks the path,
            // hold the zombie in place instead. The vehicle can still drive into the zombie.
            if (Time.time < m_VehicleYieldUntil)
            {
                if (m_NavAgent != null && m_NavAgent.enabled)
                {
                    m_NavAgent.isStopped = true;
                    m_NavAgent.velocity = Vector3.zero;
                    if (!m_NavAgent.updatePosition)
                        m_NavAgent.nextPosition = transform.position - Vector3.up * GroundOffset;
                }

                if (m_Animator != null)
                {
                    m_Animator.SetBool(IsWalkingHash, false);
                    m_Animator.SetFloat(SpeedHash, 0f);
                }
                return;
            }

            UpdateVehicleAccessState();

            if (m_LinkTraversalRoutine != null)
                return;
            if (!m_IsAttacking && ZombieTown.Foundation.CarryableDefenseItem.TryHandleZombie(this, m_PlayerTransform, m_NavAgent))
            { UpdateAnimator(); return; }
            if (!m_IsAttacking && ZombieTown.Foundation.BarricadeWindow.TryHandleZombie(this, m_PlayerTransform))
                return;
            if (!m_IsAttacking &&
                ZombieTown.Foundation.RepairableGate.TryHandleZombie(this, m_PlayerTransform, m_NavAgent))
            {
                UpdateAnimator();
                return;
            }
            if (ManuallyTraverseNavMeshLinks && m_NavAgent != null && m_NavAgent.enabled &&
                m_NavAgent.isOnOffMeshLink)
            {
                m_LinkTraversalRoutine = StartCoroutine(TraverseNavMeshLink());
                return;
            }

            if (m_NavAgent != null && m_NavAgent.enabled && !m_NavAgent.updatePosition)
            {
                Vector3 desired = m_NavAgent.nextPosition + Vector3.up * GroundOffset;
                Vector3 resolved = ClampAgainstSolidGeometry(
                    transform.position,
                    desired,
                    out bool approachingAuthoredVehicleAccess);
                transform.position = resolved;

                // Normally keep the NavMeshAgent synchronized with the position we actually
                // allowed. The exception is the short approach to an authored Door/Window/
                // Ladder link: the agent must be allowed to advance onto that link, otherwise
                // resetting nextPosition just before its endpoint makes isOnOffMeshLink impossible.
                if (!approachingAuthoredVehicleAccess &&
                    (resolved - desired).sqrMagnitude > 0.000001f)
                {
                    m_NavAgent.nextPosition = resolved - Vector3.up * GroundOffset;
                }
            }

            if (m_PlayerTransform == null)
            {
                Transform target = FindCombatTarget();
                if (target != null)
                {
                    m_PlayerTransform = target;
                    m_PlayerHealth = target.GetComponent<Health>();
                }
                else
                {
                    return;
                }
            }

            if (m_IsAttacking)
            {
                if (m_NavAgent != null && m_NavAgent.enabled)
                {
                    m_NavAgent.isStopped = true;
                }

                return;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, m_PlayerTransform.position);

            if (distanceToPlayer > AttackDistance)
            {
                m_NavAgent.isStopped = false;

                Vector3 navigationTarget = m_PlayerTransform.position;

                if (m_VehicleAccessTarget == null)
                    TryAcquireStationaryVehicleAccess(transform.position);

                if (m_VehicleAccessTarget != null)
                {
                    bool nearOrInside =
                        m_VehicleAccessTarget.ContainsOrNear(
                            transform.position,
                            Mathf.Max(
                                VehicleAccessPassMargin,
                                VehicleAccessTraversalStartMargin));

                    if (nearOrInside)
                    {
                        // Access zones are real portals, not only holes in the vehicle collision
                        // guard. A NavMeshAgent cannot cross the disconnected street/bus-floor
                        // meshes by itself, so move it through the authored opening explicitly.
                        if (TryStartVehicleAccessTraversal())
                            return;

                        m_VehicleAccessCrossing = true;
                    }

                    if (!m_VehicleAccessCrossing)
                    {
                        navigationTarget =
                            m_VehicleAccessTarget.GetApproachPoint(transform.position);
                    }
                }

                m_NavAgent.stoppingDistance =
                    m_VehicleAccessTarget != null && !m_VehicleAccessCrossing
                        ? .05f
                        : AttackDistance * .8f;
                m_NavAgent.SetDestination(navigationTarget);
                m_NextAttackTime = float.NegativeInfinity;
            }
            else
            {
                if (m_NextAttackTime == float.NegativeInfinity)
                {
                    m_NextAttackTime = Time.time + AttackStartupDelay;
                }

                m_NavAgent.isStopped = true;
                // Face player
                Vector3 lookDir = (m_PlayerTransform.position - transform.position).normalized;
                lookDir.y = 0;
                if (lookDir != Vector3.zero)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 10f);
                }

                // Attack
                if (Time.time >= m_NextAttackTime)
                {
                    PerformAttack();
                }
            }

            UpdateAnimator();
        }

        bool ShouldIdleForDistance()
        {
            float maximumDistanceSqr = MaximumActiveDistance * MaximumActiveDistance;
            if (!m_IsDistanceIdling && m_PlayerTransform != null &&
                (m_PlayerTransform.position - transform.position).sqrMagnitude <= maximumDistanceSqr)
                return false;

            if (m_IsDistanceIdling && Time.time < m_NextDistanceIdleCheck)
            {
                HoldDistanceIdle();
                return true;
            }

            m_NextDistanceIdleCheck = Time.time + DistanceIdleCheckInterval;
            Transform nearest = FindCombatTarget();
            if (nearest != null && (nearest.position - transform.position).sqrMagnitude <= maximumDistanceSqr)
            {
                m_IsDistanceIdling = false;
                m_PlayerTransform = nearest;
                m_PlayerHealth = nearest.GetComponent<Health>();
                return false;
            }

            m_IsDistanceIdling = true;
            HoldDistanceIdle();
            return true;
        }

        void HoldDistanceIdle()
        {
            m_NextAttackTime = float.NegativeInfinity;
            if (m_IsAttacking)
            {
                m_IsAttacking = false;
                if (m_AttackRoutine != null)
                {
                    StopCoroutine(m_AttackRoutine);
                    m_AttackRoutine = null;
                }
            }
            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.isStopped = true;
                m_NavAgent.velocity = Vector3.zero;
                if (m_NavAgent.hasPath) m_NavAgent.ResetPath();
            }
            if (m_Animator != null)
            {
                m_Animator.SetBool(IsWalkingHash, false);
                m_Animator.SetFloat(SpeedHash, 0f);
            }
        }

        PlayerCharacterController FindPlayerController()
        {
            PlayerCharacterController[] players =
                FindObjectsByType<PlayerCharacterController>();
            if (players == null || players.Length == 0)
                return null;

            PlayerCharacterController best = null;
            float bestDistance = float.PositiveInfinity;
            NetworkManager manager = NetworkManager.Singleton;
            bool networkActive = manager != null && manager.IsListening;

            foreach (PlayerCharacterController player in players)
            {
                if (player == null) continue;
                ZombieTown.Multiplayer.PlayerClassController playerClass = player.GetComponent<ZombieTown.Multiplayer.PlayerClassController>();
                if (networkActive && (playerClass == null || !playerClass.IsSpawned ||
                                      !playerClass.IsReady.Value || playerClass.IsDowned.Value))
                    continue;

                Health playerHealth = player.GetComponent<Health>();
                if (playerHealth == null || playerHealth.CurrentHealth <= 0f)
                    continue;

                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = player;
                }
            }

            return best;
        }

        Transform FindCombatTarget()
        {
            PlayerCharacterController player = FindPlayerController();
            Transform best = player != null ? player.transform : null;
            float bestDistance = best != null
                ? (best.position - transform.position).sqrMagnitude
                : float.PositiveInfinity;

            foreach (FriendlyGuardAI guard in FriendlyGuardAI.ActiveGuards)
            {
                if (guard == null || !guard.IsHired || !guard.IsAlive) continue;
                float sqr = (guard.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestDistance) continue;
                bestDistance = sqr;
                best = guard.transform;
            }
            return best;
        }

        FriendlyGuardAI FindNearerFriendlyGuard(Transform current)
        {
            FriendlyGuardAI best = null;
            float bestDistance = current != null
                ? (current.position - transform.position).sqrMagnitude
                : float.PositiveInfinity;
            foreach (FriendlyGuardAI guard in FriendlyGuardAI.ActiveGuards)
            {
                if (guard == null || !guard.IsHired || !guard.IsAlive) continue;
                float sqr = (guard.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestDistance) continue;
                bestDistance = sqr;
                best = guard;
            }
            return best;
        }

        bool IsValidCombatTarget(Transform target)
        {
            if (target == null) return false;
            FriendlyGuardAI guard = target.GetComponent<FriendlyGuardAI>();
            if (guard != null) return guard.IsHired && guard.IsAlive;
            return IsValidPlayerTarget(target.GetComponent<PlayerCharacterController>());
        }

        bool IsValidPlayerTarget(PlayerCharacterController player)
        {
            if (player == null) return false;
            Health playerHealth = player.GetComponent<Health>();
            if (playerHealth == null || playerHealth.CurrentHealth <= 0f) return false;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return true;
            PlayerClassController playerClass = player.GetComponent<PlayerClassController>();
            return playerClass != null && playerClass.IsSpawned && playerClass.IsReady.Value &&
                   !playerClass.IsDowned.Value;
        }

        void ClearPlayerTarget()
        {
            m_PlayerTransform = null;
            m_PlayerHealth = null;
            m_NextAttackTime = float.NegativeInfinity;
            m_IsAttacking = false;
            if (m_AttackRoutine != null)
            {
                StopCoroutine(m_AttackRoutine);
                m_AttackRoutine = null;
            }
            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.isStopped = true;
                m_NavAgent.ResetPath();
            }
            if (m_Animator != null)
            {
                m_Animator.ResetTrigger(AttackHash);
                m_Animator.SetBool(IsWalkingHash, false);
                m_Animator.SetFloat(SpeedHash, 0f);
            }
        }

        void UpdateAnimator()
        {
            if (m_Animator == null) return;

            bool isWalking = m_NavAgent.velocity.magnitude > 0.1f && !m_NavAgent.isStopped;
            m_Animator.SetBool(IsWalkingHash, isWalking);
            m_Animator.SetFloat(SpeedHash, m_NavAgent.velocity.magnitude);
            m_Animator.SetBool(IsCrouchingHash, IsCrouching);
            m_Animator.SetFloat(CrouchSpeedHash, IsCrouching ? m_NavAgent.velocity.magnitude : 0f);
            m_Animator.SetBool(IsClimbingHash, IsClimbing);
        }

        public void SetCrouching(bool crouching)
        {
            IsCrouching = crouching;
            if (m_NavAgent != null)
                m_NavAgent.speed = MoveSpeed * (crouching ? CrouchSpeedMultiplier : 1f);
            if (m_Animator != null)
            {
                m_Animator.SetBool(IsCrouchingHash, crouching);
                if (!crouching) m_Animator.SetFloat(CrouchSpeedHash, 0f);
            }
        }

        public void RestorePostureAfterTraversal(bool wasCrouching)
        {
            if (m_TraversalPostureRoutine != null)
            {
                StopCoroutine(m_TraversalPostureRoutine);
                m_TraversalPostureRoutine = null;
            }

            if (wasCrouching)
            {
                SetCrouching(true);
                return;
            }

            m_TraversalPostureRoutine = StartCoroutine(RestorePostureAfterTraversalRoutine());
        }

        IEnumerator RestorePostureAfterTraversalRoutine()
        {
            SetCrouching(true);
            float holdElapsed = 0f;
            while (holdElapsed < TraversalCrouchHoldDuration && !m_IsDead)
            {
                holdElapsed += Time.deltaTime;
                yield return null;
            }

            if (!m_IsDead)
            {
                SetCrouching(false);
                GetComponent<ZombieTown.Multiplayer.NetworkZombieAnimator>()?.SetWindowPose(6);
                if (m_Animator != null && m_Animator.HasState(0, Animator.StringToHash("Base Layer.Idle")))
                    m_Animator.CrossFadeInFixedTime("Base Layer.Idle", TraversalStandUpDuration);
                yield return new WaitForSeconds(TraversalStandUpDuration);
                GetComponent<ZombieTown.Multiplayer.NetworkZombieAnimator>()?.SetWindowPose(0);
            }

            m_TraversalPostureRoutine = null;
        }

#if UNITY_EDITOR
        public void PreviewNavigationTarget(Transform target)
        {
            m_PlayerTransform = target;
            m_PlayerHealth = target != null ? target.GetComponent<Health>() : null;
        }

        public string GetNavigationDebugState()
        {
            return "target=" + (m_PlayerTransform != null ? m_PlayerTransform.name : "NULL") +
                   " dead=" + m_IsDead +
                   " attacking=" + m_IsAttacking +
                   " knockback=" + (Time.time < m_KnockbackUntil) +
                   " yielding=" + (Time.time < m_VehicleYieldUntil) +
                   " traversing=" + (m_LinkTraversalRoutine != null) +
                   " access=" + (m_VehicleAccessTarget != null
                       ? m_VehicleAccessTarget.name
                       : "NULL") +
                   " boarded=" + (m_BoardedVehicle != null
                       ? m_BoardedVehicle.name
                       : "NULL");
        }
#endif

        IEnumerator TraverseNavMeshLink()
        {
            OffMeshLinkData link = m_NavAgent.currentOffMeshLinkData;
            if (!link.valid)
            {
                m_LinkTraversalRoutine = null;
                yield break;
            }

            bool wasCrouching = IsCrouching;
            Vector3 start = transform.position;
            Vector3 end = link.endPos + Vector3.up * GroundOffset;
            bool isDoor = IsNamedLink(start, end, "Door");
            bool isWindow = IsNamedLink(start, end, "Window");
            bool isLadder = IsNamedLink(start, end, "Ladder");
            bool isStairs = IsNamedLink(start, end, "Stair");
            // Zombies may only cross authored doors, windows, ladders and stairs; any other
            // off-mesh link (auto-generated jump/drop links, stray connections, etc.)
            // must be blocked or they can appear to walk straight through walls.
            bool blockedNarrowOpening = !CanUseNarrowOpenings && (isDoor || isWindow);
            if (blockedNarrowOpening || (!isDoor && !isWindow && !isLadder && !isStairs))
            {
                m_NavAgent.ResetPath();
                if (NavMesh.SamplePosition(start, out NavMeshHit safeHit, .8f, NavMesh.AllAreas))
                {
                    m_NavAgent.Warp(safeHit.position);
                    transform.position = safeHit.position + Vector3.up * GroundOffset;
                }
                m_NavAgent.isStopped = true;
                m_LinkTraversalRoutine = null;
                yield break;
            }
            bool climbing = isLadder;
            bool crouchingThroughOpening = !climbing && isWindow;
            IsClimbing = climbing;
            float distanceScale = Mathf.Max(1f, Vector3.Distance(start, end) / 1.6f);
            float duration = isStairs ? Mathf.Max(.4f, Vector3.Distance(start, end) / Mathf.Max(1.5f, MoveSpeed)) : Mathf.Max(.2f, LinkTraversalDuration * distanceScale);
            m_NavAgent.isStopped = true;
            SetCrouching(crouchingThroughOpening);
            if (m_Animator != null)
            {
                m_Animator.SetBool(IsClimbingHash, climbing);
                m_Animator.SetBool(IsWalkingHash, !climbing);
                float traversalSpeed = MoveSpeed * (crouchingThroughOpening ? CrouchSpeedMultiplier : 1f);
                m_Animator.SetFloat(SpeedHash, climbing ? 0f : traversalSpeed);
                m_Animator.SetFloat(CrouchSpeedHash, crouchingThroughOpening ? traversalSpeed : 0f);
            }

            float windowLift=crouchingThroughOpening?ZombieTown.Foundation.WindowClimbMotion.FindSillLift(start,end):0;
            if(crouchingThroughOpening)duration=Mathf.Max(1.8f,duration);
            var windowAnimator=GetComponent<ZombieTown.Multiplayer.NetworkZombieAnimator>();byte windowPose=0;
            float elapsed = 0f;
            while (elapsed < duration && !m_IsDead && m_NavAgent != null && m_NavAgent.enabled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = t * t * (3f - 2f * t);
                Vector3 position = Vector3.Lerp(start, end, easedT);
                // Raise the feet to the sill before crossing a window.
                float arcHeight = climbing ? LinkTraversalArcHeight :
                    crouchingThroughOpening ? Mathf.Min(LinkTraversalArcHeight, .08f) : 0f;
                position.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
                if(crouchingThroughOpening){
                    position=ZombieTown.Foundation.WindowClimbMotion.Position(start,end,windowLift,t);
                    byte pose=t<.28f && windowLift>.1f?(byte)1:(byte)2;
                    if(pose!=windowPose){windowPose=pose;windowAnimator?.SetWindowPose(pose);SetCrouching(pose==2);if(m_Animator!=null)m_Animator.SetBool(IsClimbingHash,pose==1);}
                }
                transform.position = position;
                m_NavAgent.nextPosition = position - Vector3.up * GroundOffset;

                Vector3 direction = end - start;
                direction.y = 0f;
                if (direction.sqrMagnitude > .001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), easedT);
                yield return null;
            }

            if (!m_IsDead && m_NavAgent != null && m_NavAgent.enabled)
            {
                transform.position = end;
                m_NavAgent.nextPosition = end - Vector3.up * GroundOffset;
                if (m_NavAgent.isOnOffMeshLink) m_NavAgent.CompleteOffMeshLink();
                m_NavAgent.isStopped = false;
                IsClimbing = false;
                if (m_Animator != null) m_Animator.SetBool(IsClimbingHash, false);
                RestorePostureAfterTraversal(wasCrouching);
            }
            m_LinkTraversalRoutine = null;
        }

        bool TryStartVehicleAccessTraversal()
        {
            VehicleZombieAccessZone zone = m_VehicleAccessTarget;
            if (zone == null || m_PlayerTransform == null || m_NavAgent == null ||
                !m_NavAgent.enabled || !zone.IsVehicleStationary())
                return false;

            if (zone.UseClingingEntry && zone.SignedSide(transform.position) > .02f &&
                IsPointWithinVehicleBounds(zone.VehicleRoot, m_PlayerTransform.position, .15f))
            {
                if (!zone.Claim(this)) return false;
                m_LinkTraversalRoutine = StartCoroutine(TraverseClingingBusOpening(zone));
                return true;
            }

            float zombieSide = zone.SignedSide(transform.position);
            float playerSide = zone.SignedSide(m_PlayerTransform.position);
            // Both are already on the same side, so no portal crossing is required.
            if (Mathf.Abs(zombieSide) > .05f && Mathf.Abs(playerSide) > .05f &&
                Mathf.Sign(zombieSide) == Mathf.Sign(playerSide))
                return false;

            zone.GetTraversalCandidates(out Vector3 outside, out Vector3 inside);
            Vector3 exitCandidate = zombieSide >= 0f ? inside : outside;
            float sampleRadius = zone.IsWindow ? 1.1f : .85f;
            if (!NavMesh.SamplePosition(exitCandidate, out NavMeshHit exitHit,
                    sampleRadius, m_NavAgent.areaMask))
                return false;

            // Reject a sample that snapped back to the starting side of the shell.
            if (Mathf.Abs(zone.SignedSide(exitHit.position)) > .04f &&
                Mathf.Sign(zone.SignedSide(exitHit.position)) == Mathf.Sign(zombieSide))
                return false;

            m_LinkTraversalRoutine = StartCoroutine(
                TraverseVehicleAccessZone(
                    zone,
                    exitHit.position + Vector3.up * GroundOffset,
                    zombieSide >= 0f));
            return true;
        }

        IEnumerator TraverseVehicleAccessZone(
            VehicleZombieAccessZone zone,
            Vector3 end,
            bool enteringVehicle)
        {
            bool wasCrouching = IsCrouching;
            bool crouch = zone != null && zone.IsWindow;
            Vector3 start = transform.position;
            float duration = crouch ? .72f : .48f;

            m_VehicleAccessCrossing = true;
            m_NavAgent.ResetPath();
            m_NavAgent.isStopped = true;
            SetCrouching(crouch);
            if (crouch)
                GetComponent<ZombieTown.Multiplayer.NetworkZombieAnimator>()?.SetWindowPose(2);
            if (m_Animator != null)
            {
                m_Animator.SetBool(IsWalkingHash, true);
                m_Animator.SetFloat(SpeedHash, MoveSpeed * (crouch ? CrouchSpeedMultiplier : 1f));
            }

            float elapsed = 0f;
            while (elapsed < duration && !m_IsDead && zone != null &&
                   zone.isActiveAndEnabled && zone.IsVehicleStationary() &&
                   m_NavAgent != null && m_NavAgent.enabled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                Vector3 position = Vector3.Lerp(start, end, eased);
                transform.position = position;
                m_NavAgent.nextPosition = position - Vector3.up * GroundOffset;

                Vector3 direction = end - start;
                direction.y = 0f;
                if (direction.sqrMagnitude > .001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(direction), eased);
                yield return null;
            }

            if (!m_IsDead && m_NavAgent != null && m_NavAgent.enabled &&
                zone != null && zone.IsVehicleStationary() &&
                NavMesh.SamplePosition(end - Vector3.up * GroundOffset,
                    out NavMeshHit endHit, .45f, m_NavAgent.areaMask))
            {
                Vector3 finalPosition =
                    endHit.position + Vector3.up * GroundOffset;

                if (enteringVehicle && zone.VehicleRoot != null)
                {
                    m_NavAgent.ResetPath();
                    m_NavAgent.isStopped = true;
                    m_NavAgent.enabled = false;
                    m_BoardedVehicle = zone.VehicleRoot;
                    m_BoardedLocalPosition =
                        m_BoardedVehicle.InverseTransformPoint(finalPosition);
                    transform.position = finalPosition;
                }
                else
                {
                    m_NavAgent.Warp(endHit.position);
                    transform.position = finalPosition;
                    m_NavAgent.nextPosition = endHit.position;
                    m_NavAgent.isStopped = false;
                }
            }

            RestorePostureAfterTraversal(wasCrouching);
            ClearVehicleAccessTarget();
            m_LinkTraversalRoutine = null;
        }

        bool TryStartMovingVehicleBoarding(Transform requiredVehicleRoot)
        {
            if (!CanBoardMovingVehicles || !CanUseNarrowOpenings ||
                m_PlayerTransform == null || m_LinkTraversalRoutine != null)
            {
                return false;
            }

            VehicleZombieAccessZone best = null;
            float bestDistanceSqr = float.PositiveInfinity;
            float rangeSqr = MovingWindowBoardingRange * MovingWindowBoardingRange;

            foreach (VehicleZombieAccessZone zone in VehicleZombieAccessZone.Active)
            {
                if (zone == null || !zone.isActiveAndEnabled || (!zone.IsWindow && !zone.UseClingingEntry) ||
                    zone.VehicleRoot == null || (zone.IsVehicleStationary() && !zone.UseClingingEntry) ||
                    (zone.UseClingingEntry && (!zone.CanClaim(this) || (zone.IsWindow && zone.Barricade == null))))
                {
                    continue;
                }

                if (requiredVehicleRoot != null &&
                    zone.VehicleRoot != requiredVehicleRoot)
                {
                    continue;
                }

                float distanceSqr = zone.SqrDistance(transform.position);
                // On the scaled bus, the street NavMesh can end 1.5 m from the door
                // trigger. Start the authored crossing before that navigation boundary.
                float reach = zone.UseClingingEntry ? (zone.IsWindow ? Mathf.Max(rangeSqr, 3f * 3f) : 2.1f * 2.1f) : rangeSqr;
                if (distanceSqr > reach || distanceSqr >= bestDistanceSqr) continue;

                Rigidbody vehicleBody = zone.VehicleRoot.GetComponent<Rigidbody>();
                if (vehicleBody == null)
                    vehicleBody = zone.VehicleRoot.GetComponentInParent<Rigidbody>();

                float speed = vehicleBody != null
                    ? Vector3.ProjectOnPlane(vehicleBody.linearVelocity, Vector3.up).magnitude
                    : 0f;
                if (speed > MaximumMovingBoardingSpeed)
                    continue;
                if (zone.UseClingingEntry && !IsPointWithinVehicleBounds(zone.VehicleRoot, m_PlayerTransform.position, .15f))
                    continue;

                // Only board from outside while the current target is inside this shell.
                if (zone.SignedSide(transform.position) <= .02f ||
                    zone.SignedSide(m_PlayerTransform.position) >= -.02f)
                {
                    continue;
                }

                if (distanceSqr <= reach && distanceSqr < bestDistanceSqr)
                {
                    best = zone;
                    bestDistanceSqr = distanceSqr;
                }
            }

            if (best == null)
                return false;

            if (best.UseClingingEntry && !best.Claim(this)) return false;
            m_VehicleAccessTarget = best;
            m_VehicleAccessTargetUntil =
                Time.time + Mathf.Max(1f, MovingWindowBoardingDuration + .5f);
            m_LinkTraversalRoutine = StartCoroutine(best.UseClingingEntry ? TraverseClingingBusOpening(best) : TraverseMovingVehicleWindow(best));
            return true;
        }

        public bool TryBeginMovingVehicleBoarding(Transform vehicleRoot)
        {
            if (m_IsDead || vehicleRoot == null)
                return false;

            if (m_PlayerTransform == null)
            {
                PlayerCharacterController player = FindPlayerController();
                if (player != null)
                {
                    m_PlayerTransform = player.transform;
                    m_PlayerHealth = player.GetComponent<Health>();
                }
            }

            return TryStartMovingVehicleBoarding(vehicleRoot);
        }

        IEnumerator TraverseMovingVehicleWindow(VehicleZombieAccessZone zone)
        {
            bool wasCrouching = IsCrouching;
            Vector3 start = transform.position;
            float duration = Mathf.Max(.25f, MovingWindowBoardingDuration);

            m_VehicleAccessCrossing = true;
            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.ResetPath();
                m_NavAgent.isStopped = true;
                m_NavAgent.enabled = false;
            }

            IsClimbing = true;
            SetCrouching(true);
            GetComponent<ZombieTown.Multiplayer.NetworkZombieAnimator>()?.SetWindowPose(2);
            if (m_Animator != null)
            {
                m_Animator.SetBool(IsClimbingHash, true);
                m_Animator.SetBool(IsWalkingHash, true);
                m_Animator.SetFloat(SpeedHash, MoveSpeed * CrouchSpeedMultiplier);
            }

            float elapsed = 0f;
            while (elapsed < duration && !m_IsDead && zone != null &&
                   zone.isActiveAndEnabled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                zone.GetTraversalCandidates(out _, out Vector3 movingInside);

                Vector3 position = Vector3.Lerp(start, movingInside, eased);
                position.y += Mathf.Sin(t * Mathf.PI) * MovingWindowJumpArc;
                transform.position = position;

                Vector3 direction = movingInside - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > .001f)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(direction),
                        eased);
                }

                yield return null;
            }

            if (!m_IsDead && zone != null && zone.isActiveAndEnabled &&
                zone.VehicleRoot != null)
            {
                zone.GetTraversalCandidates(out _, out Vector3 inside);
                m_BoardedVehicle = zone.VehicleRoot;
                m_BoardedLocalPosition = m_BoardedVehicle.InverseTransformPoint(inside);
                transform.position = inside;
            }
            else
            {
                RestoreNavigationAfterVehicleBoarding();
            }

            IsClimbing = false;
            if (m_Animator != null)
                m_Animator.SetBool(IsClimbingHash, false);
            RestorePostureAfterTraversal(wasCrouching);
            ClearVehicleAccessTarget();
            m_LinkTraversalRoutine = null;
        }

        void UpdateBoardedVehicle()
        {
            if(m_BoardedOnRoof && m_BoardedVehicle!=null){UpdateBusRoof();return;}
            if (m_BoardedVehicle == null)
            {
                RestoreNavigationAfterVehicleBoarding();
                return;
            }

            // Once the target leaves the vehicle, do not chase it in vehicle-local
            // space. That kept the zombie at bus-floor height while it walked through
            // the shell and out over the lower street. Exit through an authored portal
            // and restore the NavMeshAgent only on the outside NavMesh layer.
            if (!IsPointWithinVehicleBounds(
                    m_BoardedVehicle,
                    m_PlayerTransform.position,
                    .15f))
            {
                if (TryStartBoardedVehicleExit())
                    return;

                transform.position =
                    m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition);
                if (m_Animator != null)
                {
                    m_Animator.SetBool(IsWalkingHash, false);
                    m_Animator.SetFloat(SpeedHash, 0f);
                }
                return;
            }

            if (m_PlayerTransform == null)
            {
                transform.position =
                    m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition);
                return;
            }

            Vector3 playerLocal =
                m_BoardedVehicle.InverseTransformPoint(m_PlayerTransform.position);
            playerLocal.y = m_BoardedLocalPosition.y;

            float distance = Vector3.Distance(
                m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition),
                m_PlayerTransform.position);

            if (distance > AttackDistance)
            {
                m_BoardedLocalPosition = Vector3.MoveTowards(
                    m_BoardedLocalPosition,
                    playerLocal,
                    MoveSpeed * CrouchSpeedMultiplier * Time.deltaTime);
                m_NextAttackTime = float.NegativeInfinity;

                if (m_Animator != null)
                {
                    m_Animator.SetBool(IsWalkingHash, true);
                    m_Animator.SetFloat(SpeedHash, MoveSpeed * CrouchSpeedMultiplier);
                }
            }
            else
            {
                if (m_NextAttackTime == float.NegativeInfinity)
                    m_NextAttackTime = Time.time + AttackStartupDelay;

                if (m_Animator != null)
                {
                    m_Animator.SetBool(IsWalkingHash, false);
                    m_Animator.SetFloat(SpeedHash, 0f);
                }

                if (!m_IsAttacking && Time.time >= m_NextAttackTime)
                    PerformAttack();
            }

            transform.position =
                m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition);

            Vector3 lookDirection = m_PlayerTransform.position - transform.position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > .001f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(lookDirection),
                    Time.deltaTime * 10f);
            }
        }

        bool TryStartBoardedVehicleExit()
        {
            if (m_BoardedVehicle == null || m_LinkTraversalRoutine != null)
                return false;

            VehicleZombieAccessZone bestZone = null;
            NavMeshHit bestOutsideHit = default;
            float bestScore = float.PositiveInfinity;

            foreach (VehicleZombieAccessZone zone in VehicleZombieAccessZone.Active)
            {
                if (zone == null || !zone.isActiveAndEnabled ||
                    zone.VehicleRoot != m_BoardedVehicle || zone.IsBlocked ||
                    !zone.IsVehicleStationary() ||
                    !TrySampleOutsideVehicleNavMesh(zone, out NavMeshHit outsideHit))
                {
                    continue;
                }

                zone.GetTraversalCandidates(out _, out Vector3 inside);
                float score =
                    (transform.position - inside).sqrMagnitude +
                    (m_PlayerTransform.position - outsideHit.position).sqrMagnitude * .15f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestZone = zone;
                bestOutsideHit = outsideHit;
            }

            if (bestZone == null)
                return false;

            m_VehicleAccessTarget = bestZone;
            m_VehicleAccessTargetUntil =
                Time.time + Mathf.Max(2f, VehicleAccessTargetTimeout);
            m_VehicleAccessCrossing = true;
            m_LinkTraversalRoutine = StartCoroutine(
                TraverseBoardedVehicleExit(bestZone, bestOutsideHit.position));
            return true;
        }

        bool TrySampleOutsideVehicleNavMesh(
            VehicleZombieAccessZone zone,
            out NavMeshHit bestHit)
        {
            bestHit = default;
            if (zone == null || m_NavAgent == null)
                return false;

            zone.GetTraversalCandidates(out Vector3 outside, out _);
            Vector3 outward = zone.GetOutsideDirection();
            float bestScore = float.PositiveInfinity;
            bool found = false;
            float[] offsets = { 0f, .3f, .65f, 1.05f };
            float[] radii = { .22f, .4f, .7f };

            foreach (float offset in offsets)
            {
                Vector3 probe = outside + outward * offset;
                foreach (float radius in radii)
                {
                    if (!NavMesh.SamplePosition(
                            probe,
                            out NavMeshHit candidate,
                            radius,
                            m_NavAgent.areaMask) ||
                        zone.SignedSide(candidate.position) <= .04f)
                    {
                        continue;
                    }

                    Vector3 horizontalDelta =
                        Vector3.ProjectOnPlane(candidate.position - probe, Vector3.up);
                    float score =
                        horizontalDelta.sqrMagnitude +
                        Mathf.Abs(candidate.position.y - outside.y) * .35f;
                    if (score >= bestScore)
                        continue;

                    bestScore = score;
                    bestHit = candidate;
                    found = true;
                }
            }

            return found;
        }

        IEnumerator TraverseBoardedVehicleExit(
            VehicleZombieAccessZone zone,
            Vector3 outsideNavPosition)
        {
            bool wasCrouching = IsCrouching;
            bool crouch = zone != null && zone.IsWindow;
            Transform vehicle = m_BoardedVehicle;
            bool completed = false;
            SetCrouching(crouch);

            if (m_Animator != null)
            {
                m_Animator.SetBool(IsWalkingHash, true);
                m_Animator.SetFloat(
                    SpeedHash,
                    MoveSpeed * (crouch ? CrouchSpeedMultiplier : 1f));
            }

            if (zone != null && vehicle != null && zone.IsVehicleStationary())
            {
                zone.GetTraversalCandidates(out _, out Vector3 inside);
                Vector3 start = transform.position;
                float approachDuration = Mathf.Clamp(
                    Vector3.Distance(start, inside) /
                    Mathf.Max(.1f, MoveSpeed * CrouchSpeedMultiplier),
                    .2f,
                    1.35f);

                float elapsed = 0f;
                while (elapsed < approachDuration && !m_IsDead &&
                       zone != null && zone.isActiveAndEnabled &&
                       vehicle != null && zone.IsVehicleStationary())
                {
                    elapsed += Time.deltaTime;
                    zone.GetTraversalCandidates(out _, out inside);
                    float t = Mathf.Clamp01(elapsed / approachDuration);
                    float eased = t * t * (3f - 2f * t);
                    transform.position = Vector3.Lerp(start, inside, eased);
                    m_BoardedLocalPosition =
                        vehicle.InverseTransformPoint(transform.position);
                    yield return null;
                }

                if (!m_IsDead && zone != null && zone.isActiveAndEnabled &&
                    vehicle != null && zone.IsVehicleStationary())
                {
                    Vector3 exitStart = transform.position;
                    Vector3 exitEnd =
                        outsideNavPosition + Vector3.up * GroundOffset;
                    float exitDuration = crouch ? .72f : .48f;
                    elapsed = 0f;
                    while (elapsed < exitDuration && !m_IsDead &&
                           zone != null && zone.isActiveAndEnabled &&
                           vehicle != null && zone.IsVehicleStationary())
                    {
                        elapsed += Time.deltaTime;
                        float t = Mathf.Clamp01(elapsed / exitDuration);
                        float eased = t * t * (3f - 2f * t);
                        transform.position = Vector3.Lerp(exitStart, exitEnd, eased);

                        Vector3 direction = exitEnd - exitStart;
                        direction.y = 0f;
                        if (direction.sqrMagnitude > .001f)
                        {
                            transform.rotation = Quaternion.Slerp(
                                transform.rotation,
                                Quaternion.LookRotation(direction),
                                eased);
                        }
                        yield return null;
                    }

                    completed = !m_IsDead && zone != null &&
                                zone.isActiveAndEnabled && vehicle != null &&
                                zone.IsVehicleStationary();
                }
            }

            if (completed)
            {
                m_BoardedVehicle = null;
                transform.position =
                    outsideNavPosition + Vector3.up * GroundOffset;
                if (m_NavAgent != null)
                {
                    if (!m_NavAgent.enabled)
                        m_NavAgent.enabled = true;
                    m_NavAgent.Warp(outsideNavPosition);
                    m_NavAgent.nextPosition = outsideNavPosition;
                    m_NavAgent.isStopped = false;
                }
            }
            else if (vehicle != null)
            {
                m_BoardedLocalPosition =
                    vehicle.InverseTransformPoint(transform.position);
            }

            SetCrouching(wasCrouching);
            ClearVehicleAccessTarget();
            m_LinkTraversalRoutine = null;
        }

        void RestoreNavigationAfterVehicleBoarding()
        {
            m_BoardedOnRoof=false;
            m_BoardedVehicle = null;
            if (m_NavAgent == null)
                return;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit,
                    2f, m_NavAgent.areaMask))
            {
                m_NavAgent.enabled = true;
                m_NavAgent.Warp(hit.position);
                transform.position = hit.position + Vector3.up * GroundOffset;
                m_NavAgent.isStopped = false;
            }
        }

        public bool IsBoardingOrOnVehicle(Transform vehicleRoot)
        {
            if (vehicleRoot == null)
                return false;

            if (m_BoardedVehicle == vehicleRoot)
                return true;

            return m_LinkTraversalRoutine != null &&
                   m_VehicleAccessTarget != null &&
                   m_VehicleAccessTarget.VehicleRoot == vehicleRoot;
        }

        public void ConfigureLargeZombie()
        {
            CanUseNarrowOpenings = false;
            AttackDistance = Mathf.Max(AttackDistance, 2.5f);
            if (m_NavAgent == null) m_NavAgent = GetComponent<NavMeshAgent>();
            if (m_NavAgent != null)
            {
                m_NavAgent.radius = Mathf.Max(m_NavAgent.radius, .75f);
                m_NavAgent.height = Mathf.Max(m_NavAgent.height, 3.2f);
            }
        }

        // Physical safety net so zombies can never tunnel through solid geometry
        // even if a navmesh bake mistake leaves walkable area overlapping a wall.
        const float WallCheckHeight = .9f;
        const float WallCheckRadius = .28f;

        Vector3 ClampAgainstSolidGeometry(
            Vector3 from,
            Vector3 to,
            out bool approachingAuthoredVehicleAccess)
        {
            approachingAuthoredVehicleAccess = false;

            Vector3 vehicleSafe =
                ClampAgainstVehicleGeometry(from, to, out bool blockedByVehicle);

            // If the normal wall guard is the only thing keeping us from an explicitly
            // authored Door/Window/Ladder link, steer the tiny movement step toward that
            // endpoint and temporarily let the authored link own the crossing. This is NOT
            // a general "ignore the bus" exception: it only exists inside a small radius
            // around a named access link and only while moving closer to that endpoint.
            if (blockedByVehicle &&
                TryGetAuthoredVehicleAccessApproach(from, to, out Vector3 accessEndpoint))
            {
                float stepDistance = Vector3.Distance(from, to);
                if (stepDistance > .0001f)
                {
                    vehicleSafe = Vector3.MoveTowards(from, accessEndpoint, stepDistance);
                    approachingAuthoredVehicleAccess = true;
                }
            }

            Vector3 delta = vehicleSafe - from;
            float distance = delta.magnitude;

            if (distance < .0001f)
            {
                return approachingAuthoredVehicleAccess
                    ? vehicleSafe
                    : ResolveVehiclePenetration(vehicleSafe);
            }

            Vector3 direction = delta / distance;
            Vector3 castOrigin = from + Vector3.up * WallCheckHeight;

            // Scenery still uses the old generic safety net. Vehicle colliders are excluded
            // here because they are handled by the dedicated multi-height wall probes below.
            RaycastHit[] hits = Physics.SphereCastAll(
                castOrigin,
                WallCheckRadius,
                direction,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = distance;
            bool foundSolid = false;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(transform) ||
                    hit.collider.GetComponentInParent<ZombieAI>() != null ||
                    hit.collider.GetComponentInParent<PlayerCharacterController>() != null ||
                    IsVehicleCollider(hit.collider))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    foundSolid = true;
                }
            }

            Vector3 result = vehicleSafe;

            if (foundSolid)
            {
                float safeDistance = Mathf.Max(
                    0f,
                    nearestDistance - WallCheckRadius * .5f);

                result = from + direction * safeDistance;
            }

            // During the final approach to an authored access link the manual link traversal
            // is about to take over. Running depenetration here can push the zombie away from
            // the exact endpoint and recreate the "queues at the bus door forever" bug.
            return approachingAuthoredVehicleAccess
                ? result
                : ResolveVehiclePenetration(result);
        }

        Vector3 ClampAgainstVehicleGeometry(
            Vector3 from,
            Vector3 to,
            out bool blockedByVehicle)
        {
            blockedByVehicle = false;

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < .0001f)
                return from;

            Vector3 direction = delta / distance;
            float probeRadius = Mathf.Clamp(VehicleWallProbeRadius, .05f, .30f);

            // Do NOT use one full NavMeshAgent-sized capsule here. The previous clean version
            // did that and effectively made a bus doorway narrower by the zombie's full radius,
            // while also treating the bus floor/step as a wall. Three small probes are enough
            // to catch actual vertical body panels at torso/head height without blocking a
            // walkable step into the bus.
            float agentHeight = m_NavAgent != null
                ? Mathf.Max(1.2f, m_NavAgent.height)
                : 1.8f;

            float[] heights =
            {
                Mathf.Min(agentHeight - .12f, .72f),
                Mathf.Min(agentHeight - .10f, 1.18f),
                Mathf.Max(.78f, agentHeight - .18f)
            };

            float nearestVehicleDistance = float.PositiveInfinity;
            Collider nearestVehicleCollider = null;

            for (int h = 0; h < heights.Length; h++)
            {
                float height = Mathf.Clamp(heights[h], .55f, Mathf.Max(.56f, agentHeight - .08f));
                Vector3 origin = from + Vector3.up * (height + GroundOffset);

                int hitCount = Physics.SphereCastNonAlloc(
                    origin,
                    probeRadius,
                    direction,
                    m_VehicleCastHits,
                    distance + VehicleGeometrySkin,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = m_VehicleCastHits[i];
                    m_VehicleCastHits[i] = default;

                    if (hit.collider == null ||
                        hit.collider.transform.IsChildOf(transform) ||
                        !IsVehicleCollider(hit.collider))
                    {
                        continue;
                    }

                    if (CanPassThroughCurrentVehicleAccess(
                            hit.collider,
                            from,
                            to))
                    {
                        continue;
                    }

                    // Up-facing vehicle surfaces are floors, steps or seat tops. They should
                    // not veto horizontal NavMesh movement; the NavMesh already decides whether
                    // that vertical transition is walkable.
                    if (Vector3.Dot(hit.normal.normalized, Vector3.up) >=
                        VehicleFloorNormalThreshold)
                    {
                        continue;
                    }

                    if (hit.distance < nearestVehicleDistance)
                    {
                        nearestVehicleDistance = hit.distance;
                        nearestVehicleCollider = hit.collider;
                    }
                }
            }

            if (nearestVehicleDistance == float.PositiveInfinity)
                return to;

            blockedByVehicle = true;

            // The direct path is trying to cross the shell. Pick an authored opening on this
            // SAME vehicle so subsequent SetDestination calls route toward a real door/window
            // instead of repeatedly walking into the side of the bus.
            TryAcquireVehicleAccessTarget(from, nearestVehicleCollider);

            float safeDistance = Mathf.Clamp(
                nearestVehicleDistance - VehicleGeometrySkin,
                0f,
                distance);

            return from + direction * safeDistance;
        }

        bool TryGetAuthoredVehicleAccessApproach(
            Vector3 from,
            Vector3 to,
            out Vector3 accessEndpoint)
        {
            accessEndpoint = default;

            NavigationLink[] links = FindObjectsByType<NavigationLink>();
            if (links == null || links.Length == 0)
                return false;

            float maxDistance = Mathf.Max(.35f, VehicleAccessLinkApproachRadius);
            float maxDistanceSqr = maxDistance * maxDistance;
            float bestDistanceSqr = float.PositiveInfinity;

            foreach (NavigationLink candidate in links)
            {
                if (candidate == null ||
                    !candidate.enabled ||
                    !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                bool isDoor = candidate.name.IndexOf(
                    "Door",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool isWindow = candidate.name.IndexOf(
                    "Window",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool isLadder = candidate.name.IndexOf(
                    "Ladder",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isDoor && !isWindow && !isLadder)
                    continue;

                if (!CanUseNarrowOpenings && (isDoor || isWindow))
                    continue;

                Vector3 a =
                    candidate.transform.TransformPoint(candidate.startPoint);
                Vector3 b =
                    candidate.transform.TransformPoint(candidate.endPoint);

                Vector3 entry;
                Vector3 exit;

                if ((from - a).sqrMagnitude <= (from - b).sqrMagnitude)
                {
                    entry = a;
                    exit = b;
                }
                else
                {
                    entry = b;
                    exit = a;
                }

                float currentDistanceSqr = (from - entry).sqrMagnitude;
                if (currentDistanceSqr > maxDistanceSqr ||
                    currentDistanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                // Only grant the exception when this frame is actually moving toward the
                // nearby endpoint, not merely because a zombie happens to stand beside a window.
                float nextDistanceSqr = (to - entry).sqrMagnitude;
                if (nextDistanceSqr > currentDistanceSqr + .0025f)
                    continue;

                // If we know the target, the far side of the link should not be clearly worse.
                // This prevents zombies from using a nearby bus window to cross the wrong way.
                if (m_PlayerTransform != null)
                {
                    float entryToTarget =
                        Vector3.Distance(entry, m_PlayerTransform.position);
                    float exitToTarget =
                        Vector3.Distance(exit, m_PlayerTransform.position);

                    if (exitToTarget > entryToTarget + .65f)
                        continue;
                }

                accessEndpoint = entry;
                bestDistanceSqr = currentDistanceSqr;
            }

            return bestDistanceSqr < float.PositiveInfinity;
        }

        Vector3 ResolveVehiclePenetration(Vector3 candidate)
        {
            if (m_BodyCollider == null ||
                !m_BodyCollider.enabled ||
                m_BodyCollider.isTrigger)
            {
                return candidate;
            }

            float queryRadius = m_NavAgent != null
                ? Mathf.Max(1f, m_NavAgent.height)
                : Mathf.Max(1f, m_BodyCollider.bounds.extents.magnitude + .5f);

            Vector3 queryCenter =
                candidate +
                Vector3.up * (queryRadius * .45f);

            // Only resolve SIDE penetration into vehicle bodywork. Physics contacts between
            // zombie and vehicle are intentionally disabled, so this prevents tunnelling through
            // walls/seats without treating the bus floor or entry step as something that must
            // eject the zombie back outside.
            for (int pass = 0; pass < 4; pass++)
            {
                int overlapCount = Physics.OverlapSphereNonAlloc(
                    queryCenter,
                    queryRadius,
                    m_VehicleOverlapHits,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                bool moved = false;

                for (int i = 0; i < overlapCount; i++)
                {
                    Collider vehicleCollider = m_VehicleOverlapHits[i];
                    m_VehicleOverlapHits[i] = null;

                    if (vehicleCollider == null ||
                        vehicleCollider.transform.IsChildOf(transform) ||
                        !vehicleCollider.enabled ||
                        vehicleCollider.isTrigger ||
                        !IsVehicleCollider(vehicleCollider))
                    {
                        continue;
                    }

                    if (CanPassThroughCurrentVehicleAccess(
                            vehicleCollider,
                            candidate,
                            candidate))
                    {
                        continue;
                    }

                    if (!Physics.ComputePenetration(
                            m_BodyCollider,
                            candidate,
                            transform.rotation,
                            vehicleCollider,
                            vehicleCollider.transform.position,
                            vehicleCollider.transform.rotation,
                            out Vector3 separationDirection,
                            out float separationDistance))
                    {
                        continue;
                    }

                    if (separationDistance <= 0f ||
                        separationDirection.sqrMagnitude < .000001f)
                    {
                        continue;
                    }

                    Vector3 normalizedSeparation = separationDirection.normalized;

                    // Floor/step/ceiling penetration is left to NavMesh vertical placement.
                    // We only need to stop horizontal tunnelling through the bus shell/interior.
                    if (Mathf.Abs(Vector3.Dot(
                            normalizedSeparation,
                            Vector3.up)) >= VehicleFloorNormalThreshold)
                    {
                        continue;
                    }

                    candidate +=
                        normalizedSeparation *
                        (separationDistance + VehicleGeometrySkin);

                    queryCenter =
                        candidate +
                        Vector3.up * (queryRadius * .45f);

                    moved = true;
                }

                if (!moved)
                    break;
            }

            return candidate;
        }


        void UpdateVehicleAccessState()
        {
            if (m_VehicleAccessTarget == null)
            {
                SetVehicleAccessCrouch(false);
                m_VehicleAccessCrossing = false;
                return;
            }

            if (!m_VehicleAccessTarget.isActiveAndEnabled ||
                Time.time > m_VehicleAccessTargetUntil)
            {
                ClearVehicleAccessTarget();
                return;
            }

            float crouchDistance =
                Mathf.Max(.2f, VehicleWindowCrouchDistance);

            if (m_VehicleAccessTarget.IsWindow &&
                m_VehicleAccessTarget.SqrDistance(transform.position) <=
                    crouchDistance * crouchDistance)
            {
                SetVehicleAccessCrouch(true);
            }
            else
            {
                SetVehicleAccessCrouch(false);
            }

            if (!m_VehicleAccessCrossing)
                return;

            // Keep the opening active until the zombie has actually moved clear of the volume
            // on the other side. This is what prevents an immediate re-clamp on the inner edge.
            float clearMargin = Mathf.Max(.25f, VehicleAccessPassMargin * 1.5f);
            if (!m_VehicleAccessTarget.ContainsOrNear(transform.position, clearMargin))
            {
                if (m_VehicleAccessTarget.VehicleRoot != null &&
                    m_VehicleAccessTarget.SignedSide(transform.position) < -.05f &&
                    m_PlayerTransform != null &&
                    m_VehicleAccessTarget.SignedSide(m_PlayerTransform.position) < -.05f)
                {
                    m_BoardedVehicle = m_VehicleAccessTarget.VehicleRoot;
                    m_BoardedLocalPosition =
                        m_BoardedVehicle.InverseTransformPoint(transform.position);
                    if (m_NavAgent != null && m_NavAgent.enabled)
                    {
                        m_NavAgent.ResetPath();
                        m_NavAgent.isStopped = true;
                        m_NavAgent.enabled = false;
                    }
                }

                ClearVehicleAccessTarget();
            }
        }

        void SetVehicleAccessCrouch(bool crouch)
        {
            if (crouch)
            {
                if (!m_VehicleAccessForcedCrouch)
                {
                    m_CrouchBeforeVehicleAccess = IsCrouching;
                    m_VehicleAccessForcedCrouch = true;
                }

                if (!IsCrouching)
                    SetCrouching(true);

                return;
            }

            if (!m_VehicleAccessForcedCrouch)
                return;

            m_VehicleAccessForcedCrouch = false;
            SetCrouching(m_CrouchBeforeVehicleAccess);
        }

        void ClearVehicleAccessTarget()
        {
            m_VehicleAccessTarget = null;
            m_VehicleAccessTargetUntil = float.NegativeInfinity;
            m_VehicleAccessCrossing = false;
            SetVehicleAccessCrouch(false);
        }

        bool CanPassThroughCurrentVehicleAccess(
            Collider vehicleCollider,
            Vector3 from,
            Vector3 to)
        {
            if (m_VehicleAccessTarget == null ||
                !m_VehicleAccessTarget.isActiveAndEnabled ||
                !m_VehicleAccessTarget.BelongsToVehicle(vehicleCollider))
            {
                return false;
            }

            float margin = Mathf.Max(.05f, VehicleAccessPassMargin);

            // Passage is granted only right at the authored opening. This keeps every other
            // part of the bus shell fully solid from the zombie's point of view.
            if (m_VehicleAccessTarget.ContainsOrNear(from, margin) ||
                m_VehicleAccessTarget.ContainsOrNear(to, margin))
            {
                return true;
            }

            return false;
        }

        void TryAcquireVehicleAccessTarget(
            Vector3 from,
            Collider blockingVehicleCollider)
        {
            if (m_VehicleAccessTarget != null &&
                m_VehicleAccessTarget.isActiveAndEnabled &&
                Time.time <= m_VehicleAccessTargetUntil)
            {
                return;
            }

            VehicleZombieAccessZone best = null;
            float bestDistanceSqr = float.PositiveInfinity;

            foreach (VehicleZombieAccessZone zone in VehicleZombieAccessZone.Active)
            {
                if (zone == null || !zone.isActiveAndEnabled)
                    continue;

                if (blockingVehicleCollider != null &&
                    !zone.BelongsToVehicle(blockingVehicleCollider))
                {
                    continue;
                }

                if (!CanUseNarrowOpenings &&
                    (zone.AccessType == VehicleZombieAccessType.Door ||
                     zone.AccessType == VehicleZombieAccessType.Window))
                {
                    continue;
                }

                float distanceSqr = zone.SqrDistance(from);
                float allowed =
                    Mathf.Max(.1f, zone.ApproachMargin) +
                    Mathf.Max(.35f, VehicleAccessLinkApproachRadius);

                if (distanceSqr > allowed * allowed)
                    continue;

                // The opening must belong to a vehicle near the current block. We do not have
                // the specific hit collider here, so choose the closest active authored vehicle
                // opening. Because this method is called only after a vehicle wall hit, this is
                // normally the bus/forklift directly in front of the zombie.
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = zone;
                }
            }

            if (best == null)
                return;

            m_VehicleAccessTarget = best;
            m_VehicleAccessTargetUntil =
                Time.time + Mathf.Max(.5f, VehicleAccessTargetTimeout);
            m_VehicleAccessCrossing = false;
        }

        void TryAcquireStationaryVehicleAccess(Vector3 from)
        {
            if (m_PlayerTransform == null ||
                Time.time < m_NextStationaryVehicleAccessSearchTime)
                return;

            m_NextStationaryVehicleAccessSearchTime = Time.time + .2f;
            VehicleZombieAccessZone best = null;
            float bestDistanceSqr = float.PositiveInfinity;
            Transform evaluatedVehicle = null;
            bool playerInsideEvaluatedVehicle = false;

            foreach (VehicleZombieAccessZone zone in VehicleZombieAccessZone.Active)
            {
                if (zone == null || !zone.isActiveAndEnabled ||
                    zone.VehicleRoot == null || !zone.IsVehicleStationary() ||
                    (!CanUseNarrowOpenings &&
                     (zone.AccessType == VehicleZombieAccessType.Door ||
                      zone.AccessType == VehicleZombieAccessType.Window)))
                {
                    continue;
                }

                if (zone.VehicleRoot != evaluatedVehicle)
                {
                    evaluatedVehicle = zone.VehicleRoot;
                    playerInsideEvaluatedVehicle =
                        IsPointWithinVehicleBounds(
                            evaluatedVehicle,
                            m_PlayerTransform.position,
                            .35f);
                }

                if (!playerInsideEvaluatedVehicle)
                {
                    continue;
                }

                if (zone.SignedSide(from) <= .02f ||
                    zone.SignedSide(m_PlayerTransform.position) >= -.02f)
                {
                    continue;
                }

                Vector3 approach = zone.GetApproachPoint(from);
                float distanceSqr = (approach - from).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    best = zone;
                    bestDistanceSqr = distanceSqr;
                }
            }

            if (best == null)
                return;

            m_VehicleAccessTarget = best;
            m_VehicleAccessTargetUntil =
                Time.time + Mathf.Max(.5f, VehicleAccessTargetTimeout);
            m_VehicleAccessCrossing = false;
        }

        bool IsPointWithinVehicleBounds(
            Transform vehicleRoot,
            Vector3 point,
            float margin)
        {
            if (vehicleRoot == null)
                return false;

            var defenseLayout=vehicleRoot.GetComponent<ZombieTown.Foundation.BusDefenseLayout>();
            if(defenseLayout!=null)return defenseLayout.ContainsPassenger(point,margin);

            // Avoid scanning vehicle colliders for the overwhelmingly common case where
            // the player is nowhere near this vehicle.
            if ((point - vehicleRoot.position).sqrMagnitude > 15f * 15f)
                return false;

            Bounds bounds = default;
            bool found = false;
            m_VehicleBoundsColliders.Clear();
            vehicleRoot.GetComponentsInChildren(
                true,
                m_VehicleBoundsColliders);
            foreach (Collider collider in m_VehicleBoundsColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (!found)
                return false;

            bounds.Expand(Mathf.Max(0f, margin) * 2f);
            return bounds.Contains(point);
        }

        static bool IsVehicleCollider(Collider collider)
        {
            if (collider == null) return false;

            // Vehicle scripts normally live on the Rigidbody root, while the collider may
            // be on that root or one of its children.
            Transform searchRoot = collider.attachedRigidbody != null
                ? collider.attachedRigidbody.transform
                : collider.transform;

            return searchRoot.GetComponentInParent<BusDriver>() != null ||
                   searchRoot.GetComponentInParent<ForkliftDriver>() != null;
        }

        static bool IsNamedLink(Vector3 start, Vector3 end, string nameFragment)
        {
            foreach (NavigationLink candidate in FindObjectsByType<NavigationLink>())
            {
                Vector3 candidateStart = candidate.transform.TransformPoint(candidate.startPoint);
                Vector3 candidateEnd = candidate.transform.TransformPoint(candidate.endPoint);
                bool sameDirection = Vector3.Distance(start, candidateStart) < 1.2f &&
                                     Vector3.Distance(end, candidateEnd) < 1.2f;
                bool reverseDirection = Vector3.Distance(start, candidateEnd) < 1.2f &&
                                        Vector3.Distance(end, candidateStart) < 1.2f;
                if (!sameDirection && !reverseDirection) continue;
                if (candidate.name.IndexOf(nameFragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            foreach (Unity.AI.Navigation.NavMeshLink candidate in FindObjectsByType<Unity.AI.Navigation.NavMeshLink>())
            {
                Vector3 candidateStart = candidate.transform.TransformPoint(candidate.startPoint);
                Vector3 candidateEnd = candidate.transform.TransformPoint(candidate.endPoint);
                bool sameDirection = Vector3.Distance(start, candidateStart) < 1.2f &&
                                     Vector3.Distance(end, candidateEnd) < 1.2f;
                bool reverseDirection = Vector3.Distance(start, candidateEnd) < 1.2f &&
                                        Vector3.Distance(end, candidateStart) < 1.2f;
                if (!sameDirection && !reverseDirection) continue;
                if (candidate.name.IndexOf(nameFragment, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidate.gameObject.name.IndexOf(nameFragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        void AlignToGround()
        {
            Collider collider = GetComponent<Collider>();
            if (collider == null || collider.isTrigger)
                return;

            Vector3 rayOrigin = transform.position + Vector3.up * 1.5f;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 4f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            RaycastHit groundHit = default;
            bool foundGround = false;
            foreach (RaycastHit candidate in hits)
            {
                if (candidate.transform.IsChildOf(transform) || candidate.normal.y < 0.4f)
                    continue;

                if (!foundGround || candidate.point.y > groundHit.point.y)
                {
                    groundHit = candidate;
                    foundGround = true;
                }
            }

            if (!foundGround)
                return;

            float bottomY = GetColliderBottomY(collider);
            float offset = groundHit.point.y - bottomY + GroundOffset;
            if (Mathf.Abs(offset) > 0.005f)
            {
                transform.position += Vector3.up * offset;
            }
        }

        float GetColliderBottomY(Collider collider)
        {
            if (collider is CapsuleCollider capsule)
            {
                Vector3 localBottom = capsule.center - Vector3.up * (capsule.height * 0.5f);
                return collider.transform.TransformPoint(localBottom).y;
            }

            if (collider is BoxCollider box)
            {
                Vector3 localBottom = box.center - Vector3.up * (box.size.y * 0.5f);
                return collider.transform.TransformPoint(localBottom).y;
            }

            if (collider is SphereCollider sphere)
            {
                Vector3 localBottom = sphere.center - Vector3.up * sphere.radius;
                return collider.transform.TransformPoint(localBottom).y;
            }

            return collider.bounds.min.y;
        }

        void PerformAttack()
        {
            if (!IsCombatAuthority) return;
            if (m_IsAttacking || m_IsDead)
                return;

            m_AttackRoutine = StartCoroutine(AttackRoutine());
        }

        IEnumerator AttackRoutine()
        {
            if (!IsCombatAuthority)
            {
                m_IsAttacking = false;
                m_AttackRoutine = null;
                yield break;
            }

            m_IsAttacking = true;
            m_NextAttackTime = float.PositiveInfinity;

            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.isStopped = true;
            }

            if (m_Animator != null)
            {
                m_Animator.SetBool(IsWalkingHash, false);
                m_Animator.SetFloat(SpeedHash, 0f);
            }
            // Only the server runs this coroutine; replicate the trigger so clients see the swing too.
            GetComponent<NetworkZombieAnimator>()?.PlayAttack();

            if (m_ZombieAudio != null)
            {
                m_ZombieAudio.PlayAttack();
            }
            else if (AttackSfx != null)
            {
                AudioUtility.CreateSFX(AttackSfx, transform.position, AudioUtility.AudioGroups.EnemyAttack, 0f);
            }

            yield return new WaitForSeconds(Mathf.Max(0f, AttackHitDelay));

            if (IsCombatAuthority && !m_IsDead && m_PlayerHealth != null && m_PlayerTransform != null &&
                IsValidCombatTarget(m_PlayerTransform))
            {
                float distance = Vector3.Distance(transform.position, m_PlayerTransform.position);
                if (distance <= AttackDistance + 0.4f)
                {
                    m_PlayerHealth.TakeDamage(AttackDamage, gameObject);
                    m_ZombieAudio?.PlayPlayerHit();
                }
            }

            yield return new WaitForSeconds(Mathf.Max(0f, AttackAnimationDuration - AttackHitDelay));

            m_NextAttackTime = Time.time + AttackInterval;
            m_IsAttacking = false;
            m_AttackRoutine = null;
        }

        public void ForceFerryFall(Vector3 ferryCenter)
        {
            if (m_IsDead || !isActiveAndEnabled) return;
            m_IsDead = true;
            m_IsAttacking = false;
            if (m_AttackRoutine != null) StopCoroutine(m_AttackRoutine);
            if (m_LinkTraversalRoutine != null) StopCoroutine(m_LinkTraversalRoutine);
            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.isStopped = true;
                m_NavAgent.enabled = false;
            }
            foreach (Collider collider in GetComponentsInChildren<Collider>())
                collider.enabled = false;
            StartCoroutine(FerryFallRoutine(ferryCenter));
        }

        IEnumerator FerryFallRoutine(Vector3 ferryCenter)
        {
            Vector3 start = transform.position;
            Vector3 landing = new Vector3(ferryCenter.x, start.y - 4f, ferryCenter.z);
            float elapsed = 0f;
            while (elapsed < .45f)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / .45f);
                transform.position = Vector3.Lerp(start, landing, progress);
                yield return null;
            }

            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                networkObject.Despawn(true);
            else if (networkObject == null || !networkObject.IsSpawned)
                Destroy(gameObject);
        }

        void ApplyRemoteDeathVisuals()
        {
            if (m_IsDead) return;
            m_IsDead = true;
            ReleaseBusOpening();
            m_IsAttacking = false;

            if (m_AttackRoutine != null)
            {
                StopCoroutine(m_AttackRoutine);
                m_AttackRoutine = null;
            }
            if (m_LinkTraversalRoutine != null)
            {
                StopCoroutine(m_LinkTraversalRoutine);
                m_LinkTraversalRoutine = null;
            }

            if (m_Animator != null)
            {
                m_Animator.applyRootMotion = false;
                m_Animator.ResetTrigger(AttackHash);
                m_Animator.SetBool(IsWalkingHash, false);
                m_Animator.SetFloat(SpeedHash, 0f);
                m_Animator.SetBool(IsDeadHash, true);
                m_Animator.SetBool(IsCrouchingHash, false);
                m_Animator.SetBool(IsClimbingHash, false);
                m_Animator.ResetTrigger(DieTriggerHash);
                m_Animator.ResetTrigger(HitHash);
                m_Animator.ResetTrigger(KnockbackHash);
                m_Animator.ResetTrigger(StaggerHash);
                m_Animator.ResetTrigger(SpecialAttackHash);
                m_Animator.ResetTrigger(HeadshotDieHash);

                int deathState = Animator.StringToHash(DeathStateName);
                if (m_WasLethalHeadshot && !string.IsNullOrWhiteSpace(HeadshotDeathStateName))
                {
                    int headshotState = Animator.StringToHash(HeadshotDeathStateName);
                    if (m_Animator.HasState(0, headshotState))
                        deathState = headshotState;
                }

                if (m_Animator.HasState(0, deathState))
                {
                    float startOffset = m_WasLethalHeadshot
                        ? HeadshotDeathAnimationStartOffset
                        : DeathAnimationStartOffset;
                    m_Animator.speed = 1f;
                    m_Animator.Play(deathState, 0, startOffset);
                }
                else
                {
                    m_Animator.SetTrigger(DieTriggerHash);
                }

                m_Animator.Update(0f);
            }

            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.isStopped = true;
                m_NavAgent.enabled = false;
            }

            Collider[] colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = false;
            }

            if (m_ZombieAudio != null)
            {
                m_ZombieAudio.PlayDeath();
            }
            else if (DeathSfx != null)
            {
                AudioUtility.CreateSFX(DeathSfx, transform.position, AudioUtility.AudioGroups.EnemyDetection, 0f);
            }
        }

        void OnDeath()
        {
            if (m_IsDead) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
            {
                ApplyRemoteDeathVisuals();
                return;
            }

            m_IsDead = true;
            ReleaseBusOpening();
            m_IsAttacking = false;

            if (IsSpawned && IsServer)
            {
                SyncedHealth.Value = m_Health != null ? m_Health.CurrentHealth : 0f;
                IsDeadNetworkState.Value = true;
            }

            if (m_AttackRoutine != null)
            {
                StopCoroutine(m_AttackRoutine);
                m_AttackRoutine = null;
            }
            if (m_LinkTraversalRoutine != null)
            {
                StopCoroutine(m_LinkTraversalRoutine);
                m_LinkTraversalRoutine = null;
            }

            if (m_Animator != null)
            {
                m_Animator.applyRootMotion = false;
                m_Animator.ResetTrigger(AttackHash);
                m_Animator.SetBool(IsWalkingHash, false);
                m_Animator.SetFloat(SpeedHash, 0f);
                m_Animator.SetBool(IsDeadHash, true);
                m_Animator.SetBool(IsCrouchingHash, false);
                m_Animator.SetBool(IsClimbingHash, false);
                m_Animator.ResetTrigger(DieTriggerHash);
                m_Animator.ResetTrigger(HitHash);
                m_Animator.ResetTrigger(KnockbackHash);
                m_Animator.ResetTrigger(StaggerHash);
                m_Animator.ResetTrigger(SpecialAttackHash);
                m_Animator.ResetTrigger(HeadshotDieHash);

                int deathState = Animator.StringToHash(DeathStateName);
                if (m_WasLethalHeadshot && !string.IsNullOrWhiteSpace(HeadshotDeathStateName))
                {
                    int headshotState = Animator.StringToHash(HeadshotDeathStateName);
                    if (m_Animator.HasState(0, headshotState))
                    {
                        deathState = headshotState;
                    }
                }

                if (m_Animator.HasState(0, deathState))
                {
                    float startOffset = m_WasLethalHeadshot
                        ? HeadshotDeathAnimationStartOffset
                        : DeathAnimationStartOffset;
                    m_Animator.speed = 1f;
                    // Direct Play intentionally bypasses locomotion transitions so
                    // a custom controller route cannot delay the lethal reaction.
                    m_Animator.Play(deathState, 0, startOffset);
                }
                else
                {
                    Debug.LogWarning($"Zombie death state '{DeathStateName}' was not found; using Die trigger.", this);
                    m_Animator.SetTrigger(DieTriggerHash);
                }

                m_Animator.Update(0f);
            }

            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.isStopped = true;
                m_NavAgent.enabled = false;
            }

            // Disable colliders
            Collider[] colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = false;
            }

            DropLoot();

            ZombieDismemberment deathDismemberment = GetComponent<ZombieDismemberment>();
            if (deathDismemberment != null)
            {
                Vector3 bloodPoint = m_WasLethalHeadshot && m_HeadshotPoint != Vector3.zero
                    ? m_HeadshotPoint
                    : transform.position + Vector3.up * 1.1f;
                Quaternion bloodRotation = m_HeadTransform != null ? m_HeadTransform.rotation : transform.rotation;
                deathDismemberment.PlayImmediateBlood(bloodPoint, bloodRotation);
            }

            if (m_WasLethalHeadshot)
            {
                PlayHeadshotDeath();
            }

            // Raise kill event for FPS framework UI & objective tracking
            EnemyKillEvent killEvt = Events.EnemyKillEvent;
            killEvt.Enemy = gameObject;
            killEvt.RemainingEnemyCount = Mathf.Max(0, GameObject.FindGameObjectsWithTag("Enemy").Length - 1);
            EventManager.Broadcast(killEvt);

            if (m_ZombieAudio != null)
            {
                m_ZombieAudio.PlayDeath();
            }
            else if (DeathSfx != null)
            {
                AudioUtility.CreateSFX(DeathSfx, transform.position, AudioUtility.AudioGroups.EnemyDetection, 0f);
            }

            StartCoroutine(DespawnAfterDeath(Mathf.Max(DeathAnimationDuration, 2.2f)));
        }

        void OnDamaged(float damage, GameObject source)
        {
            if (m_IsDead || damage <= 0f) return;
            if (source != null) m_LastDamageSource = source;

            if (IsCombatAuthority && IsSpawned && m_Health != null)
            {
                SyncedHealth.Value = m_Health.CurrentHealth;
                if (m_Health.CurrentHealth <= 0f)
                    IsDeadNetworkState.Value = true;
            }

            // Health invokes OnDamaged after subtracting damage but before OnDie.
            // Never arm Hit for a lethal frame: that trigger could interrupt the
            // directly played death state on the next Animator evaluation.
            bool lethal = m_Health != null && m_Health.CurrentHealth <= 0f;
            if (lethal)
            {
                m_WasLethalHeadshot = m_HasPendingHeadshot;
                m_HasPendingHeadshot = false;
                return;
            }

            m_HasPendingHeadshot = false;
            m_ZombieAudio?.PlayHurt();
            // A knockback (for example the Striker's charged sword sweep) owns the
            // full-body reaction. The damage event arrives immediately afterwards;
            // starting the regular Hit trigger here used to cancel the fall pose.
            if (Time.time < m_KnockbackUntil) return;
            GetComponent<NetworkZombieAnimator>()?.PlayHit();
        }

        /// <summary>
        /// Makes the zombie yield to a vehicle.
        /// A stationary/slow vehicle always wins, but does NOT trigger the fall/knockback animation.
        /// Only a vehicle at or above VehicleFallSpeed uses the existing ApplyKnockback reaction.
        /// </summary>
        public void YieldToVehicle(Vector3 vehiclePosition, Vector3 vehicleVelocity)
        {
            if (m_IsDead)
                return;

            // A NavMeshAgent can otherwise keep correcting itself back into a vehicle.
            // Keep it stopped for a short moment so even a stationary vehicle "wins".
            m_VehicleYieldUntil = Mathf.Max(
                m_VehicleYieldUntil,
                Time.time + Mathf.Max(.05f, VehicleYieldDuration));

            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                m_NavAgent.isStopped = true;
                m_NavAgent.velocity = Vector3.zero;
                m_NavAgent.ResetPath();

                if (!m_NavAgent.updatePosition)
                    m_NavAgent.nextPosition = transform.position - Vector3.up * GroundOffset;
            }

            if (Time.time < m_NextVehicleReactionTime)
                return;

            m_NextVehicleReactionTime =
                Time.time + Mathf.Max(.02f, VehicleReactionCooldown);

            Vector3 fromVehicle = transform.position - vehiclePosition;
            fromVehicle.y = 0f;
            if (fromVehicle.sqrMagnitude < .001f)
                fromVehicle = -transform.forward;
            fromVehicle.Normalize();

            float speed = vehicleVelocity.magnitude;

            // A stationary/open vehicle can be a valid navigation destination.
            // Do not forcibly warp a zombie away from it; the vehicle scripts themselves
            // prevent the Rigidbody from being shoved around while parked.
            if (speed < 0.65f)
                return;

            Vector3 escapeDirection = fromVehicle;

            if (speed > .15f)
            {
                Vector3 travelDirection = vehicleVelocity;
                travelDirection.y = 0f;

                if (travelDirection.sqrMagnitude > .001f)
                {
                    travelDirection.Normalize();

                    // Prefer moving the zombie OUT of the vehicle's driving line,
                    // rather than simply pushing it straight ahead like a crate.
                    Vector3 sideways = Vector3.Cross(Vector3.up, travelDirection);
                    float sideSign = Vector3.Dot(fromVehicle, sideways) >= 0f ? 1f : -1f;
                    Vector3 sideEscape = sideways * sideSign;

                    // A small forward component prevents the zombie from being pulled
                    // back underneath the vehicle while it is moving.
                    escapeDirection =
                        (sideEscape * 1.25f + travelDirection * .20f + fromVehicle * .25f).normalized;
                }
            }

            // Keep the chosen side stable for a moment; rapidly changing sides can make
            // a zombie jitter in front of the bumper instead of getting out of the way.
            if (m_LastVehicleEscapeDirection.sqrMagnitude > .001f &&
                Vector3.Dot(m_LastVehicleEscapeDirection, escapeDirection) > .15f)
            {
                escapeDirection =
                    Vector3.Slerp(m_LastVehicleEscapeDirection, escapeDirection, .35f).normalized;
            }

            m_LastVehicleEscapeDirection = escapeDirection;

            float pushDistance = Mathf.Clamp(
                StationaryVehiclePushDistance + speed * VehiclePushPerMetrePerSecond,
                StationaryVehiclePushDistance,
                Mathf.Max(StationaryVehiclePushDistance, MaximumVehiclePushDistance));

            if (speed >= VehicleFallSpeed)
            {
                // At driving speed, use the existing knockback/fall reaction.
                ApplyKnockback(escapeDirection, pushDistance);
                return;
            }

            // At walking/parking speed the zombie just gets out of the way without
            // constantly playing a knockback animation.
            Vector3 target = transform.position + escapeDirection * pushDistance;

            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                if (NavMesh.SamplePosition(
                        target,
                        out NavMeshHit hit,
                        Mathf.Max(1f, pushDistance),
                        NavMesh.AllAreas))
                {
                    target = hit.position;
                }

                m_NavAgent.Warp(target);
                m_NavAgent.isStopped = true;
                m_NavAgent.velocity = Vector3.zero;
                m_NavAgent.nextPosition = target;
                transform.position = target + Vector3.up * GroundOffset;
            }
            else
            {
                transform.position = target;
            }

            if (m_Animator != null)
            {
                m_Animator.SetBool(IsWalkingHash, false);
                m_Animator.SetFloat(SpeedHash, 0f);
            }
        }

        public void ApplyKnockback(Vector3 direction, float distance)
        {
            if (m_IsDead || distance <= 0f) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f) direction = -transform.forward;
            direction.Normalize();

            if (m_AttackRoutine != null)
            {
                StopCoroutine(m_AttackRoutine);
                m_AttackRoutine = null;
            }
            m_IsAttacking = false;
            m_KnockbackUntil = Time.time + Mathf.Max(.75f, KnockbackRecoveryDuration);
            m_NextAttackTime = m_KnockbackUntil + .25f;

            Vector3 target = transform.position + direction * distance;
            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                if (NavMesh.SamplePosition(target, out NavMeshHit hit, Mathf.Max(1f, distance),
                        NavMesh.AllAreas))
                    target = hit.position;
                m_NavAgent.Warp(target);
                m_NavAgent.isStopped = true;
                transform.position = m_NavAgent.nextPosition + Vector3.up * GroundOffset;
            }
            else
            {
                transform.position = target;
            }

            GetComponent<NetworkZombieAnimator>()?.PlayKnockback(KnockbackRecoveryDuration);
        }

        public override void OnDestroy()
        {
            if (m_Health == null) return;
            m_Health.OnDie -= OnDeath;
            m_Health.OnDamaged -= OnDamaged;
            base.OnDestroy();
        }

        IEnumerator DespawnAfterDeath(float delay)
        {
            yield return new WaitForSeconds(delay);
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                networkObject.Despawn(true);
            else
                Destroy(gameObject);
        }

        public void PrepareHeadshot(float damage, Transform hitHead)
        {
            PrepareHeadshot(damage, hitHead, -transform.forward, hitHead != null ? hitHead.position : transform.position);
        }

        public void PrepareHeadshot(float damage, Transform hitHead, Vector3 hitDirection, Vector3 hitPoint)
        {
            if (hitHead != null) m_HeadTransform = hitHead;
            m_HeadshotDirection = hitDirection;
            m_HeadshotPoint = hitPoint;
            m_HasPendingHeadshot = !m_IsDead;
            GetComponent<ZombieDismemberment>()?.PlayImmediateBlood(hitPoint,
                m_HeadTransform != null ? m_HeadTransform.rotation : transform.rotation);
        }

        public void PrepareChainsawDecapitation(Vector3 hitDirection, Vector3 hitPoint)
        {
            if (m_IsDead) return;
            m_HeadshotDirection = hitDirection;
            m_HeadshotPoint = hitPoint;
            m_HasPendingHeadshot = true;
        }

        void SetupHeadHitbox()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.Equals("Head", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                ZombieHeadHitbox hitbox = child.GetComponent<ZombieHeadHitbox>();
                if (hitbox == null)
                {
                    hitbox = child.gameObject.AddComponent<ZombieHeadHitbox>();
                }

                hitbox.Initialize(this, HeadshotMultiplier, HeadHitboxRadius);

                if (m_HeadTransform == null && child.gameObject.activeInHierarchy)
                {
                    m_HeadTransform = child;
                }
            }
        }

        void PlayHeadshotDeath()
        {
            if (m_HeadTransform == null)
                return;

            ZombieDismemberment dismemberment = GetComponent<ZombieDismemberment>();
            if (dismemberment != null)
            {
                dismemberment.Configure(m_HeadTransform);
                dismemberment.Decapitate(m_HeadshotDirection, m_HeadshotPoint, HeadGibPrefab);
                return;
            }

            foreach (Renderer headRenderer in m_HeadTransform.GetComponentsInChildren<Renderer>(true))
            {
                headRenderer.enabled = false;
            }

            if (HeadGibPrefab == null)
                return;

            GameObject gib = Instantiate(HeadGibPrefab, m_HeadTransform.position, m_HeadTransform.rotation);
            Rigidbody gibBody = gib.GetComponent<Rigidbody>();
            if (gibBody == null)
            {
                gibBody = gib.AddComponent<Rigidbody>();
            }

            Vector3 forceDirection = (-transform.forward + Vector3.up * 0.28f).normalized;
            gibBody.linearDamping = .18f;
            gibBody.angularDamping = 1.1f;
            gibBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            gibBody.AddForce(forceDirection * Mathf.Min(HeadGibForce, 4.5f), ForceMode.Impulse);
            gibBody.AddTorque(Random.insideUnitSphere * Mathf.Min(HeadGibForce, 3.5f), ForceMode.Impulse);
            Destroy(gib, 2.8f);
        }

        void SpawnLootPrefab(GameObject prefab, Vector3 position)
        {
            if (prefab == null)
                return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (!NetworkManager.Singleton.IsServer)
                    return;

                NetworkObject prefabNetworkObject = prefab.GetComponent<NetworkObject>();
                if (prefabNetworkObject == null)
                {
                    Debug.LogError(
                        $"Loot prefab '{prefab.name}' is missing a NetworkObject. Register it as a Network Prefab in the NetworkManager.",
                        prefab);
                    return;
                }

                GameObject loot = Instantiate(prefab, position, Quaternion.identity);
                if (loot == null)
                    return;

                NetworkObject networkObject = loot.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError(
                        $"Spawned loot instance '{loot.name}' is missing a NetworkObject.",
                        loot);
                    return;
                }

                networkObject.Spawn();
                return;
            }

            Instantiate(prefab, position, Quaternion.identity);
        }

        void DropLoot()
        {
            Vector3 dropPosition = transform.position + Vector3.up * 0.5f;
            PlayerClassController killer = m_LastDamageSource != null
                ? m_LastDamageSource.GetComponentInParent<PlayerClassController>()
                : null;
            bool strikerKill = killer != null && killer.SelectedClass.Value == PlayerArchetype.Striker;
            float healthChance = strikerKill
                ? Mathf.Max(HealthDropChance, StrikerHealthDropChance)
                : HealthDropChance;

            if (AmmoLootPrefab != null && Random.value <= AmmoDropChance)
            {
                SpawnLootPrefab(AmmoLootPrefab, dropPosition);
            }

            if (HealthLootPrefab != null && Random.value <= healthChance)
            {
                SpawnLootPrefab(HealthLootPrefab, dropPosition + transform.right * 0.35f);
            }
        }

    }
}
