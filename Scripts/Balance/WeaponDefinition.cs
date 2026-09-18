using UnityEngine;
using Unity.FPS.Game;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string weaponKey, displayName, category;
        public WeaponController weaponPrefab;
        public GameObject pickupPrefab;
        [Min(0)] public int basePurchaseCost=100;
    }
}
