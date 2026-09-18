using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public sealed class ProjectileHitRequest
    {
        public Collider Collider { get; }
        public float Damage { get; }
        public Vector3 ShotOrigin { get; }
        public Vector3 HitPoint { get; }
        public bool WasRelayed { get; set; }
        public int PenetrationIndex { get; }

        public ProjectileHitRequest(Collider collider, float damage, Vector3 shotOrigin, Vector3 hitPoint,
            int penetrationIndex = 0)
        {
            Collider = collider;
            Damage = damage;
            ShotOrigin = shotOrigin;
            HitPoint = hitPoint;
            PenetrationIndex = Mathf.Max(0, penetrationIndex);
        }
    }

    public class ProjectileStandard : ProjectileBase
    {
        [Header("General")] [Tooltip("Radius of this projectile's collision detection")]
        public float Radius = 0.01f;

        [Tooltip("Transform representing the root of the projectile (used for accurate collision detection)")]
        public Transform Root;

        [Tooltip("Transform representing the tip of the projectile (used for accurate collision detection)")]
        public Transform Tip;

        [Tooltip("LifeTime of the projectile")]
        public float MaxLifeTime = 5f;

        [Tooltip("VFX prefab to spawn upon impact")]
        public GameObject ImpactVfx;

        [Tooltip("LifeTime of the VFX before being destroyed")]
        public float ImpactVfxLifetime = 5f;

        [Tooltip("Offset along the hit normal where the VFX will be spawned")]
        public float ImpactVfxSpawnOffset = 0.1f;

        [Tooltip("Clip to play on impact")] 
        public AudioClip ImpactSfxClip;

        [Tooltip("Layers this projectile can collide with")]
        public LayerMask HittableLayers = -1;

        [Header("Movement")] [Tooltip("Speed of the projectile")]
        public float Speed = 20f;

        [Tooltip("Downward acceleration from gravity")]
        public float GravityDownAcceleration = 0f;

        [Tooltip(
            "Distance over which the projectile will correct its course to fit the intended trajectory (used to drift projectiles towards center of screen in First Person view). At values under 0, there is no correction")]
        public float TrajectoryCorrectionDistance = -1;

        [Tooltip("Determines if the projectile inherits the velocity that the weapon's muzzle had when firing")]
        public bool InheritWeaponVelocity = false;

        [Header("Damage")] [Tooltip("Damage of the projectile")]
        public float Damage = 40f;

        [Tooltip("When enabled, a zombie headshot always deals enough damage to kill, including armored variants.")]
        public bool AlwaysLethalZombieHeadshots;

        [Tooltip("Number of additional monsters this projectile may pass through after the first hit.")]
        [Min(0)] public int MaxEnemyPenetrations;

        [Tooltip("When enabled, this projectile damages heavy monsters but stops instead of passing through them.")]
        public bool StopPenetrationOnHeavyMonsters;

        [Tooltip("Health threshold used to classify an orc or other heavy monster as a penetration blocker.")]
        [Min(1f)] public float HeavyMonsterHealthThreshold = 220f;

        [Tooltip("Area of damage. Keep empty if you don<t want area damage")]
        public DamageArea AreaOfDamage;

        [Header("Debug")] [Tooltip("Color of the projectile radius debug view")]
        public Color RadiusColor = Color.cyan * 0.2f;

        ProjectileBase m_ProjectileBase;
        Vector3 m_LastRootPosition;
        Vector3 m_Velocity;
        bool m_HasTrajectoryOverride;
        float m_ShootTime;
        Vector3 m_TrajectoryCorrectionVector;
        Vector3 m_ConsumedTrajectoryCorrectionVector;
        List<Collider> m_IgnoredColliders;
        HashSet<Health> m_PenetratedEnemies;
        int m_EnemyHits;

        const QueryTriggerInteraction k_TriggerInteraction = QueryTriggerInteraction.Collide;

        void OnEnable()
        {
            m_ProjectileBase = GetComponent<ProjectileBase>();
            DebugUtility.HandleErrorIfNullGetComponent<ProjectileBase, ProjectileStandard>(m_ProjectileBase, this,
                gameObject);

            m_ProjectileBase.OnShoot += OnShoot;

            Destroy(gameObject, MaxLifeTime);
        }

        new void OnShoot()
        {
            m_ShootTime = Time.time;
            Damage *= m_ProjectileBase.RuntimeDamageMultiplier;
            m_LastRootPosition = Root.position;
            m_Velocity = transform.forward * Speed;
            m_IgnoredColliders = new List<Collider>();
            m_PenetratedEnemies = new HashSet<Health>();
            m_EnemyHits = 0;
            transform.position += m_ProjectileBase.InheritedMuzzleVelocity * Time.deltaTime;

            // Ignore colliders of owner
            Collider[] ownerColliders = m_ProjectileBase.Owner.GetComponentsInChildren<Collider>();
            m_IgnoredColliders.AddRange(ownerColliders);

            // Handle case of player shooting (make projectiles not go through walls, and remember center-of-screen trajectory)
            PlayerWeaponsManager playerWeaponsManager = m_ProjectileBase.Owner.GetComponent<PlayerWeaponsManager>();
            if (playerWeaponsManager)
            {
                m_HasTrajectoryOverride = true;

                Vector3 cameraToMuzzle = (m_ProjectileBase.InitialPosition -
                                          playerWeaponsManager.WeaponCamera.transform.position);

                m_TrajectoryCorrectionVector = Vector3.ProjectOnPlane(-cameraToMuzzle,
                    playerWeaponsManager.WeaponCamera.transform.forward);
                if (TrajectoryCorrectionDistance == 0)
                {
                    transform.position += m_TrajectoryCorrectionVector;
                    m_ConsumedTrajectoryCorrectionVector = m_TrajectoryCorrectionVector;
                }
                else if (TrajectoryCorrectionDistance < 0)
                {
                    m_HasTrajectoryOverride = false;
                }

                if (Physics.Raycast(playerWeaponsManager.WeaponCamera.transform.position, cameraToMuzzle.normalized,
                    out RaycastHit hit, cameraToMuzzle.magnitude, HittableLayers, k_TriggerInteraction))
                {
                    if (IsHitValid(hit))
                    {
                        OnHit(hit.point, hit.normal, hit.collider);
                    }
                }
            }
        }

        void Update()
        {
            // Move
            transform.position += m_Velocity * Time.deltaTime;
            if (InheritWeaponVelocity)
            {
                transform.position += m_ProjectileBase.InheritedMuzzleVelocity * Time.deltaTime;
            }

            // Drift towards trajectory override (this is so that projectiles can be centered 
            // with the camera center even though the actual weapon is offset)
            if (m_HasTrajectoryOverride && m_ConsumedTrajectoryCorrectionVector.sqrMagnitude <
                m_TrajectoryCorrectionVector.sqrMagnitude)
            {
                Vector3 correctionLeft = m_TrajectoryCorrectionVector - m_ConsumedTrajectoryCorrectionVector;
                float distanceThisFrame = (Root.position - m_LastRootPosition).magnitude;
                Vector3 correctionThisFrame =
                    (distanceThisFrame / TrajectoryCorrectionDistance) * m_TrajectoryCorrectionVector;
                correctionThisFrame = Vector3.ClampMagnitude(correctionThisFrame, correctionLeft.magnitude);
                m_ConsumedTrajectoryCorrectionVector += correctionThisFrame;

                // Detect end of correction
                if (m_ConsumedTrajectoryCorrectionVector.sqrMagnitude == m_TrajectoryCorrectionVector.sqrMagnitude)
                {
                    m_HasTrajectoryOverride = false;
                }

                transform.position += correctionThisFrame;
            }

            // Orient towards velocity
            transform.forward = m_Velocity.normalized;

            // Gravity
            if (GravityDownAcceleration > 0)
            {
                // add gravity to the projectile velocity for ballistic effect
                m_Velocity += Vector3.down * GravityDownAcceleration * Time.deltaTime;
            }

            // Hit detection
            {
                RaycastHit closestHit = new RaycastHit { distance = Mathf.Infinity };
                RaycastHit closestCriticalHit = new RaycastHit { distance = Mathf.Infinity };
                bool foundHit = false;
                bool foundCriticalHit = false;

                // Sphere cast
                Vector3 displacementSinceLastFrame = Tip.position - m_LastRootPosition;
                RaycastHit[] hits = Physics.SphereCastAll(m_LastRootPosition, Radius,
                    displacementSinceLastFrame.normalized, displacementSinceLastFrame.magnitude, HittableLayers,
                    k_TriggerInteraction);
                foreach (var hit in hits)
                {
                    if (!IsHitValid(hit))
                        continue;

                    Damageable hitDamageable = hit.collider.GetComponent<Damageable>();
                    bool isCriticalHitbox = hitDamageable != null && hitDamageable.DamageMultiplier > 1f;
                    if (isCriticalHitbox && hit.distance < closestCriticalHit.distance)
                    {
                        foundCriticalHit = true;
                        closestCriticalHit = hit;
                    }
                    else if (!isCriticalHitbox && hit.distance < closestHit.distance)
                    {
                        foundHit = true;
                        closestHit = hit;
                    }
                }

                // A body capsule often overlaps the head bone. Prefer the critical
                // hitbox when both contacts belong to the same short section of travel.
                if (foundCriticalHit && (!foundHit || closestCriticalHit.distance <= closestHit.distance + 0.45f))
                {
                    closestHit = closestCriticalHit;
                    foundHit = true;
                }

                if (foundHit)
                {
                    // Handle case of casting while already inside a collider
                    if (closestHit.distance <= 0f)
                    {
                        closestHit.point = Root.position;
                        closestHit.normal = -transform.forward;
                    }

                    OnHit(closestHit.point, closestHit.normal, closestHit.collider);
                }
            }

            m_LastRootPosition = Root.position;
        }

        bool IsHitValid(RaycastHit hit)
        {
            // Some imported buildings use a coarse collider that seals visual
            // window openings. Explicit portal volumes preserve solid walls while
            // allowing projectiles through the authored openings.
            if (ProjectilePassThroughVolume.Contains(hit.point))
            {
                return false;
            }

            // ignore hits with an ignore component
            if (hit.collider.GetComponent<IgnoreHitDetection>())
            {
                return false;
            }

            // ignore hits with triggers that don't have a Damageable component
            if (hit.collider.isTrigger && hit.collider.GetComponent<Damageable>() == null)
            {
                return false;
            }

            // ignore hits with specific ignored colliders (self colliders, by default)
            if (m_IgnoredColliders != null && m_IgnoredColliders.Contains(hit.collider))
            {
                return false;
            }

            return true;
        }

        void OnHit(Vector3 point, Vector3 normal, Collider collider)
        {
            bool continueThroughEnemy = false;
            // damage
            if (AreaOfDamage)
            {
                // area damage
                AreaOfDamage.InflictDamageInArea(Damage, point, HittableLayers, k_TriggerInteraction,
                    m_ProjectileBase.Owner);
            }
            else
            {
                // point damage
                Damageable damageable = collider.GetComponent<Damageable>();
                if (damageable)
                {
                    Health enemyHealth = damageable.Health != null
                        ? damageable.Health : collider.GetComponentInParent<Health>();
                    bool heavyMonsterBlocks = StopPenetrationOnHeavyMonsters && enemyHealth != null &&
                                              enemyHealth.MaxHealth >= HeavyMonsterHealthThreshold;
                    bool penetrableMonster = MaxEnemyPenetrations > 0 && enemyHealth != null &&
                                             enemyHealth.GetComponent("ZombieAI") != null &&
                                             !heavyMonsterBlocks &&
                                             (m_PenetratedEnemies == null || !m_PenetratedEnemies.Contains(enemyHealth));
                    int penetrationIndex = m_EnemyHits;
                    ProjectileHitRequest hitRequest = new(collider, Damage,
                        m_ProjectileBase.InitialPosition, point, penetrationIndex);
                    if (m_ProjectileBase.Owner != null)
                    {
                        m_ProjectileBase.Owner.SendMessageUpwards("RequestNetworkProjectileHit", hitRequest,
                            SendMessageOptions.DontRequireReceiver);
                    }
                    if (hitRequest.WasRelayed)
                    {
                        if (penetrableMonster)
                        {
                            m_PenetratedEnemies.Add(enemyHealth);
                            m_EnemyHits++;
                            continueThroughEnemy = m_EnemyHits <= MaxEnemyPenetrations;
                        }
                    }
                    else
                    {
                        bool isZombieHeadshot = collider.GetComponent("ZombieHeadHitbox") != null;
                        bool isGuardHeadshot = collider.GetComponent("OutpostGuardHeadHitbox") != null;
                        bool isHeadshot = isZombieHeadshot || isGuardHeadshot;
                        if (isHeadshot)
                            collider.SendMessage("SetImpact", new Ray(point, transform.forward),
                                SendMessageOptions.DontRequireReceiver);
                        collider.SendMessage("RegisterHit", Damage, SendMessageOptions.DontRequireReceiver);

                        float appliedDamage = Damage;
                        bool preventsLethalHeadshot = collider.GetComponentInParent<NonLethalZombieHeadshot>() != null;
                        if (AlwaysLethalZombieHeadshots && isZombieHeadshot && !preventsLethalHeadshot &&
                            damageable.Health != null)
                        {
                            float multiplier = Mathf.Max(.01f, damageable.DamageMultiplier);
                            appliedDamage = Mathf.Max(appliedDamage,
                                damageable.Health.CurrentHealth / multiplier + 1f);
                        }

                        damageable.InflictDamage(appliedDamage, false, m_ProjectileBase.Owner);

                        // Broadcast DamageEvent for hit markers/feedback
                        DamageEvent evt = Events.DamageEvent;
                        evt.Sender = m_ProjectileBase.Owner;
                        evt.DamageValue = appliedDamage * damageable.DamageMultiplier;
                        evt.IsHeadshot = isHeadshot;
                        evt.IsLethal = damageable.Health != null && damageable.Health.CurrentHealth <= 0f;
                        EventManager.Broadcast(evt);

                        if (MaxEnemyPenetrations > 0)
                        {
                            PenetrationHitEvent penetrationEvt = Events.PenetrationHitEvent;
                            penetrationEvt.Sender = m_ProjectileBase.Owner;
                            penetrationEvt.HitNumber = penetrationIndex + 1;
                            penetrationEvt.IsLethal = evt.IsLethal;
                            EventManager.Broadcast(penetrationEvt);
                        }

                        if (penetrableMonster)
                        {
                            m_PenetratedEnemies.Add(enemyHealth);
                            m_EnemyHits++;
                            continueThroughEnemy = m_EnemyHits <= MaxEnemyPenetrations;
                        }
                    }
                }
            }

            if (continueThroughEnemy)
            {
                Health enemyHealth = collider.GetComponentInParent<Health>();
                if (enemyHealth != null)
                foreach (Collider enemyCollider in enemyHealth.GetComponentsInChildren<Collider>(true))
                    if (!m_IgnoredColliders.Contains(enemyCollider)) m_IgnoredColliders.Add(enemyCollider);
                transform.position = point + transform.forward * Mathf.Max(.04f, Radius * 1.5f);
                m_LastRootPosition = Root.position;
                return;
            }

            // impact vfx
            if (ImpactVfx)
            {
                GameObject impactVfxInstance = Instantiate(ImpactVfx, point + (normal * ImpactVfxSpawnOffset),
                    Quaternion.LookRotation(normal));
                if (ImpactVfxLifetime > 0)
                {
                    Destroy(impactVfxInstance.gameObject, ImpactVfxLifetime);
                }
            }

            // impact sfx
            if (ImpactSfxClip)
            {
                AudioUtility.CreateSFX(ImpactSfxClip, point, AudioUtility.AudioGroups.Impact, 1f, 3f);
            }

            // Self Destruct
            Destroy(this.gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = RadiusColor;
            Gizmos.DrawSphere(transform.position, Radius);
        }
    }
}
