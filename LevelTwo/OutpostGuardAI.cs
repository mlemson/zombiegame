using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using ZombieTown.Progression;

namespace ZombieTown.LevelTwo
{
    [RequireComponent(typeof(NetworkObject), typeof(Health), typeof(Damageable))]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class OutpostGuardAI : NetworkBehaviour, IDamageReceiverOverride
    {
        public enum GuardState : byte { Idle, Suspicious, Alert, Engaging, Dead }

        [Header("Perception")]
        [SerializeField, Min(2f)] float sightRange = 36f;
        [SerializeField, Range(10f, 180f)] float sightAngle = 125f;
        [SerializeField, Min(.1f)] float suspiciousSeconds = 1.6f;
        [SerializeField, Min(.1f)] float minimumSuspiciousSeconds = .45f;
        [SerializeField, Min(1f)] float closeAlertRange = 10.5f;
        [SerializeField, Min(1f)] float hearingRange = 15f;
        // Match CombatNoiseSystem.UnsilencedGunshotRadius so an unsilenced shot is
        // never clamped down below the range it was actually reported at.
        [SerializeField, Min(1f)] float gunshotAlertRange = 45f;
        [SerializeField, Min(.1f)] float minimumAudibleMoveSpeed = .75f;
        [SerializeField, Min(.1f)] float lostSightGrace = 8f;
        [SerializeField, Min(.1f)] float alertSeconds = 12f;
        [SerializeField] Transform eyes;

        [Header("Weapon")]
        [SerializeField] Transform muzzle;
        [SerializeField, Min(1f)] float damagePerShot = 4f;
        [SerializeField, Min(.1f)] float shotInterval = .32f;
        [SerializeField, Range(0f, 12f)] float shotSpreadDegrees = 3.75f;
        [SerializeField] AudioSource shotAudio;
        [SerializeField] Animator guardAnimator;
        [SerializeField] AudioClip shotSfx;
        [SerializeField] string shotTriggerName = "Attack";
        [SerializeField] OutpostGuardWeaponIK weaponPose;
        [SerializeField] OutpostGuardAnimationDriver animationDriver;
        [SerializeField] OutpostGuardAwarenessIndicator awarenessIndicator;

        [Header("Headshots")]
        [SerializeField, Range(1f, 4f)] float headshotDamageMultiplier = 3f;
        [SerializeField, Range(.18f, .4f)] float headHitboxRadius = .3f;

        [Header("Movement")]
        [SerializeField, Min(.5f)] float suspiciousMoveSpeed = 1.35f;
        [SerializeField, Min(.5f)] float alertMoveSpeed = 3.2f;
        [SerializeField, Min(.5f)] float suspiciousWanderRadius = 3.5f;
        [SerializeField, Min(1f)] float followStoppingDistance = 3.25f;
        [SerializeField, Min(.5f)] float deathDespawnSeconds = 3f;

        public readonly NetworkVariable<byte> State = new((byte)GuardState.Idle);
        public readonly NetworkVariable<float> SyncedHealth = new(100f);

        Health health;
        Vector3 interestPoint;
        float stateUntil;
        float nextThink;
        float nextShot;
        float suspicion;
        float suspiciousStartedAt;
        bool dying;
        bool authorityInitialized;
        GuardState offlineState;
        static Material tracerMaterial;
        NavMeshAgent navigation;
        Vector3 homePosition;
        float nextInvestigationMove;
        Vector3 previousObservedPosition;
        float observedMovementSpeed;
        bool pendingHeadshot;
        bool lethalHeadshot;
        bool suppressNextDamageAlert;
        static readonly Dictionary<GameObject, float> SilentActionUntil = new();

        public GuardState CurrentState => IsSpawned ? (GuardState)State.Value : offlineState;
        public AudioSource ShotAudio => shotAudio;
        public Animator GuardAnimator => guardAnimator;
        public OutpostGuardWeaponIK WeaponPose => weaponPose;
        public OutpostGuardAnimationDriver AnimationDriver => animationDriver;
        public OutpostGuardAwarenessIndicator AwarenessIndicator => awarenessIndicator;

        bool HasSimulationAuthority
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager == null || !manager.IsListening || IsServer;
            }
        }

        void Awake()
        {
            health = GetComponent<Health>();
            navigation = GetComponent<NavMeshAgent>();
            ApplyResponsiveGuardTuning();
            homePosition = transform.position;
            previousObservedPosition = transform.position;
            ConfigureNavigation();
            if (eyes == null) eyes = transform;
            if (muzzle == null) muzzle = eyes;
            if (guardAnimator == null) guardAnimator = GetComponentInChildren<Animator>();
            SetupHeadHitbox();
        }

        void Start()
        {
            if (!IsSpawned && HasSimulationAuthority)
                InitializeAuthority();
        }

        public void Configure(Transform eyePoint, Transform muzzlePoint, AudioSource audio,
            Animator animator = null, OutpostGuardWeaponIK pose = null,
            OutpostGuardAnimationDriver animation = null, OutpostGuardAwarenessIndicator indicator = null)
        {
            eyes = eyePoint;
            muzzle = muzzlePoint;
            shotAudio = audio;
            if (shotAudio != null)
                shotAudio.outputAudioMixerGroup = AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.WeaponShoot);
            guardAnimator = animator != null ? animator : guardAnimator != null ? guardAnimator : GetComponentInChildren<Animator>();
            weaponPose = pose != null ? pose : weaponPose;
            animationDriver = animation != null ? animation : animationDriver;
            awarenessIndicator = indicator != null ? indicator : awarenessIndicator;
        }

        public override void OnNetworkSpawn()
        {
            health.CurrentHealth = health.MaxHealth;
            SyncedHealth.OnValueChanged += OnHealthChanged;
            OnHealthChanged(SyncedHealth.Value, SyncedHealth.Value);
            if (!IsServer)
            {
                if (navigation != null && navigation.enabled) navigation.enabled = false;
                return;
            }
            InitializeAuthority();
            SyncedHealth.Value = health.CurrentHealth;
        }

        void InitializeAuthority()
        {
            if (authorityInitialized || health == null) return;
            authorityInitialized = true;
            EnemyPointReward.Ensure(gameObject, 20);
            health.CurrentHealth = health.MaxHealth;
            health.OnDamaged += OnServerDamaged;
            health.OnDie += OnServerDied;
            CombatNoiseSystem.NoiseReported += OnNoise;
        }

        public override void OnNetworkDespawn()
        {
            SyncedHealth.OnValueChanged -= OnHealthChanged;

            if (authorityInitialized && health != null)
            {
                health.OnDamaged -= OnServerDamaged;
                health.OnDie -= OnServerDied;
                CombatNoiseSystem.NoiseReported -= OnNoise;
                authorityInitialized = false;
            }

            // These guards are in-scene placed NetworkObjects. Keep the scene object
            // alive when despawning, but disable it locally on every peer.
            gameObject.SetActive(false);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (authorityInitialized && health != null)
            {
                health.OnDamaged -= OnServerDamaged;
                health.OnDie -= OnServerDied;
                CombatNoiseSystem.NoiseReported -= OnNoise;
                authorityInitialized = false;
            }
            base.OnDestroy();
        }

        void Update()
        {
            float observedSpeed = Time.deltaTime > .0001f
                ? Vector3.Distance(transform.position, previousObservedPosition) / Time.deltaTime
                : 0f;
            previousObservedPosition = transform.position;

            if (dying) return;
            if (!HasSimulationAuthority)
            {
                observedMovementSpeed = Mathf.MoveTowards(observedMovementSpeed, observedSpeed, 10f * Time.deltaTime);
                animationDriver?.SetMovement(observedMovementSpeed);
                return;
            }
            if (!ZombieTown.Multiplayer.NetworkRoundGate.IsOpen)
            {
                animationDriver?.SetMovement(0f);
                return;
            }

            float navigationSpeed = navigation != null && navigation.enabled && navigation.isOnNavMesh
                ? navigation.velocity.magnitude
                : 0f;
            observedMovementSpeed = Mathf.MoveTowards(observedMovementSpeed, navigationSpeed, 10f * Time.deltaTime);
            animationDriver?.SetMovement(observedMovementSpeed);
            if (Time.time < nextThink) return;
            nextThink = Time.time + .18f;

            ZombieTown.Multiplayer.PlayerClassController target = FindVisiblePlayer(out Vector3 visiblePoint);
            if (target != null)
            {
                interestPoint = visiblePoint;
                weaponPose?.SetAimPoint(interestPoint);
                float targetDistance = Vector3.Distance(transform.position, target.transform.position);

                if (CurrentState == GuardState.Idle)
                {
                    BeginSuspicious(.12f);
                    Face(interestPoint, 4f);
                    return;
                }

                if (CurrentState == GuardState.Suspicious)
                {
                    float distancePressure = Mathf.Lerp(1.45f, .65f,
                        Mathf.Clamp01(targetDistance / sightRange));
                    suspicion = Mathf.Clamp01(suspicion + .18f / suspiciousSeconds * distancePressure);
                    stateUntil = Time.time + lostSightGrace;
                    Investigate(interestPoint);
                    Face(interestPoint, 5f);
                    bool minimumBuildUpPassed = Time.time - suspiciousStartedAt >= minimumSuspiciousSeconds;
                    if (minimumBuildUpPassed && (targetDistance <= closeAlertRange || suspicion >= 1f))
                    {
                        SetState(GuardState.Alert);
                        stateUntil = Time.time + alertSeconds;
                    }
                    return;
                }

                if (CurrentState == GuardState.Alert)
                {
                    Follow(target.transform.position);
                    SetState(GuardState.Engaging);
                    nextShot = Mathf.Max(nextShot, Time.time + .2f);
                    Face(interestPoint, 8f);
                    return;
                }

                Follow(target.transform.position);
                Face(interestPoint, 9f);
                if (CurrentState == GuardState.Engaging && Time.time >= nextShot)
                    Fire(target.gameObject, interestPoint);
                return;
            }

            ZombieTown.Multiplayer.PlayerClassController heardPlayer = FindAudiblePlayer();
            if (heardPlayer != null)
                HearFootsteps(heardPlayer);

            if (CurrentState == GuardState.Engaging)
            {
                SetState(GuardState.Alert);
                stateUntil = Time.time + alertSeconds;
            }

            if (CurrentState == GuardState.Suspicious || CurrentState == GuardState.Alert)
            {
                if (CurrentState == GuardState.Suspicious) Investigate(interestPoint);
                else MoveTo(interestPoint, alertMoveSpeed, followStoppingDistance * .5f);
                Face(interestPoint, CurrentState == GuardState.Suspicious ? 4f : 7f);
                if (Time.time >= stateUntil)
                {
                    suspicion = 0f;
                    SetState(GuardState.Idle);
                }
            }
            else
            {
                StopMoving();
                weaponPose?.ClearAimPoint();
            }
        }

        ZombieTown.Multiplayer.PlayerClassController FindVisiblePlayer(out Vector3 visiblePoint)
        {
            ZombieTown.Multiplayer.PlayerClassController best = null;
            visiblePoint = default;
            float bestSqr = sightRange * sightRange;
            NetworkManager manager = NetworkManager.Singleton;
            bool networkActive = manager != null && manager.IsListening;
            foreach (ZombieTown.Multiplayer.PlayerClassController player in
                     FindObjectsByType<ZombieTown.Multiplayer.PlayerClassController>())
            {
                if (networkActive && (!player.IsSpawned || !player.IsReady.Value)) continue;
                if (IsAwarenessSuppressed(player.gameObject)) continue;
                Vector3 target = player.transform.position + Vector3.up * .65f;
                Vector3 delta = target - eyes.position;
                float sqr = delta.sqrMagnitude;
                Vector3 horizontalDelta = Vector3.ProjectOnPlane(delta, Vector3.up);
                Vector3 horizontalForward = Vector3.ProjectOnPlane(eyes.forward, Vector3.up);
                if (sqr > bestSqr ||
                    horizontalDelta.sqrMagnitude > .001f && horizontalForward.sqrMagnitude > .001f &&
                    Vector3.Angle(horizontalForward, horizontalDelta) > sightAngle * .5f) continue;
                if (TryGetVisibleBodyPoint(player, out Vector3 clearPoint))
                {
                    best = player;
                    bestSqr = sqr;
                    visiblePoint = clearPoint;
                }
            }
            return best;
        }

        bool TryGetVisibleBodyPoint(ZombieTown.Multiplayer.PlayerClassController player, out Vector3 point)
        {
            // Test the actual crouched body as well as chest/head height. The old
            // single 1.25m ray could pass above a crouched CharacterController.
            Vector3 origin = player.transform.position;
            Vector3 lowerBody = origin + Vector3.up * .42f;
            if (HasClearSightTo(player, lowerBody - eyes.position,
                    Vector3.Distance(eyes.position, lowerBody) + .3f))
            {
                point = lowerBody;
                return true;
            }

            Vector3 torso = origin + Vector3.up * .82f;
            if (HasClearSightTo(player, torso - eyes.position,
                    Vector3.Distance(eyes.position, torso) + .3f))
            {
                point = torso;
                return true;
            }

            Vector3 upperBody = origin + Vector3.up * 1.35f;
            if (HasClearSightTo(player, upperBody - eyes.position,
                    Vector3.Distance(eyes.position, upperBody) + .3f))
            {
                point = upperBody;
                return true;
            }

            point = default;
            return false;
        }

        bool HasClearSightTo(ZombieTown.Multiplayer.PlayerClassController player, Vector3 delta, float distance)
        {
            RaycastHit[] hits = Physics.RaycastAll(eyes.position, delta.normalized, distance,
                ~0, QueryTriggerInteraction.Ignore);
            RaycastHit nearest = default;
            bool foundBlockingHit = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.GetComponentInParent<OutpostGuardAI>() == this) continue;
                if (foundBlockingHit && hit.distance >= nearest.distance) continue;
                nearest = hit;
                foundBlockingHit = true;
            }
            return foundBlockingHit &&
                nearest.transform.GetComponentInParent<ZombieTown.Multiplayer.PlayerClassController>() == player;
        }

        ZombieTown.Multiplayer.PlayerClassController FindAudiblePlayer()
        {
            ZombieTown.Multiplayer.PlayerClassController closest = null;
            float closestSqr = hearingRange * hearingRange;
            NetworkManager manager = NetworkManager.Singleton;
            bool networkActive = manager != null && manager.IsListening;
            foreach (ZombieTown.Multiplayer.PlayerClassController player in
                     FindObjectsByType<ZombieTown.Multiplayer.PlayerClassController>())
            {
                if (networkActive && (!player.IsSpawned || !player.IsReady.Value)) continue;
                if (IsAwarenessSuppressed(player.gameObject)) continue;
                Unity.FPS.Gameplay.PlayerCharacterController movement =
                    player.GetComponent<Unity.FPS.Gameplay.PlayerCharacterController>();
                if (movement == null || movement.IsCrouching) continue;
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(movement.CharacterVelocity, Vector3.up);
                if (horizontalVelocity.sqrMagnitude < minimumAudibleMoveSpeed * minimumAudibleMoveSpeed) continue;
                float sqr = (player.transform.position - transform.position).sqrMagnitude;
                if (sqr >= closestSqr) continue;
                closest = player;
                closestSqr = sqr;
            }
            return closest;
        }

        void HearFootsteps(ZombieTown.Multiplayer.PlayerClassController player)
        {
            interestPoint = player.transform.position;
            if (CurrentState == GuardState.Idle)
                BeginSuspicious(.5f);
            else if (CurrentState == GuardState.Suspicious)
            {
                suspicion = Mathf.Max(suspicion, .5f);
                stateUntil = Time.time + lostSightGrace;
            }
            else if (CurrentState == GuardState.Alert)
                stateUntil = Time.time + alertSeconds;
        }

        void OnNoise(Vector3 position, float radius, GameObject source)
        {
            if (IsAwarenessSuppressed(source)) return;
            float effectiveRadius = Mathf.Min(radius, gunshotAlertRange);
            if (dying || Vector3.Distance(transform.position, position) > effectiveRadius) return;
            interestPoint = position;
            if (CurrentState == GuardState.Engaging) return;
            suspicion = 1f;
            SetState(GuardState.Alert);
            stateUntil = Time.time + alertSeconds;
        }

        void BeginSuspicious(float initialSuspicion)
        {
            suspicion = Mathf.Clamp01(initialSuspicion);
            suspiciousStartedAt = Time.time;
            stateUntil = Time.time + lostSightGrace;
            SetState(GuardState.Suspicious);
            nextInvestigationMove = 0f;
        }

        void Face(Vector3 position, float speed)
        {
            Vector3 direction = position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), speed * .18f);
        }

        void ConfigureNavigation()
        {
            if (navigation == null) return;
            navigation.radius = .4f;
            navigation.height = 2f;
            navigation.baseOffset = 0f;
            navigation.acceleration = 12f;
            navigation.angularSpeed = 540f;
            navigation.updateRotation = false;
            navigation.stoppingDistance = .25f;
            if (!navigation.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                navigation.Warp(hit.position);
        }

        void Investigate(Vector3 focus)
        {
            if (Time.time < nextInvestigationMove && navigation != null && navigation.hasPath) return;
            nextInvestigationMove = Time.time + Random.Range(1.2f, 2.2f);
            Vector2 random = Random.insideUnitCircle * suspiciousWanderRadius;
            Vector3 towardFocus = focus - transform.position;
            towardFocus.y = 0f;
            if (towardFocus.sqrMagnitude > 1f) towardFocus = towardFocus.normalized * 1.5f;
            MoveTo(transform.position + towardFocus + new Vector3(random.x, 0f, random.y), suspiciousMoveSpeed, .2f);
        }

        void Follow(Vector3 position) => MoveTo(position, alertMoveSpeed, followStoppingDistance);

        void MoveTo(Vector3 position, float speed, float stoppingDistance)
        {
            if (navigation == null || !navigation.enabled || !navigation.isOnNavMesh) return;
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return;
            navigation.speed = speed;
            navigation.stoppingDistance = stoppingDistance;
            navigation.isStopped = false;
            navigation.SetDestination(hit.position);
        }

        void StopMoving()
        {
            if (navigation == null || !navigation.enabled || !navigation.isOnNavMesh) return;
            navigation.isStopped = true;
            navigation.ResetPath();
            animationDriver?.SetMovement(0f);
        }

        void Fire(GameObject target, Vector3 targetPoint)
        {
            nextShot = Time.time + shotInterval * Random.Range(.85f, 1.15f);
            Vector3 origin = muzzle.position;
            CombatNoiseSystem.Report(origin, CombatNoiseSystem.UnsilencedGunshotRadius, gameObject);
            Vector3 direction = (targetPoint - origin).normalized;
            Vector2 spread = Random.insideUnitCircle * shotSpreadDegrees;
            direction = Quaternion.LookRotation(direction) * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward;
            Vector3 end = origin + direction * sightRange;

            if (animationDriver != null)
                animationDriver.PlayAttack();
            else if (guardAnimator != null && !string.IsNullOrEmpty(shotTriggerName) &&
                HasAnimatorTrigger(guardAnimator, shotTriggerName))
                guardAnimator.SetTrigger(shotTriggerName);

            if (Physics.Raycast(origin, direction, out RaycastHit hit, sightRange, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                Health targetHealth = hit.transform.GetComponentInParent<Health>();
                if (targetHealth != null && hit.transform.GetComponentInParent<ZombieTown.Multiplayer.PlayerClassController>() != null)
                    targetHealth.TakeDamage(damagePerShot, gameObject);
            }
            if (IsSpawned) PlayShotRpc(origin, end);
            else PlayShotEffects(origin, end);
        }

        [Rpc(SendTo.Everyone)]
        void PlayShotRpc(Vector3 origin, Vector3 end)
        {
            PlayShotEffects(origin, end);
        }

        void PlayShotEffects(Vector3 origin, Vector3 end)
        {
            weaponPose?.PlayShot(end);
            if (shotAudio != null)
            {
                AudioClip clip = shotSfx != null ? shotSfx : shotAudio.clip;
                if (clip != null)
                {
                    shotAudio.pitch = Random.Range(.96f, 1.04f);
                    shotAudio.PlayOneShot(clip);
                }
            }
            else if (shotSfx != null)
            {
                AudioUtility.CreateSFX(shotSfx, muzzle.position, AudioUtility.AudioGroups.WeaponShoot, 1f);
            }

            GameObject tracer = new("Guard tracer");
            LineRenderer line = tracer.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, origin);
            line.SetPosition(1, end);
            line.startWidth = .035f;
            line.endWidth = .012f;
            if (tracerMaterial == null)
                tracerMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Guard Tracer Material" };
            line.sharedMaterial = tracerMaterial;
            line.startColor = new Color(1f, .78f, .22f, 1f);
            line.endColor = new Color(1f, .25f, .05f, 0f);
            Destroy(tracer, .08f);
        }

#if UNITY_EDITOR
        public void PreviewInvestigate(Vector3 point)
        {
            interestPoint = point;
            BeginSuspicious(.5f);
            stateUntil = Time.time + 10f;
            Investigate(point);
        }

        public void PreviewAlertFollow(Vector3 point)
        {
            interestPoint = point;
            SetState(GuardState.Alert);
            stateUntil = Time.time + 10f;
            MoveTo(point, alertMoveSpeed, .25f);
        }

        public void PreviewShot(Vector3 target)
        {
            Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.25f;
            if (animationDriver != null)
                animationDriver.PlayAttack();
            else if (guardAnimator != null && !string.IsNullOrEmpty(shotTriggerName) &&
                     HasAnimatorTrigger(guardAnimator, shotTriggerName))
                guardAnimator.SetTrigger(shotTriggerName);
            PlayShotEffects(origin, target);
        }

        public void PreviewState(GuardState state)
        {
            if (state == GuardState.Suspicious)
            {
                BeginSuspicious(.5f);
                stateUntil = Time.time + 10f;
            }
            else SetState(state);
        }
#endif

        public bool TryReceiveDamage(float damage, bool isExplosionDamage, GameObject damageSource)
        {
            if (dying) return true;
            if (!IsSpawned)
            {
                health.TakeDamage(Mathf.Clamp(damage, 0f, 250f), damageSource);
                return true;
            }
            if (IsServer)
            {
                health.TakeDamage(Mathf.Clamp(damage, 0f, 250f), damageSource);
                return true;
            }

            NetworkObject sourceNetworkObject = damageSource != null ? damageSource.GetComponentInParent<NetworkObject>() : null;
            bool wasHeadshot = pendingHeadshot;
            pendingHeadshot = false;
            if (sourceNetworkObject != null && sourceNetworkObject.IsOwner)
            {
                RequestDamageRpc(Mathf.Clamp(damage, 0f, 250f), wasHeadshot);
            }
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void RequestDamageRpc(float damage, bool headshot, RpcParams rpcParams = default)
        {
            if (dying || damage <= 0f || NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client) ||
                client.PlayerObject == null) return;
            Vector3 source = client.PlayerObject.transform.position + Vector3.up * 1.25f;
            Vector3 target = transform.position + Vector3.up;
            if (Vector3.Distance(source, target) > 75f) return;
            if (Physics.Linecast(source, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
                hit.transform.GetComponentInParent<OutpostGuardAI>() != this) return;
            pendingHeadshot = headshot;
            health.TakeDamage(Mathf.Clamp(damage, 0f, 250f), client.PlayerObject.gameObject);
        }

        void OnServerDamaged(float amount, GameObject source)
        {
            if (IsSpawned) SyncedHealth.Value = health.CurrentHealth;
            bool lethal = health.CurrentHealth <= 0f;
            if (lethal) lethalHeadshot = pendingHeadshot;
            pendingHeadshot = false;
            if (!lethal) animationDriver?.PlayHit();
            bool silentDamage = suppressNextDamageAlert;
            suppressNextDamageAlert = false;
            if (source != null && !silentDamage)
            {
                interestPoint = source.transform.position;
                SetState(GuardState.Alert);
                stateUntil = Time.time + alertSeconds;
            }
        }

        void OnHealthChanged(float previous, float current)
        {
            if (!IsServer && health != null) health.CurrentHealth = current;
        }

        void OnServerDied()
        {
            if (dying) return;
            dying = true;
            if (IsSpawned) SyncedHealth.Value = 0f;
            SetState(GuardState.Dead);
            StopMoving();
            if (navigation != null && navigation.enabled) navigation.enabled = false;
            weaponPose?.SetDead();
            animationDriver?.SetDead(lethalHeadshot);
            RadioOutpostMissionController mission = FindAnyObjectByType<RadioOutpostMissionController>();
            mission?.NotifyGuardDefeated(this);
            StartCoroutine(DespawnAfterDeath());
        }

        IEnumerator DespawnAfterDeath()
        {
            yield return new WaitForSeconds(deathDespawnSeconds);

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                // In-scene placed NetworkObjects should be despawned without destroying
                // their scene instance. OnNetworkDespawn disables the GameObject.
                NetworkObject.Despawn(false);
            }
            else
            {
                // Offline play: there is no active Netcode spawn to preserve.
                Destroy(gameObject);
            }
        }

        void SetState(GuardState state)
        {
            if (IsSpawned)
            {
                if (State.Value != (byte)state) State.Value = (byte)state;
            }
            else
            {
                offlineState = state;
            }
        }

        static bool HasAnimatorTrigger(Animator animator, string parameterName)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.name == parameterName && parameter.type == AnimatorControllerParameterType.Trigger)
                    return true;
            return false;
        }

        public void PrepareHeadshot() => pendingHeadshot = !dying;

        public bool CanBeTakenDown => !dying && CurrentState != GuardState.Dead;

        public bool TryTakedown(GameObject source)
        {
            if (!HasSimulationAuthority || !CanBeTakenDown || source == null ||
                Vector3.Distance(source.transform.position, transform.position) > 2.8f) return false;
            SuppressAwarenessOf(source);
            suppressNextDamageAlert = true;
            health.TakeDamage(Mathf.Max(health.CurrentHealth, health.MaxHealth) + 1f, source);
            return true;
        }

        public void PrepareSilentMeleeDamage(GameObject source = null)
        {
            SuppressAwarenessOf(source);
            if (!dying) suppressNextDamageAlert = true;
        }

        static void SuppressAwarenessOf(GameObject source)
        {
            if (source == null) return;
            SilentActionUntil[source] = Mathf.Max(
                SilentActionUntil.TryGetValue(source, out float current) ? current : 0f,
                Time.time + 1.5f);
        }

        static bool IsAwarenessSuppressed(GameObject source)
        {
            if (source == null) return false;
            if (!SilentActionUntil.TryGetValue(source, out float until)) return false;
            if (Time.time <= until) return true;
            SilentActionUntil.Remove(source);
            return false;
        }

        void ApplyResponsiveGuardTuning()
        {
            // Existing scene instances can retain older serialized defaults. These bounds
            // migrate them while preserving any manually authored, more responsive values.
            sightRange = Mathf.Max(sightRange, 36f);
            sightAngle = Mathf.Max(sightAngle, 125f);
            suspiciousSeconds = Mathf.Min(suspiciousSeconds, 1.6f);
            minimumSuspiciousSeconds = Mathf.Min(minimumSuspiciousSeconds, .45f);
            closeAlertRange = Mathf.Max(closeAlertRange, 10.5f);
            hearingRange = Mathf.Max(hearingRange, 15f);
            gunshotAlertRange = Mathf.Max(gunshotAlertRange,
                CombatNoiseSystem.UnsilencedGunshotRadius);
            minimumAudibleMoveSpeed = Mathf.Min(minimumAudibleMoveSpeed, .75f);
            lostSightGrace = Mathf.Max(lostSightGrace, 8f);
            alertSeconds = Mathf.Max(alertSeconds, 12f);
            followStoppingDistance = Mathf.Min(followStoppingDistance, 3.25f);
            headshotDamageMultiplier = Mathf.Max(headshotDamageMultiplier, 3f);
            headHitboxRadius = Mathf.Max(headHitboxRadius, .3f);
            damagePerShot = Mathf.Min(damagePerShot, 4f);
            shotInterval = Mathf.Min(shotInterval, .32f);
            shotSpreadDegrees = Mathf.Max(shotSpreadDegrees, 3.75f);
        }

        void SetupHeadHitbox()
        {
            if (guardAnimator == null || !guardAnimator.isHuman) return;
            Transform head = guardAnimator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            OutpostGuardHeadHitbox hitbox = head.GetComponent<OutpostGuardHeadHitbox>() ??
                                             head.gameObject.AddComponent<OutpostGuardHeadHitbox>();
            hitbox.Initialize(this, headshotDamageMultiplier, headHitboxRadius);
        }
    }
}
