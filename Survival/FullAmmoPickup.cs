using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using Unity.Netcode;
using ZombieTown.Multiplayer;

namespace ZombieTown.Survival
{
    public sealed class FullAmmoPickup : Pickup
    {
        protected override bool TryPick(PlayerCharacterController player)
        {
            PlayerClassController networkPlayer = player.GetComponent<PlayerClassController>();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
                NetworkManager.Singleton.IsServer && networkPlayer != null && networkPlayer.IsSpawned)
            {
                if (!networkPlayer.TryGrantNetworkAmmoPickup(false, 0, 0f, 1, 1, true))
                    return false;

                PlayPickupFeedback();
                DespawnOrDestroyPickup();
                return true;
            }

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