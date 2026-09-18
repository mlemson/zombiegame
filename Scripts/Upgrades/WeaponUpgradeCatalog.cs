using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Weapon Upgrade Catalog")]
    public sealed class WeaponUpgradeCatalog : ScriptableObject
    {
        public WeaponUpgradeDefinition[] entries=System.Array.Empty<WeaponUpgradeDefinition>();
        public WeaponUpgradeDefinition Find(string key) { foreach(var e in entries) if(e != null && e.weaponKey==key) return e; return null; }
    }
}
