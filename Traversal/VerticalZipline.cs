using Unity.FPS.Gameplay;
using UnityEngine;

namespace ZombieTown.Traversal
{
    /// <summary>
    /// A two-way vertical ascender. Only its endpoint volumes are interactive;
    /// the cable itself deliberately does not block players or projectiles.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VerticalZipline : MonoBehaviour
    {
        [SerializeField] Transform bottomMount;
        [SerializeField] Transform topMount;
        [SerializeField] Transform bottomExit;
        [SerializeField] Transform topExit;
        [SerializeField] BoxCollider bottomTrigger;
        [SerializeField] BoxCollider topTrigger;
        [SerializeField, Min(2f)] float rideSpeed = 12f;
        [SerializeField, Min(1f)] float interactionDistance = 3.2f;

        public Vector3 BottomMountPoint => bottomMount != null ? bottomMount.position : transform.position;
        public Vector3 TopMountPoint => topMount != null ? topMount.position : transform.position + Vector3.up * 10f;
        public Vector3 BottomExitPoint => bottomExit != null ? bottomExit.position : BottomMountPoint;
        public Vector3 TopExitPoint => topExit != null ? topExit.position : TopMountPoint;
        public float RideSpeed => rideSpeed;
        public float TravelDistance => Mathf.Max(.1f, Vector3.Distance(BottomMountPoint, TopMountPoint));

        void Awake() => EnsureEndpointTriggers();

        public void Initialize(Transform bottom, Transform top, Transform lowerExit, Transform upperExit,
            float speed = 12f)
        {
            bottomMount = bottom;
            topMount = top;
            bottomExit = lowerExit;
            topExit = upperExit;
            rideSpeed = Mathf.Max(2f, speed);
            EnsureEndpointTriggers();
        }

        public bool CanStartRide(Vector3 playerPosition, out bool travelUp)
        {
            float bottomDistance = Vector3.Distance(playerPosition, BottomExitPoint);
            float topDistance = Vector3.Distance(playerPosition, TopExitPoint);
            travelUp = bottomDistance <= topDistance;
            return Mathf.Min(bottomDistance, topDistance) <= interactionDistance;
        }

        public Vector3 GetRidePoint(float progress) =>
            Vector3.Lerp(BottomMountPoint, TopMountPoint, Mathf.Clamp01(progress));

        public Vector3 GetExitPoint(bool atTop) => atTop ? TopExitPoint : BottomExitPoint;

        void OnTriggerEnter(Collider other) => RegisterPlayer(other, true);
        void OnTriggerStay(Collider other) => RegisterPlayer(other, true);
        void OnTriggerExit(Collider other) => RegisterPlayer(other, false);

        void RegisterPlayer(Collider other, bool inside)
        {
            PlayerCharacterController player = other.GetComponentInParent<PlayerCharacterController>();
            if (player == null) return;
            PlayerZiplineRider rider = player.GetComponent<PlayerZiplineRider>() ??
                                        player.gameObject.AddComponent<PlayerZiplineRider>();
            rider.SetNearbyZipline(this, inside);
        }

        void EnsureEndpointTriggers()
        {
            if (bottomMount == null || topMount == null) return;

            if (bottomTrigger == null || bottomTrigger.gameObject != gameObject)
                bottomTrigger = gameObject.AddComponent<BoxCollider>();
            if (topTrigger == null || topTrigger.gameObject != gameObject || topTrigger == bottomTrigger)
                topTrigger = gameObject.AddComponent<BoxCollider>();

            ConfigureTrigger(bottomTrigger, BottomExitPoint);
            ConfigureTrigger(topTrigger, TopExitPoint);

            Rigidbody triggerBody = GetComponent<Rigidbody>();
            if (triggerBody == null) triggerBody = gameObject.AddComponent<Rigidbody>();
            triggerBody.isKinematic = true;
            triggerBody.useGravity = false;
            triggerBody.detectCollisions = true;
        }

        void ConfigureTrigger(BoxCollider trigger, Vector3 worldCenter)
        {
            trigger.isTrigger = true;
            trigger.center = transform.InverseTransformPoint(worldCenter + Vector3.up * .8f);
            Vector3 localSize = transform.InverseTransformVector(new Vector3(3.4f, 2.6f, 3.4f));
            trigger.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.1f, .85f, 1f, .9f);
            Gizmos.DrawLine(BottomMountPoint, TopMountPoint);
            Gizmos.DrawWireSphere(BottomExitPoint, .35f);
            Gizmos.DrawWireSphere(TopExitPoint, .35f);
        }
    }
}
