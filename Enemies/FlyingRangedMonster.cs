using System.Collections;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using ZombieTown.Multiplayer;
using ZombieTown.Progression;

namespace ZombieTown.Enemies
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class FlyingRangedMonster : NetworkBehaviour
    {
        [SerializeField] GameObject flyingVisualPrefab;
        [SerializeField] GameObject projectileVisualPrefab;
        [SerializeField] RuntimeAnimatorController placeholderAnimatorController;
        [SerializeField, Min(1f)] float damage = 10f;
        [SerializeField, Min(2f)] float preferredRange = 9f;
        [SerializeField, Min(.5f)] float attackInterval = 1.8f;
        [SerializeField, Min(1f)] float projectileSpeed = 10f;
        [SerializeField, Min(1f)] float maxHealth = 150f;
        [SerializeField, Min(.5f)] float flightSpeed = 4.4f;
        [SerializeField, Min(1f)] float orbitInterval = 1.8f;

        [Header("Sounds")]
        [SerializeField] AudioClip[] idleClips;
        [SerializeField] AudioClip[] attackClips;
        [SerializeField] AudioClip[] deathClips;
        [SerializeField, Range(0f, 1f)] float idleVolume = .55f;
        [SerializeField, Range(0f, 1f)] float attackVolume = .8f;
        [SerializeField, Range(0f, 1f)] float deathVolume = .8f;
        [SerializeField] Vector2 idleInterval = new(3f, 6.5f);
        [SerializeField, Range(0f, .2f)] float pitchVariation = .08f;
        [SerializeField, Min(1f)] float audioMinDistance = 2f;
        [SerializeField, Min(2f)] float audioMaxDistance = 30f;

        readonly NetworkVariable<bool> flying = new();
        Renderer[] baseRenderers;
        ZombieAI zombie;
        NavMeshAgent agent;
        Health health;
        GameObject flyingVisual;
        Animator flyingAnimator;
        AudioSource ambientAudio;
        AudioSource actionAudio;
        Vector3 visualRestPosition;
        bool configuredFlying;
        bool dead;
        float nextAttackAt;
        float nextTargetSearch;
        float nextOrbitMove;
        float nextIdleSoundAt;
        float orbitDirection = 1f;
        PlayerCharacterController target;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AlertHash = Animator.StringToHash("IsAlert");
        static readonly int AttackHash = Animator.StringToHash("Attack");

        public bool IsFlyingConfigured => configuredFlying || flying.Value;

        public void ConfigureFlying()
        {
            configuredFlying = true;
            InheritZombieAudio();
            DisableZombieAudio();

            Health demonHealth = GetComponent<Health>();
            if (demonHealth != null)
            {
                demonHealth.MaxHealth = maxHealth;
                demonHealth.CurrentHealth = maxHealth;
            }

            // Flying demons pressure the player through movement rather than burst damage.
            damage = Mathf.Clamp(damage, 10f, 18f);
            projectileSpeed = Mathf.Max(projectileSpeed, 14f);
            attackInterval = Mathf.Min(attackInterval, 1.65f);
            flightSpeed = Mathf.Max(flightSpeed, 6.5f);
            orbitInterval = Mathf.Min(orbitInterval, .65f);
            ApplyDemonDurability();
            if (IsSpawned && IsServer) flying.Value = true;
            ApplyFlyingVisual(true);
        }

        void ApplyDemonDurability()
        {
            if (GetComponent<NonLethalZombieHeadshot>() == null)
                gameObject.AddComponent<NonLethalZombieHeadshot>();
            foreach (Damageable damageable in GetComponentsInChildren<Damageable>(true))
                if (damageable.DamageMultiplier > 1f)
                    damageable.DamageMultiplier = Mathf.Min(damageable.DamageMultiplier, 1.35f);
        }

        void Awake()
        {
            zombie = GetComponent<ZombieAI>();
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            // This also runs on clients, where ConfigureFlying is not called directly.
            // Copy the serialized zombie voice references before network state arrives.
            InheritZombieAudio();
            baseRenderers = GetComponentsInChildren<Renderer>(true);
            ambientAudio = ConfigureAudioSource(gameObject.AddComponent<AudioSource>());
            actionAudio = ConfigureAudioSource(gameObject.AddComponent<AudioSource>());
            ScheduleNextIdleSound();
        }

        AudioSource ConfigureAudioSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = audioMinDistance;
            source.maxDistance = Mathf.Max(audioMinDistance + .1f, audioMaxDistance);
            return source;
        }

        void ScheduleNextIdleSound()
        {
            float min = Mathf.Min(idleInterval.x, idleInterval.y);
            float max = Mathf.Max(idleInterval.x, idleInterval.y);
            nextIdleSoundAt = Time.time + Random.Range(min, max);
        }

        void PlayRandomClip(AudioSource source, AudioClip[] clips, float volume)
        {
            if (source == null || clips == null || clips.Length == 0) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;
            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, volume);
        }

        void Start()
        {
            EnemyPointReward.Ensure(gameObject, 20);
            if (health != null) health.OnDie += OnDeath;
            if (configuredFlying || flying.Value) StartCoroutine(ActivateFlyingNextFrame());
        }

        public override void OnNetworkSpawn()
        {
            flying.OnValueChanged += OnFlyingChanged;
            if (IsServer && configuredFlying) flying.Value = true;
            if (flying.Value) StartCoroutine(ActivateFlyingNextFrame());
        }

        public override void OnNetworkDespawn()
        {
            flying.OnValueChanged -= OnFlyingChanged;
        }

        void OnFlyingChanged(bool previous, bool current)
        {
            if (current) StartCoroutine(ActivateFlyingNextFrame());
        }

        IEnumerator ActivateFlyingNextFrame()
        {
            yield return null;
            ApplyFlyingVisual(true);
            if (!HasSimulationAuthority()) yield break;
            if (zombie != null) zombie.enabled = false;
            if (agent != null)
            {
                agent.enabled = true;
                agent.updatePosition = true;
                agent.updateRotation = true;
                agent.speed = flightSpeed;
                agent.acceleration = 14f;
                agent.angularSpeed = 300f;
                agent.stoppingDistance = preferredRange * .5f;
            }
        }

        void Update()
        {
            if ((!flying.Value && !configuredFlying) || dead) return;

            if (!IsValidTarget(target) || Time.time >= nextTargetSearch)
            {
                nextTargetSearch = Time.time + .45f;
                target = FindClosestPlayer();
            }

            if (Time.time >= nextIdleSoundAt)
            {
                ScheduleNextIdleSound();
                if (ambientAudio != null && !ambientAudio.isPlaying)
                    PlayRandomClip(ambientAudio, idleClips, idleVolume);
            }

            if (flyingVisual != null)
            {
                float targetHoverHeight = 2.2f;
                if (target != null)
                {
                    float elevationDiff = target.transform.position.y - transform.position.y;
                    targetHoverHeight = Mathf.Clamp(elevationDiff + 2.4f, 2.2f, 12f);
                }
                visualRestPosition = Vector3.Lerp(visualRestPosition, new Vector3(0f, targetHoverHeight, 0f), Time.deltaTime * 3.5f);

                float bobPhase = transform.position.x * .17f + transform.position.z * .13f;
                float bob = Mathf.Sin(Time.time * 2.5f + bobPhase) * .28f;
                float sway = Mathf.Cos(Time.time * 1.8f + bobPhase) * 4.5f;

                Vector3 localVelocity = transform.InverseTransformDirection(agent != null ? agent.velocity : Vector3.zero);
                float bank = Mathf.Clamp(-localVelocity.x * 6f, -20f, 20f);
                float pitch = Mathf.Clamp(localVelocity.z * 3f, -10f, 15f);

                flyingVisual.transform.localPosition = visualRestPosition + Vector3.up * bob;
                flyingVisual.transform.localRotation = Quaternion.Euler(pitch, sway, bank);
            }

            if (flyingAnimator != null && agent != null && agent.enabled)
            {
                flyingAnimator.SetBool(AlertHash, true);
                flyingAnimator.SetFloat(SpeedHash, agent.velocity.magnitude, .12f, Time.deltaTime);
            }

            if (!HasSimulationAuthority() || !NetworkRoundGate.IsOpen || agent == null || !agent.enabled) return;

            if (target == null) return;

            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (distance > preferredRange + 1.5f)
            {
                agent.isStopped = false;
                agent.SetDestination(target.transform.position);
            }
            else if (Time.time >= nextOrbitMove)
            {
                // Deliberately strafe around the player. Alternating the orbit side
                // makes the demon visibly hunt instead of picking nearby random points.
                nextOrbitMove = Time.time + Random.Range(orbitInterval * .65f, orbitInterval * 1.15f);
                if (Random.value < .36f) orbitDirection *= -1f;
                Vector3 toTarget = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                Vector3 radial = toTarget.sqrMagnitude > .01f ? -toTarget.normalized : transform.forward;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial) * orbitDirection;
                Vector3 desired = target.transform.position +
                                  radial * (preferredRange * Random.Range(.58f, .82f)) +
                                  tangent * Random.Range(4.1f, 6.8f);
                if (NavMesh.SamplePosition(desired, out NavMeshHit orbitHit, preferredRange, NavMesh.AllAreas))
                {
                    agent.isStopped = false;
                    agent.SetDestination(orbitHit.position);
                }
            }

            Vector3 look = target.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > .01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look),
                    Time.deltaTime * 5f);

            Vector3 startPos = GetProjectileStart();
            Vector3 victimCenter = target.transform.position + Vector3.up * 0.9f;

            if (distance <= preferredRange + 3f && Time.time >= nextAttackAt && HasLineOfSight(startPos, victimCenter))
            {
                nextAttackAt = Time.time + attackInterval;
                StartCoroutine(FireAt(target));
            }
        }

        bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float dist = diff.magnitude;
            if (dist < 0.1f) return true;
            if (Physics.Raycast(from, diff.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null &&
                    !hit.collider.transform.IsChildOf(transform) &&
                    hit.collider.GetComponentInParent<PlayerCharacterController>() == null &&
                    hit.collider.GetComponentInParent<FlyingRangedMonster>() == null)
                {
                    return false;
                }
            }
            return true;
        }

        IEnumerator FireAt(PlayerCharacterController victim)
        {
            if (IsSpawned) PlayAttackAnimationRpc();
            else PlayAttackAnimation();
            yield return new WaitForSeconds(.35f);
            if (dead || !IsValidTarget(victim)) yield break;

            Vector3 start = GetProjectileStart();
            Vector3 victimTarget = victim.transform.position + Vector3.up * .9f;
            if (!HasLineOfSight(start, victimTarget)) yield break;

            Vector3 targetVelocity = Vector3.ProjectOnPlane(victim.CharacterVelocity, Vector3.up);
            float leadTime = Mathf.Clamp(Vector3.Distance(start, victim.transform.position) / projectileSpeed, .15f, .75f);
            Vector3 predicted = victimTarget + targetVelocity * leadTime;

            // Clip predicted to wall if wall intervenes
            if (Physics.Raycast(start, (predicted - start).normalized, out RaycastHit wallHit, Vector3.Distance(start, predicted), ~0, QueryTriggerInteraction.Ignore))
            {
                if (wallHit.collider != null &&
                    !wallHit.collider.transform.IsChildOf(transform) &&
                    wallHit.collider.GetComponentInParent<PlayerCharacterController>() == null)
                {
                    predicted = wallHit.point;
                }
            }

            float travelTime = Mathf.Clamp(Vector3.Distance(start, predicted) / projectileSpeed, .4f, 2.2f);
            if (IsSpawned) AnimateProjectileRpc(start, predicted, travelTime);
            else StartCoroutine(AnimateProjectile(start, predicted, travelTime));

            yield return ResolveProjectileHit(victim, start, predicted, travelTime);
        }

        Vector3 GetProjectileStart()
        {
            if (flyingAnimator != null && flyingAnimator.isHuman)
            {
                Transform hand = flyingAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null) return hand.position + transform.forward * .22f;
            }
            Transform source = flyingVisual != null ? flyingVisual.transform : transform;
            return source.position + Vector3.up * .55f + transform.forward * .55f;
        }

        IEnumerator ResolveProjectileHit(PlayerCharacterController victim, Vector3 start, Vector3 end, float duration)
        {
            float elapsed = 0f;
            Vector3 lastPos = start;
            while (!dead && victim != null && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                Vector3 currentPos = Vector3.Lerp(start, end, Mathf.Clamp01(elapsed / duration));

                if (Physics.Linecast(lastPos, currentPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider != null &&
                        !hit.collider.transform.IsChildOf(transform) &&
                        hit.collider.GetComponentInParent<FlyingRangedMonster>() == null)
                    {
                        if (hit.collider.GetComponentInParent<PlayerCharacterController>() != null)
                        {
                            victim.GetComponent<Health>()?.TakeDamage(damage, gameObject);
                        }
                        yield break;
                    }
                }
                lastPos = currentPos;

                if (Vector3.Distance(victim.transform.position + Vector3.up * .9f, currentPos) <= 1.4f)
                {
                    if (HasLineOfSight(currentPos, victim.transform.position + Vector3.up * .9f))
                    {
                        victim.GetComponent<Health>()?.TakeDamage(damage, gameObject);
                    }
                    yield break;
                }
                yield return null;
            }

            if (!dead && victim != null &&
                Vector3.Distance(victim.transform.position + Vector3.up * .9f, end) <= 2.2f &&
                HasLineOfSight(end, victim.transform.position + Vector3.up * .9f))
            {
                victim.GetComponent<Health>()?.TakeDamage(damage * .75f, gameObject);
            }
        }

        [Rpc(SendTo.Everyone)]
        void PlayAttackAnimationRpc() => PlayAttackAnimation();

        void PlayAttackAnimation()
        {
            if (flyingAnimator != null) flyingAnimator.SetTrigger(AttackHash);
            PlayRandomClip(actionAudio, attackClips, attackVolume);
        }

        [Rpc(SendTo.Everyone)]
        void AnimateProjectileRpc(Vector3 start, Vector3 end, float duration)
        {
            StartCoroutine(AnimateProjectile(start, end, duration));
        }

        IEnumerator AnimateProjectile(Vector3 start, Vector3 end, float duration)
        {
            Vector3 direction = (end - start).normalized;
            GameObject projectile = projectileVisualPrefab != null
                ? Instantiate(projectileVisualPrefab, start, Quaternion.FromToRotation(Vector3.right, direction))
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "Spirit Demon Projectile";
            foreach (Collider collider in projectile.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            projectile.transform.localScale *= projectileVisualPrefab != null ? 1.65f : .42f;
            Light glow = projectile.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(.32f, .55f, 1f);
            glow.intensity = 2.8f;
            glow.range = 3.6f;
            float elapsed = 0f;
            while (projectile != null && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                projectile.transform.position = Vector3.Lerp(start, end, t);
                yield return null;
            }
            if (projectile != null) Destroy(projectile);
        }

        void ApplyFlyingVisual(bool active)
        {
            if (!active) return;
            // This also runs on clients after the networked flying state arrives.
            DisableZombieAudio();
            ApplyDemonDurability();
            foreach (Renderer renderer in baseRenderers)
                if (renderer != null) renderer.enabled = false;
            if (flyingVisual == null && flyingVisualPrefab != null)
            {
                flyingVisual = Instantiate(flyingVisualPrefab, transform);
                flyingVisual.name = "Flying Spirit Demon Visual";
                flyingVisual.transform.SetLocalPositionAndRotation(new Vector3(0f, 1.55f, 0f), Quaternion.identity);
                visualRestPosition = flyingVisual.transform.localPosition;
                foreach (Collider collider in flyingVisual.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                flyingAnimator = flyingVisual.GetComponentInChildren<Animator>(true);
                if (flyingAnimator != null)
                {
                    if (placeholderAnimatorController != null)
                        flyingAnimator.runtimeAnimatorController = placeholderAnimatorController;
                    flyingAnimator.applyRootMotion = false;
                    flyingAnimator.SetBool(AlertHash, true);
                }
            }
        }

        static bool HasAny(AudioClip[] clips)
        {
            if (clips == null) return false;
            foreach (AudioClip clip in clips)
                if (clip != null) return true;
            return false;
        }

        void InheritZombieAudio()
        {
            // The demon is promoted from a zombie prefab. Reuse that prefab's authored
            // voice set when dedicated demon arrays were left empty in the inspector.
            ZombieAudio sourceAudio = GetComponent<ZombieAudio>();
            if (sourceAudio == null) return;
            if (!HasAny(idleClips)) idleClips = sourceAudio.IdleClips;
            if (!HasAny(attackClips)) attackClips = sourceAudio.AttackClips;
            if (!HasAny(deathClips)) deathClips = sourceAudio.DeathClips;
        }

        void DisableZombieAudio()
        {
            ZombieAudio sourceAudio = GetComponent<ZombieAudio>();
            if (sourceAudio != null) sourceAudio.enabled = false;
        }

        PlayerCharacterController FindClosestPlayer()
        {
            PlayerCharacterController best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (PlayerCharacterController player in FindObjectsByType<PlayerCharacterController>())
            {
                if (!IsValidTarget(player)) continue;
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = player;
            }
            return best;
        }

        static bool IsValidTarget(PlayerCharacterController player)
        {
            if (player == null || player.GetComponent<Health>()?.CurrentHealth <= 0f) return false;
            PlayerClassController playerClass = player.GetComponent<PlayerClassController>();
            if (playerClass != null && playerClass.IsDowned.Value) return false;
            return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening ||
                   playerClass != null && playerClass.IsReady.Value;
        }

        bool HasSimulationAuthority()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager == null || !manager.IsListening || manager.IsServer;
        }

        void OnDeath()
        {
            // The editor setup uses the same source prefab for normal zombies and
            // flying demons. Only an instance that was actually promoted to flying
            // may play the energy death burst.
            if (!configuredFlying && !flying.Value) return;
            if (dead) return;
            dead = true;
            StopAllCoroutines();
            if (agent != null && agent.enabled) agent.isStopped = true;
            if (!HasSimulationAuthority()) return;

            Vector3 burstPosition = flyingVisual != null
                ? flyingVisual.transform.position + Vector3.up * .35f
                : transform.position + Vector3.up * 1.4f;
            if (IsSpawned) PlayDeathBurstRpc(burstPosition);
            else PlayDeathBurst(burstPosition);
            StartCoroutine(DespawnAfterDeath());
        }

        [Rpc(SendTo.Everyone)]
        void PlayDeathBurstRpc(Vector3 position) => PlayDeathBurst(position);

        void PlayDeathBurst(Vector3 position)
        {
            if (flyingVisual != null) flyingVisual.SetActive(false);
            foreach (Renderer renderer in baseRenderers)
                if (renderer != null) renderer.enabled = false;
            PlayRandomClip(actionAudio, deathClips, deathVolume);

            GameObject burst = new("Demon Death Burst");
            burst.transform.position = position;
            Light flash = burst.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(.3f, .55f, 1f);
            flash.intensity = 5f;
            flash.range = 5f;

            for (int i = 0; i < 10; i++)
            {
                Vector3 direction = (Random.onUnitSphere + Vector3.up * .35f).normalized;
                GameObject fragment = projectileVisualPrefab != null
                    ? Instantiate(projectileVisualPrefab, position, Random.rotation, burst.transform)
                    : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (fragment.transform.parent == null) fragment.transform.SetParent(burst.transform, true);
                fragment.name = "Demon Energy Fragment";
                foreach (Collider collider in fragment.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                fragment.transform.localScale *= projectileVisualPrefab != null ? .42f : .16f;
                Rigidbody body = fragment.GetComponent<Rigidbody>() ?? fragment.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.AddForce(direction * Random.Range(4.5f, 7.5f), ForceMode.VelocityChange);
                body.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);
            }

            Destroy(burst, .7f);
        }

        IEnumerator DespawnAfterDeath()
        {
            // The monster itself is hidden immediately. Keep the network object for
            // two frames so every peer receives the cosmetic burst before despawn.
            yield return null;
            yield return null;
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned) networkObject.Despawn(true);
            else Destroy(gameObject);
        }

        public override void OnDestroy()
        {
            if (health != null) health.OnDie -= OnDeath;
            base.OnDestroy();
        }
    }
}
