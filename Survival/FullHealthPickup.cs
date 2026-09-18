using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;

namespace ZombieTown.Survival
{
    public sealed class FullHealthPickup : Pickup
    {
        protected override bool TryPick(PlayerCharacterController player)
        {
            Health health = player.GetComponent<Health>();
            if (health == null || !health.CanPickup()) return false;
            health.Heal(health.MaxHealth);
            PlayPickupFeedback();
            DespawnOrDestroyPickup();
            return true;
        }
    }
}