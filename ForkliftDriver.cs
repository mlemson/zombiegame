using System.Collections.Generic;
using FreeForkLift;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ZombieTown.Multiplayer
{
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(100)]
    public sealed class ForkliftDriver : NetworkBehaviour
    {
        const ulong NoDriver = ulong.MaxValue;
        [SerializeField] NewCarUserControl driveControl;
        [SerializeField] NewCarController vehicleController;
        [SerializeField] ForkController forkControl;
        [SerializeField] Transform seat;
        [SerializeField] Transform exitPoint;
        [SerializeField] float interactionDistance = 3.2f;
        [SerializeField] float minimumImpactSpeed = 3.5f;
        [SerializeField] float impactDamagePerMetre = 14f;
        [Header("Zombie Vehicle Physics")]
        [Tooltip("Prevents NavMesh-driven zombies from transferring physics forces into the forklift. ZombieAI handles one-way solid vehicle geometry instead.")]
        [SerializeField] bool isolateZombiePhysics = true;
        [SerializeField, Min(0.05f)] float zombiePreContactPadding = 0.50f;
        [SerializeField, Min(0.02f)] float zombieTouchDistance = 0.24f;
        [SerializeField, Min(0.05f)] float zombieIgnoreReleaseDelay = 0.40f;
        [SerializeField, Min(0f)] float zombieYieldSpeedThreshold = 0.55f;

        [Header("Low-speed stability")]
        [SerializeField] bool resistExternalPushesWhenParked = true;
        [SerializeField, Min(0f)] float parkedSpeedThreshold = 0.8f;
        [SerializeField, Range(0f, 1f)] float parkedHorizontalVelocityRetention = 0.35f;
        [SerializeField, Range(0f, 1f)] float parkedTiltVelocityRetention = 0.35f;

        [Header("Stability")]
        [SerializeField, Min(0f)] float uprightStrength = 18f;
        [SerializeField, Min(0f)] float uprightDamping = 6f;
        [SerializeField] Vector3 stableCenterOfMass = new(0f, -.85f, 0f);

        [Header("Driver Camera")]
        [SerializeField, Min(0.001f)] float mouseLookSensitivity = 0.12f;
        [SerializeField, Min(1f)] float gamepadLookSpeed = 120f;
        [SerializeField, Range(30f, 180f)] float maxLookLeftRight = 100f;
        [SerializeField, Range(10f, 89f)] float maxLookUp = 60f;
        [SerializeField, Range(10f, 89f)] float maxLookDown = 45f;

        PlayerCharacterController driver;
        NetworkPlayerOwnership driverOwnership;
        PlayerInputHandler driverInput;
        CharacterController driverCapsule;
        PlayerWeaponsManager driverWeapons;
        WeaponController hiddenWeapon;
        Canvas promptCanvas;
        Text promptText;
        Rigidbody body;
        VehicleAudio vehicleAudio;
        readonly Dictionary<Health, float> impactCooldowns = new();
        readonly Dictionary<Collider, float> ignoredZombieColliders = new();
        readonly List<Collider> ignoredZombieCleanup = new();
        readonly HashSet<ZombieAI> processedZombies = new();
        readonly List<Collider> zombieColliderScratch = new();
        readonly Collider[] zombieScanBuffer = new Collider[96];
        Collider[] vehicleContactColliders;
        Bounds vehicleContactBounds;
        bool vehicleContactBoundsValid;
        readonly NetworkVariable<ulong> driverClientId = new(NoDriver);
        readonly NetworkVariable<float> forkLevel = new(0f);
        float serverSteering;
        float serverAcceleration;
        float serverHandbrake;
        float serverForkInput;
        float serverLastInputTime = float.NegativeInfinity;
        float nextInputSendTime;

        bool IsNetworkClientOnly => NetworkManager.Singleton != null &&
                                    NetworkManager.Singleton.IsListening &&
                                    !NetworkManager.Singleton.IsServer;

        void Awake()
        {
            if (driveControl == null) driveControl = GetComponent<NewCarUserControl>();
            if (vehicleController == null) vehicleController = GetComponent<NewCarController>();
            if (forkControl == null) forkControl = GetComponent<ForkController>();
            body = GetComponent<Rigidbody>();
            vehicleAudio = GetComponent<VehicleAudio>();
            CacheVehicleContactGeometry();
            if (driveControl != null) driveControl.enabled = false;
            if (forkControl != null) forkControl.enabled = false;
        }

        void Start() => ConfigureBodyStability();

        public override void OnNetworkSpawn()
        {
            driverClientId.OnValueChanged += OnDriverChanged;
            forkLevel.OnValueChanged += OnForkLevelChanged;
            if (body != null) body.isKinematic = !IsServer;
            if (IsServer && forkControl != null && forkControl.fork != null)
                forkLevel.Value = Mathf.InverseLerp(forkControl.minY.y, forkControl.maxY.y,
                    forkControl.fork.localPosition.y);
            ApplyForkLevel(forkLevel.Value);
            OnDriverChanged(NoDriver, driverClientId.Value);
        }

        public override void OnNetworkDespawn()
        {
            vehicleAudio?.SetEngineOn(false);
            RestoreZombiePhysicsPairs();
            driverClientId.OnValueChanged -= OnDriverChanged;
            forkLevel.OnValueChanged -= OnForkLevelChanged;
            if (driver != null) EndLocalDriving(false);
            if (body != null) body.isKinematic = false;
        }

        void Update()
        {
            if (IsSpawned && IsServer && driverClientId.Value != NoDriver &&
                (NetworkManager.Singleton == null ||
                 !NetworkManager.Singleton.ConnectedClients.ContainsKey(driverClientId.Value)))
                driverClientId.Value = NoDriver;

            if (Keyboard.current == null) return;

            if (driver != null)
            {
                ShowPrompt(GameLocalization.Text(
                    "E  EXIT   |   WASD  DRIVE   |   SPACE  HANDBRAKE   |   R/F  FORK UP/DOWN   |   H  HORN",
                    "E  UITSTAPPEN   |   WASD  RIJDEN   |   SPACE  HANDREM   |   R/F  VORK OMHOOG/OMLAAG   |   H  CLAXON"));
                if (Keyboard.current.eKey.wasPressedThisFrame) RequestExitVehicle();
                if (Keyboard.current.hKey.wasPressedThisFrame) vehicleAudio?.PlayHorn();
                return;
            }

            PlayerCharacterController localPlayer = FindLocalPlayer();
            if (localPlayer == null || Vector3.Distance(localPlayer.transform.position, transform.position) > interactionDistance)
            {
                HidePrompt();
                return;
            }

            ShowPrompt(GameLocalization.Text("E  DRIVE FORKLIFT", "E  BESTUUR FORKLIFT"));
            if (Keyboard.current.eKey.wasPressedThisFrame) RequestEnterVehicle(localPlayer);
        }

        float vehicleLookYaw;
        float vehicleLookPitch;

        void LateUpdate()
        {
            if (driver == null || seat == null)
                return;

            Transform cameraTransform =
                driver.PlayerCamera != null
                    ? driver.PlayerCamera.transform
                    : driver.transform;

            // Read vehicle-only look input. This keeps the camera relative to the
            // forklift instead of leaving it pointing in the world-space direction
            // it had when the player entered.
            Vector2 lookDelta = Vector2.zero;

            if (Mouse.current != null)
                lookDelta += Mouse.current.delta.ReadValue() * mouseLookSensitivity;

            if (Gamepad.current != null)
                lookDelta += Gamepad.current.rightStick.ReadValue() *
                             gamepadLookSpeed * Time.unscaledDeltaTime;

            vehicleLookYaw += lookDelta.x;
            vehicleLookPitch -= lookDelta.y;

            vehicleLookYaw = Mathf.Clamp(
                vehicleLookYaw,
                -maxLookLeftRight,
                maxLookLeftRight);

            vehicleLookPitch = Mathf.Clamp(
                vehicleLookPitch,
                -maxLookDown,
                maxLookUp);

            // Keep the player/camera eye exactly at the authored driver's seat.
            driver.transform.position += seat.position - cameraTransform.position;

            Quaternion vehicleYaw =
                seat.rotation * Quaternion.Euler(0f, vehicleLookYaw, 0f);

            driver.transform.rotation = vehicleYaw;

            // Apply pitch directly to the actual FPS camera after the normal player
            // update has run. DefaultExecutionOrder(100) helps us win that ordering.
            if (driver.PlayerCamera != null)
            {
                driver.PlayerCamera.transform.rotation =
                    vehicleYaw * Quaternion.Euler(vehicleLookPitch, 0f, 0f);
            }
        }

        void FixedUpdate()
        {
            if (!IsNetworkClientOnly) UpdateZombieCollisionIsolation();
            if (!IsNetworkClientOnly) StabilizeBody();
            if (!IsNetworkClientOnly) ResistParkedExternalPushes();
            if (vehicleController == null) return;

            bool networked = IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (driver != null)
            {
                ReadDriveInput(out float steering, out float acceleration, out float handbrake, out float forkInput);

                vehicleAudio?.SetInput(acceleration, handbrake, true);
                vehicleAudio?.SetAuxiliaryInput(forkInput);

                if (!networked)
                {
                    vehicleController.Move(steering, acceleration, acceleration, handbrake);
                    MoveFork(forkInput);
                    return;
                }

                if (IsServer)
                    SetServerDriveInput(steering, acceleration, handbrake, forkInput);
                else if (Time.unscaledTime >= nextInputSendTime)
                {
                    nextInputSendTime = Time.unscaledTime + .033f;
                    PlayerClassController playerClass = driver.GetComponent<PlayerClassController>();
                    if (playerClass != null)
                        playerClass.SubmitForkliftInput(steering, acceleration, handbrake, forkInput);
                    else
                        DriveInputRpc(steering, acceleration, handbrake, forkInput);
                }
            }

            if (networked && IsServer)
            {
                if (driverClientId.Value == NoDriver)
                {
                    vehicleAudio?.SetInput(0f, 1f, false);
                    vehicleAudio?.SetAuxiliaryInput(0f);
                    vehicleController.Move(0f, 0f, 0f, 1f);
                    return;
                }
                if (Time.unscaledTime > serverLastInputTime + .5f)
                    SetServerDriveInput(0f, 0f, 0f, 0f);
                vehicleAudio?.SetInput(serverAcceleration, serverHandbrake, false);
                vehicleAudio?.SetAuxiliaryInput(serverForkInput);
                vehicleController.Move(serverSteering, serverAcceleration, serverAcceleration, serverHandbrake);
                MoveFork(serverForkInput);
            }
        }

        static void ReadDriveInput(out float steering, out float acceleration, out float handbrake,
            out float forkInput)
        {
            steering = 0f;
            acceleration = 0f;
            handbrake = 0f;
            forkInput = 0f;
            if (Keyboard.current != null)
            {
                steering = (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                           (Keyboard.current.aKey.isPressed ? 1f : 0f);
                acceleration = (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                               (Keyboard.current.sKey.isPressed ? 1f : 0f);
                handbrake = Keyboard.current.spaceKey.isPressed ? 1f : 0f;
                bool forkUp = Keyboard.current.rKey.isPressed || Keyboard.current.pageUpKey.isPressed ||
                              Keyboard.current.upArrowKey.isPressed;
                bool forkDown = Keyboard.current.fKey.isPressed || Keyboard.current.pageDownKey.isPressed ||
                                Keyboard.current.downArrowKey.isPressed;
                forkInput = (forkUp ? 1f : 0f) - (forkDown ? 1f : 0f);
            }
            if (Gamepad.current == null) return;
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (Mathf.Abs(stick.x) > Mathf.Abs(steering)) steering = stick.x;
            float trigger = Gamepad.current.rightTrigger.ReadValue() - Gamepad.current.leftTrigger.ReadValue();
            if (Mathf.Abs(trigger) > Mathf.Abs(acceleration)) acceleration = trigger;
            if (Gamepad.current.buttonSouth.isPressed) handbrake = 1f;
            if (Gamepad.current.rightShoulder.isPressed) forkInput = 1f;
            else if (Gamepad.current.leftShoulder.isPressed) forkInput = -1f;
        }

        PlayerCharacterController FindLocalPlayer()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkObject playerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
                return playerObject != null ? playerObject.GetComponent<PlayerCharacterController>() : null;
            }
            return FindAnyObjectByType<PlayerCharacterController>();
        }

        void RequestEnterVehicle(PlayerCharacterController player)
        {
            if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                BeginLocalDriving(player);
                return;
            }

            PlayerClassController playerClass = player.GetComponent<PlayerClassController>();
            if (playerClass != null)
                playerClass.RequestForkliftControl();
            else if (IsServer) TryAssignDriver(NetworkManager.Singleton.LocalClientId);
            else RequestEnterVehicleRpc();
        }

        [Rpc(SendTo.Server)]
        void RequestEnterVehicleRpc(RpcParams rpcParams = default)
        {
            TryAssignDriver(rpcParams.Receive.SenderClientId);
        }

        void TryAssignDriver(ulong clientId)
        {
            if (!IsServer || driverClientId.Value != NoDriver || NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
                client.PlayerObject == null ||
                Vector3.Distance(client.PlayerObject.transform.position, transform.position) > interactionDistance + 1f)
                return;

            driverClientId.Value = clientId;
            SetServerDriveInput(0f, 0f, 0f, 0f);
        }

        void OnDriverChanged(ulong previous, ulong current)
        {
            vehicleAudio?.SetEngineOn(current != NoDriver);
            if (NetworkManager.Singleton == null) return;
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            if (previous == localClientId && driver != null) EndLocalDriving(true);
            if (current == localClientId && driver == null)
            {
                PlayerCharacterController localPlayer = FindLocalPlayer();
                if (localPlayer != null) BeginLocalDriving(localPlayer);
            }
        }

        void BeginLocalDriving(PlayerCharacterController player)
        {
            driver = player;
            if (seat != null)
            {
                vehicleLookYaw = 0f;
                vehicleLookPitch = 0f;
                driver.transform.rotation = seat.rotation;
            }

            driverOwnership = player.GetComponent<NetworkPlayerOwnership>();
            driverInput = player.GetComponent<PlayerInputHandler>();
            driverCapsule = player.GetComponent<CharacterController>();
            driverWeapons = player.GetComponent<PlayerWeaponsManager>();
            hiddenWeapon = driverWeapons != null ? driverWeapons.GetActiveWeapon() : null;

            driverOwnership?.SetLocalGameplayReady(false);
            // Keep the gameplay input context/cursor active for direct vehicle input,
            // while movement and weapons remain disabled by NetworkPlayerOwnership.
            driverInput?.SetGameplayInputEnabled(true);
            if (driverCapsule != null) driverCapsule.enabled = false;
            if (hiddenWeapon != null) hiddenWeapon.ShowWeapon(false);
            if (driveControl != null) driveControl.enabled = false;
            if (forkControl != null) forkControl.enabled = false;
            ConfigureBodyStability();

            if (!IsSpawned ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening)
            {
                vehicleAudio?.SetEngineOn(true);
            }
        }

        void RequestExitVehicle()
        {
            if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                EndLocalDriving(true);
                return;
            }

            PlayerClassController playerClass = driver != null ? driver.GetComponent<PlayerClassController>() : null;
            if (playerClass != null)
                playerClass.RequestForkliftExit();
            else if (IsServer && driverClientId.Value == NetworkManager.Singleton.LocalClientId)
                driverClientId.Value = NoDriver;
            else RequestExitVehicleRpc();
        }

        [Rpc(SendTo.Server)]
        void RequestExitVehicleRpc(RpcParams rpcParams = default)
        {
            if (driverClientId.Value != rpcParams.Receive.SenderClientId) return;
            driverClientId.Value = NoDriver;
            SetServerDriveInput(0f, 0f, 1f, 0f);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void DriveInputRpc(float steering, float acceleration, float handbrake, float forkInput,
            RpcParams rpcParams = default)
        {
            if (driverClientId.Value != rpcParams.Receive.SenderClientId) return;
            SetServerDriveInput(steering, acceleration, handbrake, forkInput);
        }

        void SetServerDriveInput(float steering, float acceleration, float handbrake, float forkInput)
        {
            serverSteering = Mathf.Clamp(steering, -1f, 1f);
            serverAcceleration = Mathf.Clamp(acceleration, -1f, 1f);
            serverHandbrake = Mathf.Clamp01(handbrake);
            serverForkInput = Mathf.Clamp(forkInput, -1f, 1f);
            serverLastInputTime = Time.unscaledTime;
        }

        public static void TryAssignClosestOnServer(PlayerClassController player, ulong clientId)
        {
            if (player == null || !player.IsServer) return;
            ForkliftDriver closest = null;
            float closestDistance = float.PositiveInfinity;
            foreach (ForkliftDriver forklift in FindObjectsByType<ForkliftDriver>())
            {
                if (!forklift.IsSpawned || !forklift.IsServer || forklift.driverClientId.Value != NoDriver) continue;
                float distance = Vector3.Distance(player.transform.position, forklift.transform.position);
                if (distance < closestDistance) { closestDistance = distance; closest = forklift; }
            }
            if (closest != null && closestDistance <= closest.interactionDistance + 1f)
                closest.TryAssignDriver(clientId);
        }

        public static void ApplyInputOnServer(ulong clientId, float steering, float acceleration,
            float handbrake, float forkInput)
        {
            foreach (ForkliftDriver forklift in FindObjectsByType<ForkliftDriver>())
            {
                if (!forklift.IsServer || forklift.driverClientId.Value != clientId) continue;
                forklift.SetServerDriveInput(steering, acceleration, handbrake, forkInput);
                return;
            }
        }

        public static void ExitOnServer(ulong clientId)
        {
            foreach (ForkliftDriver forklift in FindObjectsByType<ForkliftDriver>())
            {
                if (!forklift.IsServer || forklift.driverClientId.Value != clientId) continue;
                forklift.driverClientId.Value = NoDriver;
                forklift.SetServerDriveInput(0f, 0f, 1f, 0f);
                return;
            }
        }

        void MoveFork(float inputValue)
        {
            if (forkControl == null || forkControl.fork == null || forkControl.mast == null) return;
            if (Mathf.Abs(inputValue) > .01f)
            {
                float travel = Mathf.Max(.01f, Mathf.Abs(forkControl.maxY.y - forkControl.minY.y));
                float level = Mathf.Clamp01(forkLevel.Value +
                    inputValue * Mathf.Max(.01f, forkControl.speedTranslate) * Time.fixedDeltaTime / travel);
                if (IsSpawned && IsServer) forkLevel.Value = level;
                else ApplyForkLevel(level);
            }
            ApplyForkLevel(forkLevel.Value);
        }

        void OnForkLevelChanged(float previous, float current) => ApplyForkLevel(current);

        void ApplyForkLevel(float level)
        {
            if (forkControl == null || forkControl.fork == null || forkControl.mast == null) return;
            forkControl.fork.localPosition = Vector3.Lerp(forkControl.minY, forkControl.maxY, level);
            forkControl.mast.localPosition = Vector3.Lerp(forkControl.minYmast, forkControl.maxYmast, level);
        }

        void EndLocalDriving(bool moveToExit)
        {
            if (driver == null) return;
            if (driveControl != null) driveControl.enabled = false;
            if (forkControl != null) forkControl.enabled = false;

            vehicleAudio?.SetInput(0f, 1f, false);
            vehicleAudio?.SetAuxiliaryInput(0f);

            if (!IsSpawned ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening)
            {
                vehicleAudio?.SetEngineOn(false);
            }

            if ((!IsSpawned || IsServer) && vehicleController != null)
                vehicleController.Move(0f, 0f, 0f, 1f);

            if (moveToExit && exitPoint != null)
                driver.transform.SetPositionAndRotation(exitPoint.position, exitPoint.rotation);
            if (hiddenWeapon != null) hiddenWeapon.ShowWeapon(true);
            if (driverCapsule != null) driverCapsule.enabled = true;
            driverOwnership?.SetLocalGameplayReady(true);
            driverInput?.SetGameplayInputEnabled(true);

            driver = null;
            driverOwnership = null;
            driverInput = null;
            driverCapsule = null;
            driverWeapons = null;
            hiddenWeapon = null;
            HidePrompt();
        }

        void CacheVehicleContactGeometry()
        {
            vehicleContactColliders = GetComponentsInChildren<Collider>(true);
            RefreshVehicleContactBounds();
        }

        void RefreshVehicleContactBounds()
        {
            vehicleContactBoundsValid = false;

            if (vehicleContactColliders == null || vehicleContactColliders.Length == 0)
                return;

            foreach (Collider vehicleCollider in vehicleContactColliders)
            {
                if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger)
                    continue;

                if (!vehicleContactBoundsValid)
                {
                    vehicleContactBounds = vehicleCollider.bounds;
                    vehicleContactBoundsValid = true;
                }
                else
                {
                    vehicleContactBounds.Encapsulate(vehicleCollider.bounds);
                }
            }
        }

        void UpdateZombieCollisionIsolation()
        {
            if (body == null)
                return;

            if (!isolateZombiePhysics)
            {
                RestoreZombiePhysicsPairs();
                return;
            }

            if (vehicleContactColliders == null || vehicleContactColliders.Length == 0)
                CacheVehicleContactGeometry();
            else
                RefreshVehicleContactBounds();

            if (!vehicleContactBoundsValid)
                return;

            Bounds scanBounds = vehicleContactBounds;
            scanBounds.Expand(zombiePreContactPadding * 2f);

            int hitCount = Physics.OverlapBoxNonAlloc(
                scanBounds.center,
                scanBounds.extents,
                zombieScanBuffer,
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Collide);

            ignoredZombieCleanup.Clear();
            foreach (Collider ignored in ignoredZombieColliders.Keys)
                ignoredZombieCleanup.Add(ignored);

            processedZombies.Clear();

            Vector3 yieldVelocity = GetVehicleYieldVelocity();
            float actualSpeed = body.linearVelocity.magnitude;

            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = zombieScanBuffer[i];
                zombieScanBuffer[i] = null;

                if (candidate == null)
                    continue;

                ZombieAI zombie = candidate.GetComponentInParent<ZombieAI>();
                if (zombie == null || !processedZombies.Add(zombie))
                    continue;

                zombieColliderScratch.Clear();
                zombie.GetComponentsInChildren<Collider>(true, zombieColliderScratch);

                Collider primaryCollider = null;

                foreach (Collider zombieCollider in zombieColliderScratch)
                {
                    if (zombieCollider == null || !zombieCollider.enabled || zombieCollider.isTrigger)
                        continue;

                    primaryCollider ??= zombieCollider;

                    IgnoreZombiePhysicsPair(zombieCollider, true);
                    ignoredZombieColliders[zombieCollider] = Time.time;
                    ignoredZombieCleanup.Remove(zombieCollider);
                }

                if (primaryCollider == null)
                    continue;

                // Parked forklift: do not stop navigation. This is what lets zombies approach
                // doors/windows and enter through authored NavMesh links.
                if (yieldVelocity.magnitude >= zombieYieldSpeedThreshold &&
                    IsZombieNearVehicleSurface(primaryCollider))
                {
                    zombie.YieldToVehicle(body.worldCenterOfMass, yieldVelocity);

                    if (actualSpeed >= minimumImpactSpeed)
                        ApplyImpactDamage(primaryCollider, actualSpeed);
                }
            }

            foreach (Collider zombieCollider in ignoredZombieCleanup)
            {
                if (zombieCollider == null)
                    continue;

                if (!ignoredZombieColliders.TryGetValue(zombieCollider, out float lastSeen))
                    continue;

                if (Time.time - lastSeen < zombieIgnoreReleaseDelay)
                    continue;

                IgnoreZombiePhysicsPair(zombieCollider, false);
                ignoredZombieColliders.Remove(zombieCollider);
            }
        }

        bool IsZombieNearVehicleSurface(Collider zombieCollider)
        {
            if (zombieCollider == null || vehicleContactColliders == null)
                return false;

            Vector3 zombieCenter = zombieCollider.bounds.center;
            float maxDistanceSqr = zombieTouchDistance * zombieTouchDistance;

            foreach (Collider vehicleCollider in vehicleContactColliders)
            {
                if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger)
                    continue;

                Vector3 closest = vehicleCollider.ClosestPoint(zombieCenter);
                if ((closest - zombieCenter).sqrMagnitude <= maxDistanceSqr)
                    return true;
            }

            return false;
        }

        void IgnoreZombiePhysicsPair(Collider zombieCollider, bool ignore)
        {
            if (zombieCollider == null)
                return;

            if (vehicleContactColliders == null || vehicleContactColliders.Length == 0)
                CacheVehicleContactGeometry();

            foreach (Collider vehicleCollider in vehicleContactColliders)
            {
                if (vehicleCollider == null ||
                    !vehicleCollider.enabled ||
                    vehicleCollider.isTrigger ||
                    vehicleCollider == zombieCollider)
                {
                    continue;
                }

                Physics.IgnoreCollision(vehicleCollider, zombieCollider, ignore);
            }
        }

        void ApplyImpactDamage(Collider zombieCollider, float impactSpeed)
        {
            if (zombieCollider == null || impactSpeed < minimumImpactSpeed)
                return;

            Damageable damageable = zombieCollider.GetComponentInParent<Damageable>();
            if (damageable == null || damageable.Health == null)
                return;

            Health target = damageable.Health;

            if (impactCooldowns.TryGetValue(target, out float nextImpact) &&
                Time.time < nextImpact)
            {
                return;
            }

            impactCooldowns[target] = Time.time + .4f;

            float damage = Mathf.Clamp(
                impactSpeed * impactDamagePerMetre,
                10f,
                120f);

            GameObject source = driver != null ? driver.gameObject : gameObject;
            damageable.InflictDamage(damage, false, source);
            vehicleAudio?.PlayImpact(impactSpeed);
        }

        void RestoreZombiePhysicsPairs()
        {
            if (ignoredZombieColliders.Count == 0)
                return;

            ignoredZombieCleanup.Clear();
            foreach (Collider zombieCollider in ignoredZombieColliders.Keys)
                ignoredZombieCleanup.Add(zombieCollider);

            foreach (Collider zombieCollider in ignoredZombieCleanup)
            {
                if (zombieCollider != null)
                    IgnoreZombiePhysicsPair(zombieCollider, false);
            }

            ignoredZombieColliders.Clear();
            ignoredZombieCleanup.Clear();
        }

        float GetAccelerationIntent()
        {
            float intent = serverAcceleration;

            if (driver != null)
            {
                if (Keyboard.current != null)
                {
                    float keyboard =
                        (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                        (Keyboard.current.sKey.isPressed ? 1f : 0f);

                    if (Mathf.Abs(keyboard) > Mathf.Abs(intent))
                        intent = keyboard;
                }

                if (Gamepad.current != null)
                {
                    float gamepad =
                        Gamepad.current.rightTrigger.ReadValue() -
                        Gamepad.current.leftTrigger.ReadValue();

                    if (Mathf.Abs(gamepad) > Mathf.Abs(intent))
                        intent = gamepad;
                }
            }

            return Mathf.Clamp(intent, -1f, 1f);
        }

        Vector3 GetVehicleYieldVelocity()
        {
            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
            float accelerationIntent = GetAccelerationIntent();

            // On the first frame of pulling away the Rigidbody can still report ~0 m/s.
            // Give the zombie AI a small direction hint so the forklift does not have to build
            // speed against a stationary NavMeshAgent first.
            if (velocity.magnitude < zombieYieldSpeedThreshold &&
                Mathf.Abs(accelerationIntent) > .05f)
            {
                velocity =
                    transform.forward *
                    Mathf.Sign(accelerationIntent) *
                    Mathf.Max(zombieYieldSpeedThreshold, .8f);
            }

            return velocity;
        }

        void ConfigureBodyStability()
        {
            if (body == null)
                return;

            body.mass = Mathf.Max(body.mass, 6000f);
            body.centerOfMass = stableCenterOfMass;

            // Niet de hele Rigidbody-rotatie afremmen.
            // Hoge angular damping maakte vooral het sturen erg zwaar.
            body.angularDamping = 0.15f;

            // Laat voldoende yaw/draaisnelheid toe.
            body.maxAngularVelocity = 7f;
        }

        void StabilizeBody()
        {
            if (body == null || body.isKinematic) return;
            Vector3 tiltCorrection = Vector3.Cross(transform.up, Vector3.up) * uprightStrength;
            Vector3 tippingVelocity = Vector3.ProjectOnPlane(body.angularVelocity, transform.up);
            body.AddTorque(tiltCorrection - tippingVelocity * uprightDamping, ForceMode.Acceleration);
        }

              void ResistParkedExternalPushes()
        {
            if (!resistExternalPushesWhenParked || body == null || body.isKinematic)
                return;

            bool hasDriveInput =
                Mathf.Abs(serverAcceleration) > 0.05f ||
                Mathf.Abs(serverSteering) > 0.08f ||
                (driver != null && (
                    (Keyboard.current != null &&
                     (Keyboard.current.wKey.isPressed ||
                      Keyboard.current.sKey.isPressed ||
                      Keyboard.current.aKey.isPressed ||
                      Keyboard.current.dKey.isPressed)) ||
                    (Gamepad.current != null &&
                     (Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.02f ||
                      Gamepad.current.rightTrigger.ReadValue() > 0.05f ||
                      Gamepad.current.leftTrigger.ReadValue() > 0.05f))));

            // Never fight the vehicle controller while the driver is trying to move.
            // The previous version damped velocity every FixedUpdate below the threshold,
            // which made acceleration feel extremely slow.
            if (hasDriveInput)
                return;

            float horizontalSpeed =
                Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;

            if (horizontalSpeed > parkedSpeedThreshold)
                return;

            // Strongly reject tiny external nudges while parked.
            Vector3 velocity = body.linearVelocity;
            Vector3 horizontal =
                Vector3.ProjectOnPlane(velocity, Vector3.up) *
                parkedHorizontalVelocityRetention;

            body.linearVelocity =
                horizontal + Vector3.Project(velocity, Vector3.up);

            // Keep yaw free so steering remains natural once input starts.
            Vector3 angular = body.angularVelocity;
            angular.x *= parkedTiltVelocityRetention;
            angular.z *= parkedTiltVelocityRetention;
            body.angularVelocity = angular;
        }




        void OnCollisionEnter(Collision collision)
        {
            // Fallback for debugging if zombie isolation is deliberately disabled.
            if (isolateZombiePhysics || IsNetworkClientOnly || body == null || collision == null)
                return;

            Collider hitCollider = collision.collider;
            ZombieAI zombie = hitCollider != null ? hitCollider.GetComponentInParent<ZombieAI>() : null;
            if (zombie == null)
                return;

            Vector3 yieldVelocity = GetVehicleYieldVelocity();
            if (yieldVelocity.magnitude >= zombieYieldSpeedThreshold)
                zombie.YieldToVehicle(body.worldCenterOfMass, yieldVelocity);

            ApplyImpactDamage(hitCollider, collision.relativeVelocity.magnitude);
        }

        void ShowPrompt(string message)
        {
            if (promptCanvas == null)
            {
                promptCanvas = RuntimeMenuUI.CreateCanvas("Forklift Prompt", 850);
                RectTransform panel = RuntimeMenuUI.Block("Prompt", promptCanvas.transform,
                    new Color(.025f, .04f, .055f, .9f));
                panel.anchorMin = new Vector2(.25f, .04f);
                panel.anchorMax = new Vector2(.75f, .105f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                promptText = RuntimeMenuUI.Label("Text", panel, string.Empty, 17,
                    TextAnchor.MiddleCenter, RuntimeMenuUI.White);
                RuntimeMenuUI.Stretch(promptText.rectTransform, 12, 12, 4, 4);
            }
            promptCanvas.gameObject.SetActive(true);
            promptText.text = message;
        }

        void HidePrompt()
        {
            if (promptCanvas != null) promptCanvas.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            RestoreZombiePhysicsPairs();
            if (driver != null) EndLocalDriving(false);
        }

        public override void OnDestroy()
        {
            RestoreZombiePhysicsPairs();
            if (promptCanvas != null) Destroy(promptCanvas.gameObject);
            base.OnDestroy();
        }
    }
}