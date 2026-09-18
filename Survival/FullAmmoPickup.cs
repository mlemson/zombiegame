using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;

namespace ZombieTown.Survival
{
    public sealed class FullAmmoPickup : Pickup
    {
        protected override bool TryPick(PlayerCharacterController player)
        {
            PlayerWeaponsManager weapons = player.GetComponent<PlayerWeaponsManager>();
            WeaponController weapon = weapons != null ? weapons.GetActiveWeapon() : null;
            if (weapon == null || weapon.IsMeleeWeapon || !weapon.HasPhysicalBullets ||
                weapon.GetCarriedPhysicalBullets() >= weapon.AmmoCapacity)
                return false;

            weapon.AddCarriablePhysicalBullets(weapon.AmmoCapacity);
            AmmoPickupEvent evt = Events.AmmoPickupEvent;
            evt.Weapon = weapon;
            EventManager.Broadcast(evt);
            PlayPickupFeedback();
            DespawnOrDestroyPickup();
            return true;
        }
    }
}