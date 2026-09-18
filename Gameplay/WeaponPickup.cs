using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public class WeaponPickup : Pickup
    {
        public static readonly System.Collections.Generic.List<WeaponPickup> Active = new();
        void OnEnable() { if(!Active.Contains(this))Active.Add(this); }
        void OnDisable() => Active.Remove(this);
        [Tooltip("The prefab for the weapon that will be added to the player on pickup")]
        public WeaponController WeaponPrefab;

        protected override void Start()
        {
            base.Start();
            HideFirstPersonHandsFromPickupVisual();

            // Set all children layers to default (to prefent seeing weapons through meshes)
            foreach (Transform t in GetComponentsInChildren<Transform>())
            {
                if (t != transform)
                    t.gameObject.layer = 0;
            }
        }

        public void HideFirstPersonHandsFromPickupVisual()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                string visualName = renderer.gameObject.name.ToLowerInvariant();
                if (!visualName.Contains("hands") && !visualName.Contains("arms")) continue;
                renderer.enabled = false;
            }
        }

#if UNITY_EDITOR
        void OnValidate() => HideFirstPersonHandsFromPickupVisual();
#endif

        protected override bool TryPick(PlayerCharacterController byPlayer)
        {
            // Weapon displays are shop terminals. Walking through one must never
            // grant or destroy it; purchases create a separate inventory copy.
            return false;
        }
    }
}
