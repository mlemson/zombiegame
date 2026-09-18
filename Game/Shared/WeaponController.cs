using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Game
{
    public interface IMeleeAttackHandler
    {
        bool TryMeleeAttack(float baseDamage);
    }

    public interface IContinuousMeleeAttackHandler
    {
        bool TryContinuousMeleeAttack(float damagePerTick, float range);
    }

    public interface IWeaponNoiseReporter
    {
        void ReportWeaponNoise(bool silenced, Vector3 position);
    }

    public enum WeaponShootType
    {
        Manual,
        Automatic,
        Charge,
    }

    [Serializable]
    public struct CrosshairData
    {
        [Tooltip("The image that will be used for this weapon's crosshair")]
        public Sprite CrosshairSprite;

        [Tooltip("The size of the crosshair image")]
        public int CrosshairSize;

        [Tooltip("The color of the crosshair image")]
        public Color CrosshairColor;
    }

    [RequireComponent(typeof(AudioSource))]
    public class WeaponController : MonoBehaviour
    {
        [Header("Information")] [Tooltip("The name that will be displayed in the UI for this weapon")]
        public string WeaponName;

        [Tooltip("The image that will be displayed in the UI for this weapon")]
        public Sprite WeaponIcon;

        [Tooltip("Default data for the crosshair")]
        public CrosshairData CrosshairDataDefault;

        [Tooltip("Data for the crosshair when targeting an enemy")]
        public CrosshairData CrosshairDataTargetInSight;

        [Header("Internal References")]
        [Tooltip("The root object for the weapon, this is what will be deactivated when the weapon isn't active")]
        public GameObject WeaponRoot;

        [Tooltip("Tip of the weapon, where the projectiles are shot")]
        public Transform WeaponMuzzle;

        [Header("Shoot Parameters")] [Tooltip("The type of weapon wil affect how it shoots")]
        public WeaponShootType ShootType;

        [Tooltip("The projectile prefab")] public ProjectileBase ProjectilePrefab;

        [Tooltip("Minimum duration between two shots")]
        public float DelayBetweenShots = 0.5f;

        [Tooltip("Angle for the cone in which the bullets will be shot randomly (0 means no spread at all)")]
        public float BulletSpreadAngle = 0f;

        [Tooltip("Amount of bullets per shot")]
        public int BulletsPerShot = 1;

        [Tooltip("Force that will push back the weapon after each shot")] [Range(0f, 3f)]
        public float RecoilForce = 1;

        [Tooltip("Ratio of the default FOV that this weapon applies while aiming")] [Range(0f, 1f)]
        public float AimZoomRatio = 1f;

        [Tooltip("Translation to apply to weapon arm when aiming with this weapon")]
        public Vector3 AimOffset;

        [Header("First Person Presentation")]
        [Tooltip("Hide the generic player arms because this weapon model already contains its own authored hands.")]
        public bool UsesIntegratedFirstPersonHands;

        [Header("Melee")]
        [Tooltip("Routes primary fire to the owner's melee attack handler instead of spawning projectiles.")]
        public bool IsMeleeWeapon;

        [Min(1f)] public float MeleeDamage = 32f;

        [Header("Ammo Parameters")]
        [Tooltip("Should the player manually reload")]
        public bool AutomaticReload = true;
        [Tooltip("Has physical clip on the weapon and ammo shells are ejected when firing")]
        public bool HasPhysicalBullets = false;
        [Tooltip("Number of bullets in a clip")]
        public int ClipSize = 30;
        [Tooltip("Bullet Shell Casing")]
        public GameObject ShellCasing;
        [Tooltip("Weapon Ejection Port for physical ammo")]
        public Transform EjectionPort;
        [Tooltip("Force applied on the shell")]
        [Range(0.0f, 5.0f)] public float ShellCasingEjectionForce = 2.0f;
        [Tooltip("Maximum number of shell that can be spawned before reuse")]
        [Range(1, 30)] public int ShellPoolSize = 1;
        [Tooltip("Amount of ammo reloaded per second")]
        public float AmmoReloadRate = 1f;

        [Tooltip("Delay after the last shot before starting to reload")]
        public float AmmoReloadDelay = 2f;

        [Tooltip("Time before a manual reload moves ammunition into the magazine")]
        public float ReloadDuration = 1.15f;

        [Tooltip("Maximum amount of ammo in the gun")]
        public int MaxAmmo = 8;

        [Tooltip("Extra magazine/reserve capacity applied to every newly acquired weapon and later ammo refills.")]
        [Range(1f, 2f)] public float AmmoCapacityMultiplier = 1.25f;

        public int AmmoCapacity => Mathf.Max(1,
            Mathf.CeilToInt(Mathf.Max(1, MaxAmmo) * Mathf.Max(1f, AmmoCapacityMultiplier)));

        [Header("Charging parameters (charging weapons only)")]
        [Tooltip("Trigger a shot when maximum charge is reached")]
        public bool AutomaticReleaseOnCharged;

        [Tooltip("Duration to reach maximum charge")]
        public float MaxChargeDuration = 2f;

        [Tooltip("Initial ammo used when starting to charge")]
        public float AmmoUsedOnStartCharge = 1f;

        [Tooltip("Additional ammo used when charge reaches its maximum")]
        public float AmmoUsageRateWhileCharging = 1f;

        [Header("Audio & Visual")] 
        [Tooltip("Optional weapon animator for OnShoot animations")]
        public Animator WeaponAnimator;

        [Tooltip("Prefab of the muzzle flash")]
        public GameObject MuzzleFlashPrefab;

        [Tooltip("Unparent the muzzle flash instance on spawn")]
        public bool UnparentMuzzleFlash;

        [Tooltip("sound played when shooting")]
        public AudioClip ShootSfx;

        [Tooltip("Silenced shots do not alert sound-sensitive enemies.")]
        public bool IsSilenced;

        [Tooltip("Sound played when changing to this weapon")]
        public AudioClip ChangeWeaponSfx;

        [Tooltip("Continuous Shooting Sound")] public bool UseContinuousShootSound = false;
        public AudioClip ContinuousShootStartSfx;
        public AudioClip ContinuousShootLoopSfx;
        public AudioClip ContinuousShootEndSfx;
        AudioSource m_ContinuousShootAudioSource = null;
        bool m_WantsToShoot = false;

        public UnityAction OnShoot;
        public event Action OnShootProcessed;
        public event Action ReloadStarted;
        public event Action ReloadFinished;

        int m_CarriedPhysicalBullets;
        float m_CurrentAmmo;
        float m_LastTimeShot = Mathf.NegativeInfinity;
        public float LastChargeTriggerTimestamp { get; private set; }
        Vector3 m_LastMuzzlePosition;

        public GameObject Owner { get; set; }
        public GameObject SourcePrefab { get; set; }
        public float RuntimeDamageMultiplier { get; set; } = 1f;
        public bool IsCharging { get; private set; }
        public float CurrentAmmoRatio { get; private set; }
        public bool IsWeaponActive { get; private set; }
        public bool IsCooling { get; private set; }
        public float CurrentCharge { get; private set; }
        public Vector3 MuzzleWorldVelocity { get; private set; }

        public float GetAmmoNeededToShoot() =>
            (ShootType != WeaponShootType.Charge ? 1f : Mathf.Max(1f, AmmoUsedOnStartCharge)) /
            (HasPhysicalBullets ? Mathf.Max(1, ClipSize) : AmmoCapacity);

        public int GetCarriedPhysicalBullets() => m_CarriedPhysicalBullets;
        public int GetCurrentAmmo() => Mathf.FloorToInt(m_CurrentAmmo);

        AudioSource m_ShootAudioSource;

        public bool IsReloading { get; private set; }

        const string k_AnimAttackParameter = "Attack";
        const string k_AnimReloadParameter = "Reload";

        private Queue<Rigidbody> m_PhysicalAmmoPool;
        Coroutine m_ReloadCoroutine;
        bool m_UsesAuthoredReloadAnimation;
        Vector3 m_ReloadBasePosition;
        Quaternion m_ReloadBaseRotation;
        bool m_HasReloadPose;

        void Awake()
        {
            m_CurrentAmmo = HasPhysicalBullets ? Mathf.Min(ClipSize, AmmoCapacity) : AmmoCapacity;
            m_CarriedPhysicalBullets = HasPhysicalBullets
                ? Mathf.Max(0, AmmoCapacity - Mathf.FloorToInt(m_CurrentAmmo))
                : 0;
            m_LastMuzzlePosition = WeaponMuzzle.position;

            m_ShootAudioSource = GetComponent<AudioSource>();
            DebugUtility.HandleErrorIfNullGetComponent<AudioSource, WeaponController>(m_ShootAudioSource, this,
                gameObject);

            if (UseContinuousShootSound)
            {
                m_ContinuousShootAudioSource = gameObject.AddComponent<AudioSource>();
                m_ContinuousShootAudioSource.playOnAwake = false;
                m_ContinuousShootAudioSource.clip = ContinuousShootLoopSfx;
                m_ContinuousShootAudioSource.outputAudioMixerGroup =
                    AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.WeaponShoot);
                m_ContinuousShootAudioSource.loop = true;
            }

            if (HasPhysicalBullets && ShellCasing != null && EjectionPort != null)
            {
                m_PhysicalAmmoPool = new Queue<Rigidbody>(ShellPoolSize);

                for (int i = 0; i < ShellPoolSize; i++)
                {
                    GameObject shell = Instantiate(ShellCasing, transform);
                    shell.SetActive(false);
                    m_PhysicalAmmoPool.Enqueue(shell.GetComponent<Rigidbody>());
                }
            }
        }

        public void AddCarriablePhysicalBullets(int count)
        {
            if (count <= 0)
                return;

            if (HasPhysicalBullets)
            {
                m_CarriedPhysicalBullets = Mathf.Min(AmmoCapacity, m_CarriedPhysicalBullets + count);
            }
            else
            {
                m_CurrentAmmo = Mathf.Min(AmmoCapacity, m_CurrentAmmo + count);
            }
        }

        public void RefillAfterUpgrade()
        {
            if (m_ReloadCoroutine != null) StopCoroutine(m_ReloadCoroutine);
            m_ReloadCoroutine = null;
            bool wasReloading = IsReloading;
            IsReloading = false;
            RestoreReloadPose();
            m_CurrentAmmo = HasPhysicalBullets ? Mathf.Max(1, ClipSize) : AmmoCapacity;
            m_CarriedPhysicalBullets = HasPhysicalBullets ? AmmoCapacity : 0;
            if (wasReloading) ReloadFinished?.Invoke();
        }

        void ShootShell()
        {
            if (m_PhysicalAmmoPool == null || m_PhysicalAmmoPool.Count == 0)
            {
                Debug.LogWarning($"WeaponController '{name}' cannot shoot shells because the physical ammo pool is empty or not initialized.", this);
                return;
            }

            Rigidbody nextShell = m_PhysicalAmmoPool.Dequeue();
            if (nextShell == null || EjectionPort == null)
            {
                Debug.LogWarning($"WeaponController '{name}' cannot shoot shells because the shell instance or EjectionPort is invalid.", this);
                return;
            }

            nextShell.transform.position = EjectionPort.transform.position;
            nextShell.transform.rotation = EjectionPort.transform.rotation;
            nextShell.gameObject.SetActive(true);
            nextShell.transform.SetParent(null);
            nextShell.collisionDetectionMode = CollisionDetectionMode.Continuous;
            nextShell.AddForce(nextShell.transform.up * ShellCasingEjectionForce, ForceMode.Impulse);

            m_PhysicalAmmoPool.Enqueue(nextShell);
        }

        void PlaySFX(AudioClip sfx) => AudioUtility.CreateSFX(sfx, transform.position, AudioUtility.AudioGroups.WeaponShoot, 0.0f);

        void PlayShootAudioOneShot(AudioClip clip)
        {
            if (clip == null || m_ShootAudioSource == null ||
                !m_ShootAudioSource.enabled || !m_ShootAudioSource.gameObject.activeInHierarchy)
                return;

            m_ShootAudioSource.PlayOneShot(clip);
        }


        public void FinishReload()
        {
            if (!IsReloading)
                return;

            int missingBullets = Mathf.Max(0, ClipSize - Mathf.FloorToInt(m_CurrentAmmo));
            int bulletsToLoad = Mathf.Min(missingBullets, m_CarriedPhysicalBullets);
            if (bulletsToLoad > 0)
            {
                m_CurrentAmmo += bulletsToLoad;
                m_CarriedPhysicalBullets -= bulletsToLoad;
            }

            IsReloading = false;
            m_ReloadCoroutine = null;
            RestoreReloadPose();
            ReloadFinished?.Invoke();
        }

        public void StartReloadAnimation()
        {
            if (IsReloading || !HasPhysicalBullets || m_CurrentAmmo >= ClipSize || m_CarriedPhysicalBullets <= 0)
                return;

            IsReloading = true;
            Animator animator = WeaponAnimator ? WeaponAnimator : GetComponent<Animator>();
            m_UsesAuthoredReloadAnimation = animator != null && HasAnimatorParameter(animator, k_AnimReloadParameter);
            if (m_UsesAuthoredReloadAnimation)
            {
                animator.SetTrigger(k_AnimReloadParameter);
            }

            if (WeaponRoot != null)
            {
                m_ReloadBasePosition = WeaponRoot.transform.localPosition;
                m_ReloadBaseRotation = WeaponRoot.transform.localRotation;
                m_HasReloadPose = true;
            }

            ReloadStarted?.Invoke();
            m_ReloadCoroutine = StartCoroutine(ReloadAfterDelay());
        }

        System.Collections.IEnumerator ReloadAfterDelay()
        {
            float duration = Mathf.Max(0.05f, ReloadDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (!m_UsesAuthoredReloadAnimation && m_HasReloadPose && WeaponRoot != null)
                {
                    float normalized = Mathf.Clamp01(elapsed / duration);
                    float lower = Mathf.Sin(normalized * Mathf.PI);
                    float magazineBeat = Mathf.Sin(Mathf.Clamp01((normalized - .18f) / .64f) * Mathf.PI);
                    WeaponRoot.transform.localPosition = m_ReloadBasePosition + new Vector3(-.035f, -.13f * lower, -.045f * lower);
                    WeaponRoot.transform.localRotation = m_ReloadBaseRotation * Quaternion.Euler(28f * lower, -12f * magazineBeat, -32f * lower);
                }
                yield return null;
            }
            FinishReload();
        }

        void RestoreReloadPose()
        {
            if (!m_HasReloadPose || WeaponRoot == null) return;
            WeaponRoot.transform.localPosition = m_ReloadBasePosition;
            WeaponRoot.transform.localRotation = m_ReloadBaseRotation;
            m_HasReloadPose = false;
        }

        static bool HasAnimatorParameter(Animator animator, string parameterName)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name == parameterName)
                    return true;
            }

            return false;
        }

        void Update()
        {
            UpdateAmmo();
            UpdateCharge();
            UpdateContinuousShootSound();

            if (Time.deltaTime > 0)
            {
                MuzzleWorldVelocity = (WeaponMuzzle.position - m_LastMuzzlePosition) / Time.deltaTime;
                m_LastMuzzlePosition = WeaponMuzzle.position;
            }
        }

        void UpdateAmmo()
        {
            if (AutomaticReload && m_LastTimeShot + AmmoReloadDelay < Time.time && m_CurrentAmmo < AmmoCapacity && !IsCharging)
            {
                // reloads weapon over time
                m_CurrentAmmo += AmmoReloadRate * Time.deltaTime;

                // limits ammo to max value
                m_CurrentAmmo = Mathf.Clamp(m_CurrentAmmo, 0, AmmoCapacity);

                IsCooling = true;
            }
            else
            {
                IsCooling = false;
            }

            if (MaxAmmo == Mathf.Infinity)
            {
                CurrentAmmoRatio = 1f;
            }
            else
            {
                CurrentAmmoRatio = m_CurrentAmmo /
                    (HasPhysicalBullets ? Mathf.Max(1, ClipSize) : AmmoCapacity);
            }
        }

        void UpdateCharge()
        {
            if (IsCharging)
            {
                if (CurrentCharge < 1f)
                {
                    float chargeLeft = 1f - CurrentCharge;

                    // Calculate how much charge ratio to add this frame
                    float chargeAdded = 0f;
                    if (MaxChargeDuration <= 0f)
                    {
                        chargeAdded = chargeLeft;
                    }
                    else
                    {
                        chargeAdded = (1f / MaxChargeDuration) * Time.deltaTime;
                    }

                    chargeAdded = Mathf.Clamp(chargeAdded, 0f, chargeLeft);

                    // See if we can actually add this charge
                    float ammoThisChargeWouldRequire = chargeAdded * AmmoUsageRateWhileCharging;
                    if (ammoThisChargeWouldRequire <= m_CurrentAmmo)
                    {
                        // Use ammo based on charge added
                        UseAmmo(ammoThisChargeWouldRequire);

                        // set current charge ratio
                        CurrentCharge = Mathf.Clamp01(CurrentCharge + chargeAdded);
                    }
                }
            }
        }

        void UpdateContinuousShootSound()
        {
            if (UseContinuousShootSound)
            {
                if (m_WantsToShoot && m_CurrentAmmo >= 1f)
                {
                    if (!m_ContinuousShootAudioSource.isPlaying)
                    {
                        PlayShootAudioOneShot(ShootSfx);
                        PlayShootAudioOneShot(ContinuousShootStartSfx);
                        m_ContinuousShootAudioSource.Play();
                    }
                }
                else if (m_ContinuousShootAudioSource.isPlaying)
                {
                    PlayShootAudioOneShot(ContinuousShootEndSfx);
                    m_ContinuousShootAudioSource.Stop();
                }
            }
        }

        public void ShowWeapon(bool show)
        {
            if (WeaponRoot != null)
                WeaponRoot.SetActive(show);

            if (show && ChangeWeaponSfx)
            {
                PlayShootAudioOneShot(ChangeWeaponSfx);
            }

            IsWeaponActive = show;
        }

        public void CancelPendingShot()
        {
            m_WantsToShoot = false;
            IsCharging = false;
            CurrentCharge = 0f;
        }

        public void UseAmmo(float amount)
        {
            m_CurrentAmmo = Mathf.Clamp(m_CurrentAmmo - amount, 0f, AmmoCapacity);
            m_LastTimeShot = Time.time;
        }

        public bool HandleShootInputs(bool inputDown, bool inputHeld, bool inputUp)
        {
            m_WantsToShoot = inputDown || inputHeld;
            switch (ShootType)
            {
                case WeaponShootType.Manual:
                    if (inputDown)
                    {
                        return TryShoot();
                    }

                    return false;

                case WeaponShootType.Automatic:
                    if (inputHeld)
                    {
                        return TryShoot();
                    }

                    return false;

                case WeaponShootType.Charge:
                    if (inputHeld)
                    {
                        TryBeginCharge();
                    }

                    // Check if we released charge or if the weapon shoot autmatically when it's fully charged
                    if (inputUp || (AutomaticReleaseOnCharged && CurrentCharge >= 1f))
                    {
                        return TryReleaseCharge();
                    }

                    return false;

                default:
                    return false;
            }
        }

        bool TryShoot()
        {
            if (m_CurrentAmmo >= 1f
                && m_LastTimeShot + DelayBetweenShots < Time.time)
            {
                HandleShoot();
                m_CurrentAmmo -= 1f;

                return true;
            }

            return false;
        }

        bool TryBeginCharge()
        {
            if (!IsCharging
                && m_CurrentAmmo >= AmmoUsedOnStartCharge
                && Mathf.FloorToInt((m_CurrentAmmo - AmmoUsedOnStartCharge) * BulletsPerShot) > 0
                && m_LastTimeShot + DelayBetweenShots < Time.time)
            {
                UseAmmo(AmmoUsedOnStartCharge);

                LastChargeTriggerTimestamp = Time.time;
                IsCharging = true;

                return true;
            }

            return false;
        }

        bool TryReleaseCharge()
        {
            if (IsCharging)
            {
                HandleShoot();

                CurrentCharge = 0f;
                IsCharging = false;

                return true;
            }

            return false;
        }

        void HandleShoot()
        {
            int bulletsPerShotFinal = ShootType == WeaponShootType.Charge
                ? Mathf.CeilToInt(CurrentCharge * BulletsPerShot)
                : BulletsPerShot;

            // spawn all bullets with random direction
            for (int i = 0; i < bulletsPerShotFinal; i++)
            {
                Vector3 shotDirection = GetShotDirectionWithinSpread(WeaponMuzzle);
                ProjectileBase newProjectile = Instantiate(ProjectilePrefab, WeaponMuzzle.position,
                    Quaternion.LookRotation(shotDirection));
                newProjectile.Shoot(this);
            }

            // muzzle flash
            if (MuzzleFlashPrefab != null)
            {
                GameObject muzzleFlashInstance = Instantiate(MuzzleFlashPrefab, WeaponMuzzle.position,
                    WeaponMuzzle.rotation, WeaponMuzzle.transform);
                // Unparent the muzzleFlashInstance
                if (UnparentMuzzleFlash)
                {
                    muzzleFlashInstance.transform.SetParent(null);
                }

                Destroy(muzzleFlashInstance, 2f);
            }

            if (m_PhysicalAmmoPool != null && m_PhysicalAmmoPool.Count > 0)
            {
                ShootShell();
            }

            m_LastTimeShot = Time.time;

            // play shoot SFX
            if (ShootSfx && !UseContinuousShootSound)
            {
                PlayShootAudioOneShot(ShootSfx);
            }

            // Trigger attack animation if there is any
            if (WeaponAnimator)
            {
                WeaponAnimator.SetTrigger(k_AnimAttackParameter);
            }

            OnShoot?.Invoke();
            OnShootProcessed?.Invoke();

            if (Owner != null)
                Owner.GetComponent<IWeaponNoiseReporter>()?.ReportWeaponNoise(IsSilenced, WeaponMuzzle.position);
        }

        public Vector3 GetShotDirectionWithinSpread(Transform shootTransform)
        {
            float spreadAngleRatio = BulletSpreadAngle / 180f;
            Vector3 spreadWorldDirection = Vector3.Slerp(shootTransform.forward, UnityEngine.Random.insideUnitSphere,
                spreadAngleRatio);

            return spreadWorldDirection;
        }
    }
}
