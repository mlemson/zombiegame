using UnityEngine;
namespace ZombieTown.Foundation
{
    [System.Flags] public enum EnemyRole { Basic=1, Fast=2, Tank=4, Ranged=8, Special=16, All=31 }
    [CreateAssetMenu(menuName="Zombie Town/Enemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public string enemyKey, displayName;
        public GameObject prefab;
        public EnemyRole role=EnemyRole.Basic;
        [Min(0)] public float baseWeight=1, baseHealthMultiplier=1;
        [Min(1)] public int minimumStage=1;
        [Tooltip("-1 uses the economy kill reward")] public int pointValue=-1;
    }
}
