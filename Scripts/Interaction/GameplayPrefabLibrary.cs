using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Gameplay Prefab Library")]
    public sealed class GameplayPrefabLibrary:ScriptableObject
    {
        [System.Serializable] public struct Entry { public string use; public GameObject prefab; }
        public Entry[] prefabs;
    }
}
