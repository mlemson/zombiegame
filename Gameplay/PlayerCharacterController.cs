using System;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(AudioSource))]
    public class PlayerCharacterController : MonoBehaviour
    {
        // Assigned by multiplayer code (a different assembly) to keep a single death from
        // ending the match while teammates are still alive. Return true to suppress.
        public static Func<PlayerCharacterController, bool> SuppressDeathBroadcast;
        [Header("References")] [Tooltip("Reference to the main camera used for the player")]
        public Camera PlayerCamera;

        [Tooltip("Audio source for footsteps, jump, etc...")]
        public AudioSource AudioSource;

        [Header("General")] [Tooltip("Force applied downward when in the air")]
        public float GravityDownForce = 20f;

        [Tooltip("Physic layers checked to consider the player grounded")]
        public LayerMask GroundCheckLayers = -1;

        [Tooltip("distance from the bottom of the character controller capsule to test for grounded")]
        public float GroundCheckDistance = 0.05f;

        [Header("Movement")] [Tooltip("Max movement speed when grounded (when not sprinting)")]
        public float MaxSpeedOnGround = 10f;

        [Tooltip(
            "Sharpness for the movement when grounded, a low value will make the player accelerate and decelerate slowly, a high value will do the opposite")]
        public float MovementSharpnessOnGround = 15;

        [Tooltip("Max movement speed when crouching")] [Range(0, 1)]
        public float MaxSpeedCrouchedRatio = 0.5f;

        [Tooltip("Max movement speed when not grounded")]
        public float MaxSpeedInAir = 10f;

        [Tooltip("Acceleration speed when in the air")]
        public float AccelerationSpeedInAir = 25f;

        [Tooltip("Multiplicator for the sprint speed (based on grounded speed)")]
        public float SprintSpeedModifier = 2f;

        [Header("Vehicle Collision")]
        [Tooltip("Prevents the CharacterController from transferring movement into drivable vehicles.")]
        public bool PreventPushingVehicles = true;

        [Tooltip("Small clearance kept from vehicle colliders when walking into them.")]
        [Range(0f, 0.2f)]
        public float VehicleCollisionSkin = 0.03f;

        [Tooltip("Height at which the player dies instantly when falling off the map")]
        public float KillHeight = -50f;

        [Header("Rotation")] [Tooltip("Rotation speed for moving the camera")]
        public float RotationSpeed = 200f;

        [Range(0.1f, 1f)] [Tooltip("Rotation speed multiplier when aiming")]
        public float AimingRotationMultiplier = 0.4f;

        [Header("Jump")] [Tooltip("Force applied upward when jumping")]
        public float JumpForce = 9f;

        [Header("Stance")] [Tooltip("Ratio (0-1) of the character height where the camera will be at")]
        public float CameraHeightRatio = 0.9f;

        [Tooltip("Height of character when standing")]
        public float CapsuleHeightStanding = 1.6f;

        [Tooltip("Height of character when crouching")]
        public float CapsuleHeightCrouching = 0.8f;

        [Tooltip("Speed of crouching transitions")]
        public float CrouchingSharpness = 10f;
        public CrouchProfile CrouchTuning;
        // Per-player multiplier: a shared crouch asset must not shrink a 1.2x avatar.
        public float StanceHeightScale => CrouchTuning!=null
            ? CapsuleHeightStanding/Mathf.Max(.1f,CrouchTuning.standingHeight) : 1f;
        float heightVelocity, cameraHeightVelocity;
        float StandingTarget => CrouchTuning!=null?CrouchTuning.standingHeight*StanceHeightScale:CapsuleHeightStanding;
        float CrouchingTarget => CrouchTuning!=null?CrouchTuning.crouchingHeight*StanceHeightScale:CapsuleHeightCrouching;

        [Header("Audio")] [Tooltip("Amount of footstep sounds played when moving one meter")]
        public float FootstepSfxFrequency = 1f;

        [Tooltip("Amount of footstep sounds played when moving one meter while sprinting")]
        public float FootstepSfxFrequencyWhileSprinting = 1f;

        [Tooltip("Sound played for footsteps")]
        public AudioClip FootstepSfx;

        [Tooltip("Sound played when jumping")] public AudioClip JumpSfx;
        [Tooltip("Sound played when landing")] public AudioClip LandSfx;

        [Tooltip("Sound played when taking damage froma fall")]
        public AudioClip FallDamageSfx;

        [Header("Fall Damage")]
        [Tooltip("Whether the player will recieve damage when hitting the ground at high speed")]
        public bool RecievesFallDamage;

        [Tooltip("Minimun fall speed for recieving fall damage")]
        public float MinSpeedForFallDamage = 10f;

        [Tooltip("Fall speed for recieving th emaximum amount of fall damage")]
        public float MaxSpeedForFallDamage = 30f;

        [Tooltip("Damage recieved when falling at the mimimum speed")]
        public float FallDamageAtMinSpeed = 10f;

        [Tooltip("Damage recieved when falling at the maximum speed")]
        public float FallDamageAtMaxSpeed = 50f;

        public UnityAction<bool> OnStanceChanged;

        public Vector3 CharacterVelocity { get; set; }
        public bool IsGrounded { get; private set; }
        public bool HasJumpedThisFrame { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsCrouching { get; private set; }

        public float RotationMultiplier
        {
            get
            {
                if (m_WeaponsManager.IsAiming)
                {
                    return AimingRotationMultiplier;
                }

                return 1f;
            }
        }

        Health m_Health;
        PlayerInputHandler m_InputHandler;
        CharacterController m_Controller;
        PlayerWeaponsManager m_WeaponsManager;
        Actor m_Actor;
        PlayerAudio m_PlayerAudio;
        Vector3 m_GroundNormal;
        Vector3 m_CharacterVelocity;
        Vector3 m_LatestImpactSpeed;
        float m_LastTimeJumped = 0f;
        float m_CameraVerticalAngle = 0f;
        float m_FootstepDistanceCounter;
        float m_TargetCharacterHeight;

        const float k_JumpGroundingPreventionTime = 0.2f;
        const float k_GroundCheckDistanceInAir = 0.07f;

        void Awake()
        {
            ActorsManager actorsManager = FindAnyObjectByType<ActorsManager>();
            if (actorsManager != null)
                actorsManager.SetPlayer(gameObject);

            if (GetComponent<MapEdgeWarning>() == null)
                gameObject.AddComponent<MapEdgeWarning>();
        }

        void Start()
        {
            // fetch components on the same gameObject
            m_Controller = GetComponent<CharacterController>();
            DebugUtility.HandleErrorIfNullGetComponent<CharacterController, PlayerCharacterController>(m_Controller,
                this, gameObject);

            m_InputHandler = GetComponent<PlayerInputHandler>();
            DebugUtility.HandleErrorIfNullGetComponent<PlayerInputHandler, PlayerCharacterController>(m_InputHandler,
                this, gameObject);

            m_WeaponsManager = GetComponent<PlayerWeaponsManager>();
            DebugUtility.HandleErrorIfNullGetComponent<PlayerWeaponsManager, PlayerCharacterController>(
                m_WeaponsManager, this, gameObject);

            m_Health = GetComponent<Health>();
            DebugUtility.HandleErrorIfNullGetComponent<Health, PlayerCharacterController>(m_Health, this, gameObject);

            m_Actor = GetComponent<Actor>();
            DebugUtility.HandleErrorIfNullGetComponent<Actor, PlayerCharacterController>(m_Actor, this, gameObject);

            // Optional richer audio component. Existing single AudioClip fields remain as fallback.
            m_PlayerAudio = GetComponent<PlayerAudio>();

            m_Controller.enableOverlapRecovery = true;

            m_Health.OnDie += OnDie;

            // force the crouch state to false when starting
            SetCrouchingState(false, true);
            UpdateCharacterHeight(true);
        }

        void Update()
        {
            // check for Y kill
            if (!IsDead && transform.position.y < KillHeight)
            {
                m_Health.Kill();
            }

            HasJumpedThisFrame = false;

            bool wasGrounded = IsGrounded;
            GroundCheck();

            // landing
            if (IsGrounded && !wasGrounded)
            {
                // Fall damage
                float fallSpeed = -Mathf.Min(CharacterVelocity.y, m_LatestImpactSpeed.y);
                float fallSpeedRatio = (fallSpeed - MinSpeedForFallDamage) /
                                       (MaxSpeedForFallDamage - MinSpeedForFallDamage);
                if (RecievesFallDamage && fallSpeedRatio > 0f)
                {
                    float dmgFromFall = Mathf.Lerp(FallDamageAtMinSpeed, FallDamageAtMaxSpeed, fallSpeedRatio);
                    m_Health.TakeDamage(dmgFromFall, null);

                    // Hard landing sound; PlayerAudio can also independently play a hurt/fall vocal via Health.OnDamaged.
                    if (m_PlayerAudio != null)
                        m_PlayerAudio.PlayLand(true);
                    else if (FallDamageSfx != null)
                        AudioSource.PlayOneShot(FallDamageSfx);
                }
                else
                {
                    // land SFX
                    if (m_PlayerAudio != null)
                        m_PlayerAudio.PlayLand(false);
                    else if (LandSfx != null)
                        AudioSource.PlayOneShot(LandSfx);
                }
            }

            // crouching
            if (m_InputHandler.GetCrouchInputDown())
            {
                SetCrouchingState(!IsCrouching, false);
            }

            UpdateCharacterHeight(false);

            HandleCharacterMovement();
        }

        void OnDie()
        {
            IsDead = true;

            // Tell the weapons manager to switch to a non-existing weapon in order to lower the weapon
            m_WeaponsManager.SwitchToWeaponIndex(-1, true);

            if (SuppressDeathBroadcast == null || !SuppressDeathBroadcast(this))
                EventManager.Broadcast(Events.PlayerDeathEvent);
        }

        void GroundCheck()
        {
            // Make sure that the ground check distance while already in air is very small, to prevent suddenly snapping to ground
            float chosenGroundCheckDistance =
                IsGrounded ? (m_Controller.skinWidth + GroundCheckDistance) : k_GroundCheckDistanceInAir;

            // reset values before the ground check
            IsGrounded = false;
            m_GroundNormal = Vector3.up;

            // only try to detect ground if it's been a short amount of time since last jump; otherwise we may snap to the ground instantly after we try jumping
            if (Time.time >= m_LastTimeJumped + k_JumpGroundingPreventionTime)
            {
                // if we're grounded, collect info about the ground normal with a downward capsule cast representing our character capsule
                if (Physics.CapsuleCast(GetCapsuleBottomHemisphere(), GetCapsuleTopHemisphere(m_Controller.height),
                    m_Controller.radius, Vector3.down, out RaycastHit hit, chosenGroundCheckDistance, GroundCheckLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    // storing the upward direction for the surface found
                    m_GroundNormal = hit.normal;

                    // Only consider this a valid ground hit if the ground normal goes in the same direction as the character up
                    // and if the slope angle is lower than the character controller's limit
                    if (Vector3.Dot(hit.normal, transform.up) > 0f &&
                        IsNormalUnderSlopeLimit(m_GroundNormal))
                    {
                        IsGrounded = true;

                        // handle snapping to the ground
                        if (hit.distance > m_Controller.skinWidth)
                        {
                            m_Controller.Move(Vector3.down * hit.distance);
                        }
                    }
                }
            }
        }

        void HandleCharacterMovement()
        {
            // horizontal character rotation
            {
                // rotate the transform with the input speed around its local Y axis
                transform.Rotate(
                    new Vector3(0f, (m_InputHandler.GetLookInputsHorizontal() * RotationSpeed * RotationMultiplier),
                        0f), Space.Self);
            }

            // vertical camera rotation
            {
                // add vertical inputs to the camera's vertical angle
                m_CameraVerticalAngle += m_InputHandler.GetLookInputsVertical() * RotationSpeed * RotationMultiplier;

                // limit the camera's vertical angle to min/max
                m_CameraVerticalAngle = Mathf.Clamp(m_CameraVerticalAngle, -89f, 89f);

                // apply the vertical angle as a local rotation to the camera transform along its right axis (makes it pivot up and down)
                PlayerCamera.transform.localEulerAngles = new Vector3(m_CameraVerticalAngle, 0, 0);
            }

            // character movement handling
            bool isSprinting = m_InputHandler.GetSprintInputHeld();
            {
                if (isSprinting)
                {
                    isSprinting = SetCrouchingState(false, false);
                }

                float speedModifier = isSprinting ? SprintSpeedModifier : 1f;

                // converts move input to a worldspace vector based on our character's transform orientation
                Vector3 worldspaceMoveInput = transform.TransformVector(m_InputHandler.GetMoveInput());

                // handle grounded movement
                if (IsGrounded)
                {
                    // calculate the desired velocity from inputs, max speed, and current slope
                    Vector3 targetVelocity = worldspaceMoveInput * MaxSpeedOnGround * speedModifier;
                    // reduce speed if crouching by crouch speed ratio
                    if (IsCrouching)
                        targetVelocity *= MaxSpeedCrouchedRatio;
                    targetVelocity = GetDirectionReorientedOnSlope(targetVelocity.normalized, m_GroundNormal) *
                                     targetVelocity.magnitude;

                    // smoothly interpolate between our current velocity and the target velocity based on acceleration speed
                    CharacterVelocity = Vector3.Lerp(CharacterVelocity, targetVelocity,
                        MovementSharpnessOnGround * Time.deltaTime);

                    // jumping
                    if (IsGrounded && m_InputHandler.GetJumpInputDown())
                    {
                        // force the crouch state to false
                        if (SetCrouchingState(false, false))
                        {
                            // start by canceling out the vertical component of our velocity
                            CharacterVelocity = new Vector3(CharacterVelocity.x, 0f, CharacterVelocity.z);

                            // then, add the jumpSpeed value upwards
                            CharacterVelocity += Vector3.up * JumpForce;

                            // play sound
                            if (m_PlayerAudio != null)
                                m_PlayerAudio.PlayJump();
                            else if (JumpSfx != null)
                                AudioSource.PlayOneShot(JumpSfx);

                            // remember last time we jumped because we need to prevent snapping to ground for a short time
                            m_LastTimeJumped = Time.time;
                            HasJumpedThisFrame = true;

                            // Force grounding to false
                            IsGrounded = false;
                            m_GroundNormal = Vector3.up;
                        }
                    }

                    // Footsteps are driven by intentional horizontal movement. Using
                    // the smoothed velocity alone let a queued step fire after input
                    // had stopped, and vertical correction inflated traveled distance.
                    float chosenFootstepSfxFrequency =
                        (isSprinting ? FootstepSfxFrequencyWhileSprinting : FootstepSfxFrequency);
                    float horizontalSpeed = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up).magnitude;
                    bool movingIntentionally = worldspaceMoveInput.sqrMagnitude > .01f && horizontalSpeed > .25f;
                    if (movingIntentionally)
                    {
                        m_FootstepDistanceCounter += horizontalSpeed * Time.deltaTime;
                        float stepDistance = 1f / Mathf.Max(.01f, chosenFootstepSfxFrequency);
                        if (m_FootstepDistanceCounter >= stepDistance)
                        {
                            m_FootstepDistanceCounter -= stepDistance;
                            if (m_PlayerAudio != null)
                                m_PlayerAudio.PlayFootstep(isSprinting, IsCrouching);
                            else if (FootstepSfx != null)
                                AudioSource.PlayOneShot(FootstepSfx);
                        }
                    }
                    else
                    {
                        m_FootstepDistanceCounter = 0f;
                    }
                }
                // handle air movement
                else
                {
                    // add air acceleration
                    CharacterVelocity += worldspaceMoveInput * AccelerationSpeedInAir * Time.deltaTime;

                    // limit air speed to a maximum, but only horizontally
                    float verticalVelocity = CharacterVelocity.y;
                    Vector3 horizontalVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
                    horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, MaxSpeedInAir * speedModifier);
                    CharacterVelocity = horizontalVelocity + (Vector3.up * verticalVelocity);

                    // apply the gravity to the velocity
                    CharacterVelocity += Vector3.down * GravityDownForce * Time.deltaTime;
                }
            }

            // apply the final calculated velocity value as a character movement
            Vector3 capsuleBottomBeforeMove = GetCapsuleBottomHemisphere();
            Vector3 capsuleTopBeforeMove = GetCapsuleTopHemisphere(m_Controller.height);
            Vector3 desiredDisplacement = CharacterVelocity * Time.deltaTime;
            Vector3 safeDisplacement = ClampMovementAgainstVehicles(desiredDisplacement);
            m_Controller.Move(safeDisplacement);

            // detect obstructions to adjust velocity accordingly
            m_LatestImpactSpeed = Vector3.zero;
            if (Physics.CapsuleCast(capsuleBottomBeforeMove, capsuleTopBeforeMove, m_Controller.radius,
                CharacterVelocity.normalized, out RaycastHit hit, CharacterVelocity.magnitude * Time.deltaTime, -1,
                QueryTriggerInteraction.Ignore))
            {
                // We remember the last impact speed because the fall damage logic might need it
                m_LatestImpactSpeed = CharacterVelocity;

                if (IsDrivableVehicle(hit.collider))
                {
                    // CharacterController movement is not mass-based. Without this special case,
                    // a walking player can keep resolving overlap against a multi-ton vehicle and
                    // effectively shove it around. Remove the movement into the vehicle while
                    // still allowing the player to slide along its side.
                    CharacterVelocity = Vector3.ProjectOnPlane(CharacterVelocity, hit.normal);
                }
                else
                {
                    CharacterVelocity = Vector3.ProjectOnPlane(CharacterVelocity, hit.normal);
                }
            }
        }

        bool IsDrivableVehicle(Collider collider)
        {
            if (!PreventPushingVehicles || collider == null)
                return false;

            Rigidbody attachedBody = collider.attachedRigidbody;
            Transform searchRoot = attachedBody != null
                ? attachedBody.transform
                : collider.transform;

            // Do not reference BusDriver/ForkliftDriver types directly here.
            // PlayerCharacterController lives in the FPS assembly, while the vehicle
            // scripts can live in a different assembly. Checking component type names
            // avoids creating an assembly dependency/circular reference.
            MonoBehaviour[] behaviours = searchRoot.GetComponentsInParent<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;
                if (typeName == "BusDriver" || typeName == "ForkliftDriver")
                    return true;
            }

            return false;
        }

        Vector3 ClampMovementAgainstVehicles(Vector3 desiredDisplacement)
        {
            if (!PreventPushingVehicles ||
                desiredDisplacement.sqrMagnitude < 0.000001f ||
                m_Controller == null)
            {
                return desiredDisplacement;
            }

            Vector3 direction = desiredDisplacement.normalized;
            float distance = desiredDisplacement.magnitude;

            Vector3 bottom = GetCapsuleBottomHemisphere();
            Vector3 top = GetCapsuleTopHemisphere(m_Controller.height);

            RaycastHit[] hits = Physics.CapsuleCastAll(
                bottom,
                top,
                Mathf.Max(0.01f, m_Controller.radius - m_Controller.skinWidth),
                direction,
                distance + VehicleCollisionSkin,
                -1,
                QueryTriggerInteraction.Ignore);

            float allowedDistance = distance;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider == m_Controller ||
                    hit.collider.transform.IsChildOf(transform) ||
                    !IsDrivableVehicle(hit.collider))
                {
                    continue;
                }

                float candidate = Mathf.Max(0f, hit.distance - VehicleCollisionSkin);

                if (candidate < allowedDistance)
                    allowedDistance = candidate;
            }

            return direction * allowedDistance;
        }

        // Returns true if the slope angle represented by the given normal is under the slope angle limit of the character controller
        bool IsNormalUnderSlopeLimit(Vector3 normal)
        {
            return Vector3.Angle(transform.up, normal) <= m_Controller.slopeLimit;
        }

        // Gets the center point of the bottom hemisphere of the character controller capsule    
        Vector3 GetCapsuleBottomHemisphere()
        {
            return transform.position + (transform.up * m_Controller.radius);
        }

        // Gets the center point of the top hemisphere of the character controller capsule    
        Vector3 GetCapsuleTopHemisphere(float atHeight)
        {
            return transform.position + (transform.up * (atHeight - m_Controller.radius));
        }

        // Gets a reoriented direction that is tangent to a given slope
        public Vector3 GetDirectionReorientedOnSlope(Vector3 direction, Vector3 slopeNormal)
        {
            Vector3 directionRight = Vector3.Cross(direction, transform.up);
            return Vector3.Cross(slopeNormal, directionRight).normalized;
        }

        void UpdateCharacterHeight(bool force)
        {
            m_TargetCharacterHeight=IsCrouching?CrouchingTarget:StandingTarget;
            // Re-check clearance throughout expansion (including moving ceilings).
            // A blocked height adjustment must not toggle crouch on behalf of the
            // player (for example when the shared profile is assigned after spawn).
            if(!force && m_TargetCharacterHeight>m_Controller.height+.001f && !SetCrouchingState(false,false)) return;
            float smoothTime=CrouchTuning!=null?CrouchTuning.crouchTransitionTime:.16f;
            float cameraY=CrouchTuning!=null?(IsCrouching?CrouchTuning.crouchingCameraY:CrouchTuning.standingCameraY)*StanceHeightScale:m_TargetCharacterHeight*CameraHeightRatio;
            m_Controller.height=force?m_TargetCharacterHeight:Mathf.SmoothDamp(m_Controller.height,m_TargetCharacterHeight,ref heightVelocity,smoothTime);
            m_Controller.center=Vector3.up*m_Controller.height*.5f;
            Vector3 cameraPosition=PlayerCamera.transform.localPosition;
            cameraPosition.y=force?cameraY:Mathf.SmoothDamp(cameraPosition.y,cameraY,ref cameraHeightVelocity,smoothTime);
            PlayerCamera.transform.localPosition=cameraPosition;
            if(m_Actor.AimPoint!=null) m_Actor.AimPoint.transform.localPosition=m_Controller.center;
            if(force) { heightVelocity=0; cameraHeightVelocity=0; }
        }

        // returns false if there was an obstruction
        bool SetCrouchingState(bool crouched, bool ignoreObstructions)
        {
            // set appropriate heights
            if (crouched)
            {
                m_TargetCharacterHeight = CrouchingTarget;
            }
            else
            {
                // Detect obstructions
                if (!ignoreObstructions)
                {
                    Collider[] standingOverlaps = Physics.OverlapCapsule(
                        GetCapsuleTopHemisphere(m_Controller.height),
                        GetCapsuleTopHemisphere(StandingTarget),
                        Mathf.Max(.01f, m_Controller.radius - m_Controller.skinWidth),
                        -1,
                    QueryTriggerInteraction.Ignore);
                    foreach (Collider c in standingOverlaps)
                    {
                        // Equipped weapons and character visuals are children of the
                        // player. In particular the melee sword has a MeshCollider;
                        // treating that as ceiling geometry prevented only the melee
                        // loadout from leaving the ground.
                        if (c != m_Controller && !c.transform.IsChildOf(transform))
                        {
                            return false;
                        }
                    }
                }

                m_TargetCharacterHeight = StandingTarget;
            }

            if (OnStanceChanged != null)
            {
                OnStanceChanged.Invoke(crouched);
            }

            IsCrouching = crouched;
            return true;
        }
    }
}
