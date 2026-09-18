using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Game Balance Database")]
    public sealed class GameBalanceDatabase : ScriptableObject
    {
        public PlayerCountScalingProfile playerScaling;
        public EconomyProfile economy;
        public DefenseSupplyProfile defenseSupplies;
        public EnemyCatalog enemies;
        public WeaponCatalog weapons;
        public WeaponUpgradeCatalog upgrades;
        public Unity.FPS.Game.CrouchProfile crouch;
        [Tooltip("Extra zombie walking speed per campaign level, added to the level 1 baseline. Existing wave and enemy-type multipliers still apply.")]
        [Range(0,.15f)] public float zombieSpeedIncreasePerLevel=.06f;
    }
}
