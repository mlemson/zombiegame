using UnityEngine;
namespace ZombieTown.Foundation
{
    [RequireComponent(typeof(ZombieTown.LevelTwo.LevelPlayerSpawn))]
    public sealed class PlayerSpawn : MonoBehaviour
    {
        public string spawnId;
        void OnDrawGizmosSelected() { Gizmos.color=Color.blue; Gizmos.DrawWireSphere(transform.position,.4f); Gizmos.DrawRay(transform.position,transform.forward*1.5f); }
    }
}
