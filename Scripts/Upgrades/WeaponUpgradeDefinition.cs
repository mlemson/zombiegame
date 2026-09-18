using UnityEngine;
using Unity.FPS.Game;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Weapon Upgrade")]
    public sealed class WeaponUpgradeDefinition : ScriptableObject
    {
        [System.Serializable] public sealed class Tier
        {
            [Tooltip("-1 uses the EconomyProfile cost")] public int cost=-1;
            public float damageMultiplier=1.6f, attackDelayMultiplier=.8f, magazineMultiplier=1.35f, reserveMultiplier=1.35f, audioPitchOffset=.08f, audioVolumeOffset=.12f;
            public Material visualPreset;
            [ColorUsage(false,true)] public Color glowColor=new Color(.05f,.7f,1f);
            [Min(0)] public float glowIntensity=2f;
            public WeaponController replacementWeaponPrefab;
        }
        public string weaponKey;
        public Tier level1=new();
        public Tier level2=new() { damageMultiplier=2.4f, attackDelayMultiplier=.6f, magazineMultiplier=1.75f, reserveMultiplier=1.75f, audioPitchOffset=.16f, audioVolumeOffset=.22f, glowIntensity=5f, glowColor=new Color(.6f,.12f,1f) };
        public Tier Get(int level) => level==1 ? level1 : level==2 ? level2 : null;
    }
}
