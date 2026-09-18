using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    [DefaultExecutionOrder(-1000)]
    public class PlayerLoadoutEnforcer : MonoBehaviour
    {
        public WeaponController StartingPistol;

        void Awake()
        {
            PlayerWeaponsManager weaponsManager = GetComponent<PlayerWeaponsManager>();
            if (weaponsManager == null || StartingPistol == null)
                return;

            weaponsManager.StartingWeapons = new List<WeaponController> { StartingPistol };
        }
    }
}
