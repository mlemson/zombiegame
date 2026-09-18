using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Enemy Catalog")]
    public sealed class EnemyCatalog : ScriptableObject { public EnemyDefinition[] entries = System.Array.Empty<EnemyDefinition>(); }
}
