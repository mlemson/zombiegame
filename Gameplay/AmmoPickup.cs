using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public interface IAmmoPickupFilter
    {
        bool CanPickupAmmo { get; }
    }

    public class AmmoPickup : Pickup
    {
        [Tooltip("Weapon those bullets are for. If null, gives ammo to the active weapon.")]
        public WeaponController Weapon;

        [Tooltip("Number of bullets the player gets")]
        public int BulletCount = 30;

        [Tooltip("Calculate the amount from the damage and firing profile of the currently held weapon.")]
        public bool UseActiveWeaponBalance = true;

        [Tooltip("Approximate raw damage value contained in one generic ammo pickup.")]
        public float AmmoValueBudget = 204f;

        public int MinimumRounds = 2;
        public int MaximumRounds = 30;

        protected override bool TryPick(PlayerCharacterController byPlayer)
        {
            IAmmoPickupFilter filter = byPlayer.GetComponent<IAmmoPickupFilter>();
            if (filter != null && !filter.CanPickupAmmo)
                return false;

            PlayerWeaponsManager playerWeaponsManager = byPlayer.GetComponent<PlayerWeaponsManager>();
            if (playerWeaponsManager)
            {
                WeaponController targetWeapon = null;

                if (UseActiveWeaponBalance)
                {
                    targetWeapon = playerWeaponsManager.GetActiveWeapon();
                }
                else if (Weapon != null)
                {
                    targetWeapon = playerWeaponsManager.HasWeapon(Weapon);
                }
                else
                {
                    targetWeapon = playerWeaponsManager.GetActiveWeapon();
                    if (targetWeapon == null)
                    {
                        targetWeapon = playerWeaponsManager.GetFirstWeaponWithPhysicalBullets();
                    }
                }

                if (targetWeapon != null && targetWeapon.HasPhysicalBullets && !targetWeapon.IsMeleeWeapon)
                {
                    int bullets = UseActiveWeaponBalance
                        ? CalculateBalancedRoundCount(targetWeapon)
                        : BulletCount;
                    int ammoBefore = targetWeapon.GetCarriedPhysicalBullets();
                    targetWeapon.AddCarriablePhysicalBullets(bullets);
                    if (targetWeapon.HasPhysicalBullets &&
                        targetWeapon.GetCarriedPhysicalBullets() == ammoBefore)
                        return false;

                    AmmoPickupEvent evt = Events.AmmoPickupEvent;
                    evt.Weapon = targetWeapon;
                    EventManager.Broadcast(evt);

                    PlayPickupFeedback();
                    DespawnOrDestroyPickup();
                    return true;
                }
            }

            return false;
        }

        int CalculateBalancedRoundCount(WeaponController weapon)
        {
            float damagePerShot = 17f;
            ProjectileStandard projectile = weapon.ProjectilePrefab != null
                ? weapon.ProjectilePrefab.GetComponent<ProjectileStandard>()
                : null;
            if (projectile != null)
            {
                damagePerShot = Mathf.Max(1f, projectile.Damage) * Mathf.Max(1, weapon.BulletsPerShot);
                if (projectile.AreaOfDamage != null)
                    damagePerShot *= 2.5f;
            }

            int capacityLimit = Mathf.Max(MinimumRounds, Mathf.CeilToInt(weapon.AmmoCapacity * .25f));
            int calculated = Mathf.RoundToInt(Mathf.Max(1f, AmmoValueBudget) / damagePerShot);
            return Mathf.Clamp(calculated, Mathf.Max(1, MinimumRounds),
                Mathf.Max(MinimumRounds, Mathf.Min(MaximumRounds, capacityLimit)));
        }
    }
}
