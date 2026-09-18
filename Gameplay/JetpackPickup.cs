namespace Unity.FPS.Gameplay
{
    public class JetpackPickup : Pickup
    {
        protected override bool TryPick(PlayerCharacterController byPlayer)
        {
            var jetpack = byPlayer.GetComponent<Jetpack>();
            if (!jetpack)
                return false;

            if (jetpack.TryUnlock())
            {
                PlayPickupFeedback();
                Destroy(gameObject);
                return true;
            }

            return false;
        }
    }
}
