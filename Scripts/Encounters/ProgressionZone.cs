using UnityEngine;
namespace ZombieTown.Foundation
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ProgressionZone : MonoBehaviour
    {
        public string zoneId;
        [Min(1)] public int stage=1;
        public Transform defenseTarget;
        void OnDrawGizmosSelected() { Gizmos.color=Color.cyan; var box=GetComponent<BoxCollider>(); Gizmos.matrix=transform.localToWorldMatrix; Gizmos.DrawWireCube(box.center,box.size); }
    }
}
