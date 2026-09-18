using UnityEngine;
using Unity.FPS.Game;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Weapon Catalog")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        public WeaponDefinition[] entries=System.Array.Empty<WeaponDefinition>();
        public WeaponDefinition Find(string key) { foreach(var e in entries) if(e != null && e.weaponKey==key) return e; return null; }
        public WeaponDefinition Find(WeaponController weapon) { if(weapon==null) return null; foreach(var e in entries) if(e != null && e.weaponPrefab != null && (e.weaponPrefab==weapon || e.weaponPrefab.gameObject==weapon.SourcePrefab)) return e; return null; }
    }
}
