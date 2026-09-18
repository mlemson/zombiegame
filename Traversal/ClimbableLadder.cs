using Unity.AI.Navigation;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieTown.Traversal
{
    /// <summary>
    /// Reusable ladder volume for local players. It can also author a bidirectional
    /// NavMeshLink so ZombieAI uses its existing manual climb traversal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClimbableLadder : MonoBehaviour
    {
        [SerializeField] BoxCollider climbTrigger;
        [SerializeField] bool createZombieLink = true;
        [SerializeField, Min(.5f)] float climbSpeed = 3.2f;
        [SerializeField, Min(.1f)] float entryDepth = .85f;
        [SerializeField, Min(.1f)] float exitDepth = .75f;
        [SerializeField, Min(.4f)] float navigationWidth = .85f;
        [Header("Optional authored exits (prefab-local references)")]
        [SerializeField] Transform bottomExit;
        [SerializeField] Transform topExit;

        Bounds localVisualBounds;
        bool hasBounds;
        Vector3 localClimbAxis = Vector3.up;
        Vector3 localFacingAxis = Vector3.forward;

        const float PlayerEntryDistance = 2.35f;
        const float PlayerEntryHeightTolerance = 1.65f;
        const float PlayerEntryFacingDot = .3f;

        public float ClimbSpeed => climbSpeed;
        bool HasAuthoredExits => bottomExit != null && topExit != null;
        public Vector3 FacingNormal => Vector3.ProjectOnPlane(HasAuthoredExits
            ? topExit.position - bottomExit.position
            : transform.TransformDirection(localFacingAxis), Vector3.up).normalized;
        public Vector3 BottomPoint => HasAuthoredExits ? bottomExit.position : transform.TransformPoint(LocalAxisPoint(false, .2f)) -
                                      FacingNormal * entryDepth;
        public Vector3 TopPoint => HasAuthoredExits ? topExit.position : transform.TransformPoint(LocalAxisPoint(true, .05f)) +
                                   FacingNormal * exitDepth;
        public Vector3 ClimbDirection => HasAuthoredExits ? transform.up : transform.TransformDirection(localClimbAxis).normalized;

        public void ConfigureExits(Transform bottom, Transform top)
        {
            bottomExit = bottom;
            topExit = top;
        }

        public void RefreshNavigationLink()
        {
            if (createZombieLink) EnsureZombieLink();
        }

        void Awake()
        {
            AlignGeneratedVisual();
            CalculateLocalBounds();
            EnsureTrigger();
        }

        void Start()
        {
            if (createZombieLink) EnsureZombieLink();
        }

        public void Initialize(bool addZombieLink = true)
        {
            createZombieLink = addZombieLink;
            AlignGeneratedVisual();
            CalculateLocalBounds();
            EnsureTrigger();
        }

        void AlignGeneratedVisual()
        {
            Transform visual = transform.Find("Ladder Visual");
            if (visual == null) return;

            visual.gameObject.SetActive(true);
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            foreach (Renderer renderer in renderers) renderer.enabled = true;

            Bounds bounds = CalculateRendererBounds(renderers);
            if (bounds.center.sqrMagnitude > .000001f)
                visual.localPosition -= bounds.center;
        }

        Bounds CalculateRendererBounds(Renderer[] renderers)
        {
            bool initialized = false;
            Bounds combined = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds local = renderer.localBounds;
                Matrix4x4 toLadder = transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                Vector3 min = local.min;
                Vector3 max = local.max;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 point = toLadder.MultiplyPoint3x4(new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z));
                    if (!initialized)
                    {
                        combined = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else combined.Encapsulate(point);
                }
            }
            return initialized ? combined : new Bounds(Vector3.zero, Vector3.one);
        }

        public Vector3 ClosestClimbPoint(Vector3 worldPosition)
        {
            Vector3 axisPoint = ClosestAxisPoint(worldPosition);
            Vector3 normal = FacingNormal;
            if (normal.sqrMagnitude < .01f) return axisPoint;
            // Authored wall ladders have one exposed climbing face. Starting
            // from the roof must first move to that same face, otherwise the
            // CharacterController descends into the platform collider.
            if (HasAuthoredExits) return axisPoint - normal * .35f;
            float side = Vector3.Dot(worldPosition - axisPoint, normal) >= 0f ? 1f : -1f;
            return axisPoint + normal * (side * .35f);
        }

        Vector3 ClosestAxisPoint(Vector3 worldPosition)
        {
            Vector3 bottom = HasAuthoredExits ? BottomPoint + FacingNormal * entryDepth : transform.TransformPoint(LocalAxisPoint(false, .2f));
            Vector3 top = HasAuthoredExits ? TopPoint - FacingNormal * exitDepth : transform.TransformPoint(LocalAxisPoint(true, .05f));
            Vector3 axis = top - bottom;
            float t = axis.sqrMagnitude > .001f
                ? Mathf.Clamp01(Vector3.Dot(worldPosition - bottom, axis) / axis.sqrMagnitude)
                : 0f;
            return Vector3.Lerp(bottom, top, t);
        }

        public Vector3 FacingDirection(Vector3 worldPosition)
        {
            // Aim at the actual ladder axis from whichever side collision placed the
            // player. This remains correct when a ladder prefab has a reversed forward.
            Vector3 ladderAxisPoint = ClosestAxisPoint(worldPosition);
            Vector3 direction = ladderAxisPoint - worldPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f)
            {
                direction = -FacingNormal;
                direction.y = 0f;
            }
            return direction.normalized;
        }

        public bool CanPlayerStartClimbing(Vector3 playerPosition, Vector3 playerForward)
        {
            Vector3 normal = FacingNormal;
            if (normal.sqrMagnitude < .01f) return false;

            Vector3 bottomAxis = HasAuthoredExits ? BottomPoint : transform.TransformPoint(LocalAxisPoint(false, .2f));
            Vector3 topAxis = HasAuthoredExits ? TopPoint : transform.TransformPoint(LocalAxisPoint(true, .05f));
            Vector3 entryPoint = Mathf.Abs(playerPosition.y - bottomAxis.y) <=
                                 Mathf.Abs(playerPosition.y - topAxis.y) ? bottomAxis : topAxis;
            Vector3 entryOffset = playerPosition - entryPoint;
            Vector3 horizontalOffset = Vector3.ProjectOnPlane(entryOffset, Vector3.up);
            
            float entryDist = Mathf.Max(2.5f, PlayerEntryDistance);
            float entryHeight = Mathf.Max(3.0f, PlayerEntryHeightTolerance);
            if (horizontalOffset.sqrMagnitude > entryDist * entryDist ||
                Mathf.Abs(entryOffset.y) > entryHeight)
                return false;

            Vector3 horizontalRight = Vector3.Cross(Vector3.up, normal).normalized;
            float maximumLateralOffset = Mathf.Max(1.6f, navigationWidth * 1.5f);
            if (Mathf.Abs(Vector3.Dot(horizontalOffset, horizontalRight)) > maximumLateralOffset) return false;

            // Allow approaching either face of the ladder freely
            if (Mathf.Abs(Vector3.Dot(horizontalOffset, normal)) > entryDist) return false;

            Vector3 look = Vector3.ProjectOnPlane(playerForward, Vector3.up).normalized;
            Vector3 towardLadder = FacingDirection(playerPosition);
            // Relaxed dot check so prompt reliably shows up even when not looking strictly dead-center
            return look.sqrMagnitude > .01f && (Vector3.Dot(look, towardLadder) >= -0.25f || horizontalOffset.magnitude < 1.2f);
        }

        public Vector3 ExitDirection(bool atTop) => atTop ? FacingNormal : -FacingNormal;

        void OnTriggerEnter(Collider other) => RegisterPlayer(other, true);
        void OnTriggerStay(Collider other) => RegisterPlayer(other, true);
        void OnTriggerExit(Collider other) => RegisterPlayer(other, false);

        void RegisterPlayer(Collider other, bool inside)
        {
            PlayerCharacterController player = other.GetComponentInParent<PlayerCharacterController>();
            if (player == null) return;
            PlayerLadderClimber climber = player.GetComponent<PlayerLadderClimber>() ??
                                          player.gameObject.AddComponent<PlayerLadderClimber>();
            climber.SetNearbyLadder(this, inside);
        }

        void CalculateLocalBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                localVisualBounds = new Bounds(Vector3.up * 1.5f, new Vector3(1f, 3f, .35f));
                hasBounds = true;
                return;
            }

            bool initialized = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = new(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 local = transform.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else bounds.Encapsulate(local);
                }
            }
            localVisualBounds = bounds;
            hasBounds = initialized;
            CalculateClimbAxis();
        }

        Vector3 LocalAxisPoint(bool top, float inset)
        {
            Vector3 axis = localClimbAxis;
            float extent = Vector3.Dot(localVisualBounds.extents, Abs(axis));
            return localVisualBounds.center + axis * ((top ? extent : -extent) + (top ? -inset : inset));
        }

        void CalculateClimbAxis()
        {
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            Vector3 size = localVisualBounds.size;
            float bestScore = float.MinValue;
            Vector3 bestAxis = Vector3.up;
            foreach (Vector3 axis in axes)
            {
                Vector3 worldAxis = transform.TransformVector(axis).normalized;
                float extent = Vector3.Dot(size, Abs(axis));
                float score = extent * Mathf.Abs(worldAxis.y);
                if (score <= bestScore) continue;
                bestScore = score;
                bestAxis = worldAxis.y >= 0f ? axis : -axis;
            }
            localClimbAxis = bestAxis;

            // On this ladder asset the visible climbing face is aligned with the
            // other horizontal local axis. Selecting the thin bounds axis turned
            // every mounted player exactly 90 degrees away from the rungs.
            float widest = float.NegativeInfinity;
            Vector3 facingAxis = Vector3.forward;
            foreach (Vector3 axis in axes)
            {
                if (Mathf.Abs(Vector3.Dot(axis, localClimbAxis)) > .5f) continue;
                Vector3 worldAxis = transform.TransformDirection(axis).normalized;
                float horizontal = Vector3.ProjectOnPlane(worldAxis, Vector3.up).magnitude;
                if (horizontal < .5f) continue;
                float thickness = Vector3.Dot(size, Abs(axis));
                if (thickness <= widest) continue;
                widest = thickness;
                facingAxis = axis;
            }

            // Preserve the authored forward sign while using the corrected face.
            Vector3 facingWorld = transform.TransformDirection(facingAxis);
            if (Vector3.Dot(facingWorld, transform.forward) < 0f) facingAxis = -facingAxis;
            localFacingAxis = facingAxis;
        }

        static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        void EnsureTrigger()
        {
            if (!hasBounds) CalculateLocalBounds();
            if (climbTrigger == null || climbTrigger.gameObject != gameObject)
            {
                // Trigger messages are delivered to components on the collider object.
                // Older ladders put this collider on a child, so ClimbableLadder never
                // received OnTriggerEnter/Exit. Keep the trigger on this same GameObject.
                BoxCollider legacyTrigger = climbTrigger;
                if (legacyTrigger == null)
                    legacyTrigger = transform.Find("Player Climb Trigger")?.GetComponent<BoxCollider>();

                climbTrigger = null;
                foreach (BoxCollider candidate in GetComponents<BoxCollider>())
                {
                    if (!candidate.isTrigger) continue;
                    climbTrigger = candidate;
                    break;
                }
                if (climbTrigger == null) climbTrigger = gameObject.AddComponent<BoxCollider>();
                if (legacyTrigger != null && legacyTrigger != climbTrigger && legacyTrigger.isTrigger)
                    legacyTrigger.enabled = false;
            }
            climbTrigger.isTrigger = true;
            climbTrigger.center = localVisualBounds.center;
            climbTrigger.size = new Vector3(Mathf.Max(1.1f, localVisualBounds.size.x + .65f),
                Mathf.Max(1.5f, localVisualBounds.size.y + .4f), Mathf.Max(1.15f, localVisualBounds.size.z + 1f));

            // Guarantees trigger callbacks even when the player controller's collider
            // setup changes between local and network prefabs.
            Rigidbody triggerBody = GetComponent<Rigidbody>();
            if (triggerBody == null) triggerBody = gameObject.AddComponent<Rigidbody>();
            triggerBody.isKinematic = true;
            triggerBody.useGravity = false;
            triggerBody.detectCollisions = true;
        }

        void EnsureZombieLink()
        {
            NavMeshLink link = GetComponent<NavMeshLink>();

            if (link == null)
                link = gameObject.AddComponent<NavMeshLink>();

            link.startPoint = transform.InverseTransformPoint(BottomPoint);
            link.endPoint = transform.InverseTransformPoint(TopPoint);
            link.width = navigationWidth;
            link.bidirectional = true;
            link.costModifier = 3f;
            link.autoUpdate = true;

            if (!link.enabled)
                link.enabled = true;
            else
                link.UpdateLink();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ConfigureSceneLadders()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            // Manually duplicated ladder prefabs do not necessarily carry the
            // gameplay component. Promote every rendered ladder root once so all
            // authored ladders get the same prompt, trigger and navigation link.
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name.IndexOf("Ladder", System.StringComparison.OrdinalIgnoreCase) < 0 ||
                    candidate.name.IndexOf("Link", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidate.name.IndexOf("Visual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidate.GetComponentInChildren<Renderer>(true) == null ||
                    candidate.GetComponentInParent<ClimbableLadder>(true) != null ||
                    candidate.GetComponentInChildren<ClimbableLadder>(true) != null)
                    continue;

                candidate.gameObject.AddComponent<ClimbableLadder>();
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (ClimbableLadder ladder in root.GetComponentsInChildren<ClimbableLadder>(true))
            {
                // Rebuild serialized/legacy trigger references for every authored
                // ladder, including manually duplicated ladders with different names.
                ladder.Initialize(ladder.createZombieLink);
            }
        }
    }
}
