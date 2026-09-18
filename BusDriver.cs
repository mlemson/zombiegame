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
    public sealed class BusDriver : NetworkBehaviour
    {
        const ulong NoDriver = ulong.MaxValue;

        [Header("Bus")]
        [SerializeField] NewCarUserControl driveControl;
        [SerializeField] NewCarController vehicleController;
        [SerializeField] Transform seat;
        [SerializeField] Transform interactionPoint;
        [SerializeField] Transform exitPoint;

        [Header("Interaction")]
        [SerializeField] float interactionDistance = 3.2f;
        [SerializeField] string vehicleNameEnglish = "BUS";
        [SerializeField] string vehicleNameDutch = "BUS";

        [Header("Zombie Vehicle Physics")]
        [Tooltip("Prevents NavMesh-driven zombies from transferring physics forces into the bus. The zombie AI handles one-way solid vehicle geometry instead.")]
        [SerializeField] bool isolateZombiePhysics = true;

        [Tooltip("How far outside the bus bounds zombies are detected before physical contact.")]
        [SerializeField, Min(0.05f)] float zombiePreContactPadding = 0.75f;

        [Tooltip("How close a zombie must be to the bus surface before a moving bus makes it yield.")]
        [SerializeField, Min(0.02f)] float zombieTouchDistance = 0.30f;

        [Tooltip("How long ignored collider pairs stay isolated after a zombie leaves the padded bus bounds.")]
        [SerializeField, Min(0.05f)] float zombieIgnoreReleaseDelay = 0.45f;

        [Tooltip("Below this speed a parked bus does not tell zombies to yield, so they can navigate to authored doors/windows.")]
        [SerializeField, Min(0f)] float zombieYieldSpeedThreshold = 0.65f;

        [Header("Low-speed stability")]
        [Tooltip("Damps small non-zombie nudges while the bus is almost stationary. This never freezes Rigidbody constraints.")]
        [SerializeField] bool resistExternalPushesWhenParked = true;
        [SerializeField, Min(0f)] float parkedSpeedThreshold = 0.9f;
        [SerializeField, Range(0f, 1f)] float parkedHorizontalVelocityRetention = 0.35f;
        [SerializeField, Range(0f, 1f)] float parkedTiltVelocityRetention = 0.35f;

        [Header("Impact damage")]
        [SerializeField] float minimumImpactSpeed = 3.5f;
        [SerializeField] float impactDamagePerMetre = 14f;

        [Header("Stability")]
        [SerializeField, Min(0f)] float uprightStrength = 18f;
        [SerializeField, Min(0f)] float uprightDamping = 6f;
        [SerializeField] Vector3 stableCenterOfMass = new(0f, -.85f, 0f);

        [Header("Driver Camera")]
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float maxLookLeftRight = 100f;
        [SerializeField] float maxLookUp = 60f;
        [SerializeField] float maxLookDown = 45f;

        float vehicleLookYaw;
        float vehicleLookPitch;

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
        readonly List<ZombieAI> nearbyZombieScratch = new();
        readonly List<Collider> zombieColliderScratch = new();

        Collider[] vehicleContactColliders;
        Bounds vehicleContactBounds;
        bool vehicleContactBoundsValid;
        readonly NetworkVariable<ulong> driverClientId = new(NoDriver);

        float serverSteering;
        float serverAcceleration;
        float serverHandbrake;
        float serverLastInputTime = float.NegativeInfinity;
        float nextInputSendTime;

        bool IsNetworkClientOnly =>
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            !NetworkManager.Singleton.IsServer;

        void Awake()
        {
            if (driveControl == null)
                driveControl = GetComponent<NewCarUserControl>();

            if (vehicleController == null)
                vehicleController = GetComponent<NewCarController>();

            body = GetComponent<Rigidbody>();
            vehicleAudio = GetComponent<VehicleAudio>();
            CacheVehicleContactGeometry();

            // BusDriver reads input itself, just like ForkliftDriver.
            if (driveControl != null)
                driveControl.enabled = false;
        }

        void Start()
        {
            ConfigureBodyStability();
        }

        public override void OnNetworkSpawn()
        {
            driverClientId.OnValueChanged += OnDriverChanged;

            // Physics is server-authoritative.
            if (body != null)
                body.isKinematic = !IsServer;

            OnDriverChanged(NoDriver, driverClientId.Value);
        }

        public override void OnNetworkDespawn()
        {
            vehicleAudio?.SetEngineOn(false);
            RestoreZombiePhysicsPairs();

            driverClientId.OnValueChanged -= OnDriverChanged;

            if (driver != null)
                EndLocalDriving(false);

            if (body != null)
                body.isKinematic = false;
        }

        void Update()
        {
            // Release the bus if its driver disconnected.
            if (IsSpawned &&
                IsServer &&
                driverClientId.Value != NoDriver &&
                (NetworkManager.Singleton == null ||
                 !NetworkManager.Singleton.ConnectedClients.ContainsKey(driverClientId.Value)))
            {
                driverClientId.Value = NoDriver;
            }

            if (Keyboard.current == null)
                return;

            // Already driving this bus.
            if (driver != null)
            {
                ShowPrompt(GameLocalization.Text(
                    "F  EXIT   |   WASD  DRIVE   |   SPACE  HANDBRAKE   |   H  HORN",
                    "F  UITSTAPPEN   |   WASD  RIJDEN   |   SPACE  HANDREM   |   H  CLAXON"));

                if (Unity.FPS.Game.GameplayInteraction.Pressed)
                    RequestExitVehicle();

                if (Keyboard.current.hKey.wasPressedThisFrame)
                    vehicleAudio?.PlayHorn();

                return;
            }

            PlayerCharacterController localPlayer = FindLocalPlayer();
            if (localPlayer == null)
            {
                HidePrompt();
                return;
            }

            Vector3 usePosition =
                interactionPoint != null
                    ? interactionPoint.position
                    : transform.position;

            if (Vector3.Distance(localPlayer.transform.position, usePosition) > interactionDistance)
            {
                HidePrompt();
                return;
            }

            ShowPrompt(GameLocalization.Text(
                $"F  DRIVE {vehicleNameEnglish}",
                $"F  BESTUUR {vehicleNameDutch}"));

            if (Unity.FPS.Game.GameplayInteraction.Pressed)
                RequestEnterVehicle(localPlayer);
        }

        void LateUpdate()
        {
            if (driver == null || seat == null)
                return;

            Transform eye =
                driver.PlayerCamera != null
                    ? driver.PlayerCamera.transform
                    : driver.transform;

            // Speler blijft fysiek bij de bestuurdersstoel
            driver.transform.position +=
                seat.position - eye.position;

            if (Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();

                vehicleLookYaw += mouseDelta.x * lookSensitivity;
                vehicleLookPitch -= mouseDelta.y * lookSensitivity;

                vehicleLookYaw = Mathf.Clamp(
                    vehicleLookYaw,
                    -maxLookLeftRight,
                    maxLookLeftRight);

                vehicleLookPitch = Mathf.Clamp(
                    vehicleLookPitch,
                    -maxLookDown,
                    maxLookUp);
            }

            // Horizontale richting van speler volgt bus + kijkrichting
            Quaternion yawRotation =
                seat.rotation *
                Quaternion.Euler(0f, vehicleLookYaw, 0f);

            driver.transform.rotation = yawRotation;

            // Verticale camera onafhankelijk
            if (driver.PlayerCamera != null)
            {
                driver.PlayerCamera.transform.rotation =
                    yawRotation *
                    Quaternion.Euler(vehicleLookPitch, 0f, 0f);
            }
        }

        void FixedUpdate()
        {
            if (!IsNetworkClientOnly)
                UpdateZombieCollisionIsolation();

            if (!IsNetworkClientOnly)
                StabilizeBody();

            if (!IsNetworkClientOnly)
                ResistParkedExternalPushes();

            if (vehicleController == null)
                return;

            bool networked =
                IsSpawned &&
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsListening;

            if (driver != null)
            {
                ReadDriveInput(
                    out float steering,
                    out float acceleration,
                    out float handbrake);

                // Local driver controls one-shot throttle/brake sounds.
                // The continuous driving loop also reacts to the real Rigidbody speed.
                vehicleAudio?.SetInput(acceleration, handbrake, true);

                // Offline/local test.
                if (!networked)
                {
                    // Deliberately identical to the working ForkliftDriver.
                    vehicleController.Move(
                        steering,
                        acceleration,
                        acceleration,
                        handbrake);

                    return;
                }

                // Host/server reads its own local input directly.
                if (IsServer)
                {
                    SetServerDriveInput(
                        steering,
                        acceleration,
                        handbrake);
                }
                // Remote client sends its input to the server.
                else if (Time.unscaledTime >= nextInputSendTime)
                {
                    nextInputSendTime = Time.unscaledTime + .033f;

                    DriveInputRpc(
                        steering,
                        acceleration,
                        handbrake);
                }
            }

            // Only the server actually applies vehicle physics.
            if (networked && IsServer)
            {
                if (driverClientId.Value == NoDriver)
                {
                    vehicleAudio?.SetInput(0f, 1f, false);
                    vehicleController.Move(0f, 0f, 0f, 1f);
                    return;
                }

                if (Time.unscaledTime > serverLastInputTime + .5f)
                {
                    SetServerDriveInput(0f, 0f, 0f);
                }

                // Keep host/server audio state in sync when another client is driving.
                vehicleAudio?.SetInput(serverAcceleration, serverHandbrake, false);

                // Deliberately identical to the working ForkliftDriver.
                vehicleController.Move(
                    serverSteering,
                    serverAcceleration,
                    serverAcceleration,
                    serverHandbrake);
            }
        }

        static void ReadDriveInput(
            out float steering,
            out float acceleration,
            out float handbrake)
        {
            steering = 0f;
            acceleration = 0f;
            handbrake = 0f;

            if (Keyboard.current != null)
            {
                steering =
                    (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                    (Keyboard.current.aKey.isPressed ? 1f : 0f);

                acceleration =
                    (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                    (Keyboard.current.sKey.isPressed ? 1f : 0f);

                handbrake =
                    Keyboard.current.spaceKey.isPressed ? 1f : 0f;
            }

            if (Gamepad.current == null)
                return;

            Vector2 stick = Gamepad.current.leftStick.ReadValue();

            if (Mathf.Abs(stick.x) > Mathf.Abs(steering))
                steering = stick.x;

            float trigger =
                Gamepad.current.rightTrigger.ReadValue() -
                Gamepad.current.leftTrigger.ReadValue();

            if (Mathf.Abs(trigger) > Mathf.Abs(acceleration))
                acceleration = trigger;

            if (Gamepad.current.buttonSouth.isPressed)
                handbrake = 1f;
        }

        PlayerCharacterController FindLocalPlayer()
        {
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsListening)
            {
                NetworkObject playerObject =
                    NetworkManager.Singleton.LocalClient?.PlayerObject;

                return playerObject != null
                    ? playerObject.GetComponent<PlayerCharacterController>()
                    : null;
            }

            return FindAnyObjectByType<PlayerCharacterController>();
        }

        void RequestEnterVehicle(PlayerCharacterController player)
        {
            if (!IsSpawned ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening)
            {
                BeginLocalDriving(player);
                return;
            }

            // Unlike ForkliftDriver, we do not route through
            // PlayerClassController.RequestForkliftControl().
            // The bus owns its own networking path.
            if (IsServer)
                TryAssignDriver(NetworkManager.Singleton.LocalClientId);
            else
                RequestEnterVehicleRpc();
        }

        [Rpc(SendTo.Server)]
        void RequestEnterVehicleRpc(RpcParams rpcParams = default)
        {
            TryAssignDriver(rpcParams.Receive.SenderClientId);
        }

        void TryAssignDriver(ulong clientId)
        {
            Vector3 usePosition =
                interactionPoint != null
                    ? interactionPoint.position
                    : transform.position;

            if (!IsServer ||
                driverClientId.Value != NoDriver ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.ConnectedClients.TryGetValue(
                    clientId,
                    out NetworkClient client) ||
                client.PlayerObject == null ||
                Vector3.Distance(
                    client.PlayerObject.transform.position,
                    usePosition) > interactionDistance + 1f)
            {
                return;
            }

            driverClientId.Value = clientId;
            SetServerDriveInput(0f, 0f, 0f);
        }

        void OnDriverChanged(ulong previous, ulong current)
        {
            // Engine starts when somebody takes the wheel and stops when the vehicle is empty.
            // This NetworkVariable callback runs on every client, so the engine loops are audible remotely too.
            vehicleAudio?.SetEngineOn(current != NoDriver);

            if (NetworkManager.Singleton == null)
                return;

            ulong localClientId =
                NetworkManager.Singleton.LocalClientId;

            if (previous == localClientId && driver != null)
                EndLocalDriving(true);

            if (current == localClientId && driver == null)
            {
                PlayerCharacterController localPlayer =
                    FindLocalPlayer();

                if (localPlayer != null)
                    BeginLocalDriving(localPlayer);
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

            driverOwnership =
                player.GetComponent<NetworkPlayerOwnership>();

            driverInput =
                player.GetComponent<PlayerInputHandler>();

            driverCapsule =
                player.GetComponent<CharacterController>();

            driverWeapons =
                player.GetComponent<PlayerWeaponsManager>();

            hiddenWeapon =
                driverWeapons != null
                    ? driverWeapons.GetActiveWeapon()
                    : null;

            // Same behaviour as the working forklift:
            // disable normal movement/combat state while retaining
            // the gameplay input context needed for camera/vehicle input.
            driverOwnership?.SetLocalGameplayReady(false);
            driverInput?.SetGameplayInputEnabled(true);

            if (driverCapsule != null)
                driverCapsule.enabled = false;

            if (hiddenWeapon != null)
                hiddenWeapon.ShowWeapon(false);

            if (driveControl != null)
                driveControl.enabled = false;

            ConfigureBodyStability();

            // Offline/local test has no NetworkVariable callback to start the engine.
            if (!IsSpawned ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening)
            {
                vehicleAudio?.SetEngineOn(true);
            }
        }

        void RequestExitVehicle()
        {
            if (!IsSpawned ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening)
            {
                EndLocalDriving(true);
                return;
            }

            if (IsServer &&
                driverClientId.Value ==
                NetworkManager.Singleton.LocalClientId)
            {
                driverClientId.Value = NoDriver;
            }
            else
            {
                RequestExitVehicleRpc();
            }
        }

        [Rpc(SendTo.Server)]
        void RequestExitVehicleRpc(RpcParams rpcParams = default)
        {
            if (driverClientId.Value !=
                rpcParams.Receive.SenderClientId)
            {
                return;
            }

            driverClientId.Value = NoDriver;
            SetServerDriveInput(0f, 0f, 1f);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void DriveInputRpc(
            float steering,
            float acceleration,
            float handbrake,
            RpcParams rpcParams = default)
        {
            if (driverClientId.Value !=
                rpcParams.Receive.SenderClientId)
            {
                return;
            }

            SetServerDriveInput(
                steering,
                acceleration,
                handbrake);
        }

        void SetServerDriveInput(
            float steering,
            float acceleration,
            float handbrake)
        {
            serverSteering =
                Mathf.Clamp(steering, -1f, 1f);

            serverAcceleration =
                Mathf.Clamp(acceleration, -1f, 1f);

            serverHandbrake =
                Mathf.Clamp01(handbrake);

            serverLastInputTime =
                Time.unscaledTime;
        }

        // Optional helpers for other multiplayer scripts.
        public static void TryAssignClosestOnServer(
            PlayerClassController player,
            ulong clientId)
        {
            if (player == null || !player.IsServer)
                return;

            BusDriver closest = null;
            float closestDistance = float.PositiveInfinity;

            foreach (BusDriver bus in FindObjectsByType<BusDriver>())
            {
                if (!bus.IsSpawned ||
                    !bus.IsServer ||
                    bus.driverClientId.Value != NoDriver)
                {
                    continue;
                }

                Vector3 usePosition =
                    bus.interactionPoint != null
                        ? bus.interactionPoint.position
                        : bus.transform.position;

                float distance =
                    Vector3.Distance(
                        player.transform.position,
                        usePosition);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = bus;
                }
            }

            if (closest != null &&
                closestDistance <= closest.interactionDistance + 1f)
            {
                closest.TryAssignDriver(clientId);
            }
        }

        public static void ApplyInputOnServer(
            ulong clientId,
            float steering,
            float acceleration,
            float handbrake)
        {
            foreach (BusDriver bus in FindObjectsByType<BusDriver>())
            {
                if (!bus.IsServer ||
                    bus.driverClientId.Value != clientId)
                {
                    continue;
                }

                bus.SetServerDriveInput(
                    steering,
                    acceleration,
                    handbrake);

                return;
            }
        }

        public static void ExitOnServer(ulong clientId)
        {
            foreach (BusDriver bus in FindObjectsByType<BusDriver>())
            {
                if (!bus.IsServer ||
                    bus.driverClientId.Value != clientId)
                {
                    continue;
                }

                bus.driverClientId.Value = NoDriver;
                bus.SetServerDriveInput(0f, 0f, 1f);
                return;
            }
        }

        void EndLocalDriving(bool moveToExit)
        {
            if (driver == null)
                return;

            if (driveControl != null)
                driveControl.enabled = false;

            vehicleAudio?.SetInput(0f, 1f, false);

            // Offline/local test has no driver NetworkVariable callback to stop the engine.
            if (!IsSpawned ||
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening)
            {
                vehicleAudio?.SetEngineOn(false);
            }

            if ((!IsSpawned || IsServer) &&
                vehicleController != null)
            {
                vehicleController.Move(
                    0f,
                    0f,
                    0f,
                    1f);
            }

            if (moveToExit && exitPoint != null)
            {
                driver.transform.SetPositionAndRotation(
                    exitPoint.position,
                    exitPoint.rotation);
            }

            if (hiddenWeapon != null)
                hiddenWeapon.ShowWeapon(true);

            if (driverCapsule != null)
                driverCapsule.enabled = true;

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
            if (isolateZombiePhysics)
                foreach (var collider in vehicleContactColliders)
                    if (collider is WheelCollider wheel)
                        wheel.excludeLayers |= LayerMask.GetMask("ZombieBody");
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

            // Query the zombie registry, not a bounded physics buffer filled by the
            // bus, loose wood and scenery. Snapshot before impact damage can disable a zombie.
            nearbyZombieScratch.Clear();
            foreach (ZombieAI zombie in ZombieAI.ActiveZombies)
                if (zombie != null && scanBounds.SqrDistance(zombie.transform.position) <= 9f)
                    nearbyZombieScratch.Add(zombie);

            ignoredZombieCleanup.Clear();
            foreach (Collider ignored in ignoredZombieColliders.Keys)
                ignoredZombieCleanup.Add(ignored);


            Vector3 yieldVelocity = GetVehicleYieldVelocity();
            float actualSpeed =
                Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;

            foreach (ZombieAI zombie in nearbyZombieScratch)
            {
                if (zombie == null)
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

                // A zombie that is already climbing through a window or standing inside
                // the bus must follow the vehicle, not be treated as an impact victim.
                if (zombie.IsBoardingOrOnVehicle(transform) ||
                    zombie.TryBeginMovingVehicleBoarding(transform))
                    continue;

                // Parked bus: do not stop navigation. This is what lets zombies approach
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

            // Zombie collision pairs are intentionally isolated before Unity resolves them,
            // so trigger the impact sound here as well. VehicleAudio has its own short cooldown.
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
            Vector3 velocity = body != null
                ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up)
                : Vector3.zero;
            float accelerationIntent = GetAccelerationIntent();

            // On the first frame of pulling away the Rigidbody can still report ~0 m/s.
            // Give the zombie AI a small direction hint so the bus does not have to build
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
            if (body == null || body.isKinematic)
                return;

            Vector3 tiltCorrection =
                Vector3.Cross(transform.up, Vector3.up) *
                uprightStrength;

            Vector3 tippingVelocity =
                Vector3.ProjectOnPlane(
                    body.angularVelocity,
                    transform.up);

            body.AddTorque(
                tiltCorrection -
                tippingVelocity * uprightDamping,
                ForceMode.Acceleration);
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
                promptCanvas =
                    RuntimeMenuUI.CreateCanvas(
                        "Bus Prompt",
                        850);

                RectTransform panel =
                    RuntimeMenuUI.Block(
                        "Prompt",
                        promptCanvas.transform,
                        new Color(.025f, .04f, .055f, .9f));

                panel.anchorMin =
                    new Vector2(.25f, .04f);

                panel.anchorMax =
                    new Vector2(.75f, .105f);

                panel.offsetMin =
                    panel.offsetMax =
                    Vector2.zero;

                promptText =
                    RuntimeMenuUI.Label(
                        "Text",
                        panel,
                        string.Empty,
                        17,
                        TextAnchor.MiddleCenter,
                        RuntimeMenuUI.White);

                RuntimeMenuUI.Stretch(
                    promptText.rectTransform,
                    12,
                    12,
                    4,
                    4);
            }

            promptCanvas.gameObject.SetActive(true);
            promptText.text = message;
        }

        void HidePrompt()
        {
            if (promptCanvas != null)
                promptCanvas.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            RestoreZombiePhysicsPairs();

            if (driver != null)
                EndLocalDriving(false);
        }

        public override void OnDestroy()
        {
            RestoreZombiePhysicsPairs();

            if (promptCanvas != null)
                Destroy(promptCanvas.gameObject);

            base.OnDestroy();
        }
    }
}
