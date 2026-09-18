using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.AI
{
    public enum VehicleZombieAccessType
    {
        Door,
        Window
    }

    /// <summary>
    /// Explicit opening in a vehicle shell that zombies are allowed to use.
    ///
    /// Why this exists:
    /// the bus uses coarse physics colliders (for example a side collider) that can span
    /// across a visual doorway/window. Zombie/vehicle physics are intentionally isolated
    /// so zombies cannot push a multi-ton vehicle. ZombieAI therefore needs an authored
    /// "this part really is an opening" volume instead of trying to infer holes from those
    /// coarse colliders.
    ///
    /// Put this on an EMPTY CHILD of the bus/forklift and size the BoxCollider trigger so
    /// it passes from just outside to just inside the real opening.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VehicleZombieAccessZone : MonoBehaviour
    {
        public VehicleZombieAccessType AccessType = VehicleZombieAccessType.Door;

        [Tooltip("Normally auto-detected from the Rigidbody in the parent hierarchy.")]
        public Transform VehicleRoot;

        [Tooltip("How close a zombie may get before it starts targeting this opening.")]
        [Min(0.1f)]
        public float ApproachMargin = 0.55f;

        [Tooltip("How far beyond the opening volume the automatic outside/inside traversal points are placed.")]
        [Min(0.1f)]
        public float TraversalDepth = 0.45f;

        [Tooltip("Small stand-off before the opening so the approach target remains on reachable NavMesh.")]
        [Min(0.02f)]
        public float ApproachStandOff = 0.12f;

        [Tooltip("Local Y position of the walkable vehicle floor on the VehicleRoot.")]
        public float InsideFloorHeight = 0.84f;

        [Header("Boardable bus opening")]
        public bool UseClingingEntry;
        [Min(.1f)] public float JumpSeconds = .65f;
        [Min(.1f)] public float HangSeconds = 1.25f;
        [Min(.5f)] public float CrawlSeconds = 2.4f;
        [System.NonSerialized] public ZombieTown.Foundation.BarricadeWindow Barricade;
        ZombieAI occupant;
        public bool CanClaim(ZombieAI zombie) => occupant == null || !occupant.isActiveAndEnabled ||
            occupant.GetComponent<Unity.FPS.Game.Health>().CurrentHealth <= 0 || occupant == zombie;
        public bool Claim(ZombieAI zombie) {
            if (!CanClaim(zombie) || (Barricade != null && !Barricade.ClaimVehicle(zombie))) return false;
            occupant = zombie; return true;
        }
        public void Release(ZombieAI zombie) { if (occupant == zombie) occupant = null; Barricade?.ReleaseVehicle(zombie); }
        public bool IsBlocked => Barricade != null && !Barricade.IsBreached;

        public Vector3 GetLatchPosition(bool roof = false) {
            Vector3 center = transform.TransformPoint(Box.center);
            Vector3 position = center + GetOutsideDirection() * .48f;
            // Leave enough reach for both hands to grip the upper frame.
            position.y = Box.bounds.max.y - 2.1f;
            if(roof && VehicleRoot.TryGetComponent<ZombieTown.Foundation.BusDefenseLayout>(out var layout))position.y=VehicleRoot.TransformPoint(Vector3.up*layout.roofFloorHeight).y-2.1f;
            return position;
        }

        static readonly HashSet<VehicleZombieAccessZone> s_Active = new();

        BoxCollider m_Box;

        public static IEnumerable<VehicleZombieAccessZone> Active => s_Active;
        public BoxCollider Box => m_Box != null ? m_Box : (m_Box = GetComponent<BoxCollider>());
        public bool IsWindow => AccessType == VehicleZombieAccessType.Window;

        public bool IsVehicleStationary(float maximumSpeed = .55f)
        {
            if (VehicleRoot == null)
                return true;

            Rigidbody body = VehicleRoot.GetComponent<Rigidbody>();
            if (body == null)
                body = VehicleRoot.GetComponentInParent<Rigidbody>();

            return body == null ||
                   (body.linearVelocity.sqrMagnitude <= maximumSpeed * maximumSpeed &&
                    body.angularVelocity.sqrMagnitude <= .3f * .3f);
        }

        void Reset()
        {
            EnsureConfigured();
        }

        void Awake()
        {
            EnsureConfigured();
        }

        void OnValidate()
        {
            EnsureConfigured();
        }

        void OnEnable()
        {
            EnsureConfigured();
            s_Active.Add(this);
        }

        void OnDisable()
        {
            s_Active.Remove(this);
        }

        void EnsureConfigured()
        {
            m_Box = GetComponent<BoxCollider>();
            if (m_Box != null)
                m_Box.isTrigger = true;

            if (VehicleRoot == null)
            {
                Rigidbody parentBody = GetComponentInParent<Rigidbody>();
                if (parentBody != null)
                    VehicleRoot = parentBody.transform;
            }
        }

        public bool BelongsToVehicle(Collider vehicleCollider)
        {
            if (vehicleCollider == null)
                return false;

            Transform root = VehicleRoot;
            if (root == null)
            {
                Rigidbody body = vehicleCollider.attachedRigidbody;
                return body != null && transform.IsChildOf(body.transform);
            }

            Rigidbody attached = vehicleCollider.attachedRigidbody;
            if (attached != null && attached.transform == root)
                return true;

            return vehicleCollider.transform == root ||
                   vehicleCollider.transform.IsChildOf(root);
        }

        public float SqrDistance(Vector3 worldPoint)
        {
            BoxCollider box = Box;
            if (box == null || !box.enabled)
                return float.PositiveInfinity;

            return box.bounds.SqrDistance(worldPoint);
        }

        public bool ContainsOrNear(Vector3 worldPoint, float extraMargin)
        {
            float margin = Mathf.Max(0f, extraMargin);
            return SqrDistance(worldPoint) <= margin * margin;
        }

        public Vector3 GetApproachPoint(Vector3 from)
        {
            GetPortalGeometry(
                out Vector3 center,
                out Vector3 outsideDirection,
                out float halfDepth);

            float side = Mathf.Sign(Vector3.Dot(from - center, outsideDirection));
            if (Mathf.Abs(side) < .001f)
                side = 1f;

            Vector3 result =
                center +
                outsideDirection *
                side *
                (halfDepth + Mathf.Max(.02f, ApproachStandOff));

            result.y = from.y;
            return result;
        }

        public float SignedSide(Vector3 worldPoint)
        {
            GetPortalGeometry(
                out Vector3 center,
                out Vector3 outsideDirection,
                out _);

            return Vector3.Dot(worldPoint - center, outsideDirection);
        }

        public Vector3 GetOutsideDirection()
        {
            GetPortalGeometry(
                out _,
                out Vector3 outsideDirection,
                out _);

            return outsideDirection;
        }

        public void GetTraversalCandidates(
            out Vector3 outsideCandidate,
            out Vector3 insideCandidate)
        {
            GetPortalGeometry(
                out Vector3 center,
                out Vector3 outsideDirection,
                out float halfDepth);

            float depth = halfDepth + Mathf.Max(.1f, TraversalDepth);

            outsideCandidate = center + outsideDirection * depth;
            insideCandidate = center - outsideDirection * depth;

            // The outside point belongs to the street. The inside point follows the
            // vehicle-local floor so it remains valid after the vehicle has moved.
            float feetY = Box != null ? Box.bounds.min.y + .08f : center.y;
            outsideCandidate.y = feetY;
            if (VehicleRoot != null)
            {
                if (UseClingingEntry) {
                    Vector3 localOutside = VehicleRoot.InverseTransformPoint(outsideCandidate);
                    localOutside.y = .05f;
                    outsideCandidate = VehicleRoot.TransformPoint(localOutside);
                }
                Vector3 localInside = VehicleRoot.InverseTransformPoint(insideCandidate);
                localInside.y = InsideFloorHeight;
                insideCandidate = VehicleRoot.TransformPoint(localInside);
            }
            else
            {
                insideCandidate.y = feetY;
            }
        }

        void GetPortalGeometry(
            out Vector3 center,
            out Vector3 outsideDirection,
            out float halfDepth)
        {
            BoxCollider box = Box;

            center = box != null
                ? transform.TransformPoint(box.center)
                : transform.position;

            Vector3 worldX = transform.TransformVector(
                Vector3.right * (box != null ? box.size.x : 1f));
            Vector3 worldZ = transform.TransformVector(
                Vector3.forward * (box != null ? box.size.z : 1f));

            // For a doorway/window volume the thinnest horizontal axis normally crosses
            // the vehicle wall. This means the user only has to size/orient the box naturally.
            Vector3 crossingVector =
                worldX.magnitude <= worldZ.magnitude
                    ? worldX
                    : worldZ;

            halfDepth = Mathf.Max(.02f, crossingVector.magnitude * .5f);

            Vector3 crossingDirection = crossingVector.normalized;
            if (crossingDirection.sqrMagnitude < .001f)
                crossingDirection = transform.right;

            Vector3 awayFromVehicle = Vector3.zero;
            if (VehicleRoot != null)
            {
                awayFromVehicle = center - VehicleRoot.position;
                awayFromVehicle.y = 0f;
            }

            // Make the positive direction consistently point outward from the vehicle.
            if (awayFromVehicle.sqrMagnitude > .001f &&
                Vector3.Dot(crossingDirection, awayFromVehicle) < 0f)
            {
                crossingDirection = -crossingDirection;
            }

            outsideDirection = crossingDirection.normalized;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            BoxCollider box = Box;
            if (box == null)
                return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
#endif
    }
}
