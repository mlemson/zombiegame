using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Unity.FPS.UI
{
    public class WeaponHUDManager : MonoBehaviour
    {
        [Tooltip("UI panel containing the layoutGroup for displaying weapon ammo")]
        public RectTransform AmmoPanel;

        [Tooltip("Prefab for displaying weapon ammo")]
        public GameObject AmmoCounterPrefab;

        PlayerWeaponsManager m_PlayerWeaponsManager;
        List<AmmoCounter> m_AmmoCounters = new List<AmmoCounter>();
        bool m_Initialized;

        void Start()
        {
            TryInitialize();
        }

        void Update()
        {
            if (!m_Initialized)
                TryInitialize();
        }

        void TryInitialize()
        {
            if (m_Initialized) return;

            PlayerWeaponsManager[] weaponsManagers = FindObjectsByType<PlayerWeaponsManager>();
            foreach (PlayerWeaponsManager weaponsManager in weaponsManagers)
            {
                NetworkObject networkObject = weaponsManager.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsOwner)
                {
                    m_PlayerWeaponsManager = weaponsManager;
                    break;
                }

                PlayerInputHandler inputHandler = weaponsManager.GetComponent<PlayerInputHandler>();
                if (inputHandler != null && inputHandler.GameplayInputEnabled)
                {
                    m_PlayerWeaponsManager = weaponsManager;
                    break;
                }
            }

            if (m_PlayerWeaponsManager == null && weaponsManagers.Length > 0)
                m_PlayerWeaponsManager = weaponsManagers[0];

            if (m_PlayerWeaponsManager == null)
                return;

            WeaponController activeWeapon = m_PlayerWeaponsManager.GetActiveWeapon();
            if (activeWeapon)
            {
                AddWeapon(activeWeapon, m_PlayerWeaponsManager.ActiveWeaponIndex);
                ChangeWeapon(activeWeapon);
            }

            m_PlayerWeaponsManager.OnAddedWeapon += AddWeapon;
            m_PlayerWeaponsManager.OnRemovedWeapon += RemoveWeapon;
            m_PlayerWeaponsManager.OnSwitchedToWeapon += ChangeWeapon;
            m_Initialized = true;
        }

        void AddWeapon(WeaponController newWeapon, int weaponIndex)
        {
            GameObject ammoCounterInstance = Instantiate(AmmoCounterPrefab, AmmoPanel);
            AmmoCounter newAmmoCounter = ammoCounterInstance.GetComponent<AmmoCounter>();
            DebugUtility.HandleErrorIfNullGetComponent<AmmoCounter, WeaponHUDManager>(newAmmoCounter, this,
                ammoCounterInstance.gameObject);

            newAmmoCounter.Initialize(newWeapon, weaponIndex);

            m_AmmoCounters.Add(newAmmoCounter);
        }

        void RemoveWeapon(WeaponController newWeapon, int weaponIndex)
        {
            int foundCounterIndex = -1;
            for (int i = 0; i < m_AmmoCounters.Count; i++)
            {
                if (m_AmmoCounters[i].WeaponCounterIndex == weaponIndex)
                {
                    foundCounterIndex = i;
                    Destroy(m_AmmoCounters[i].gameObject);
                }
            }

            if (foundCounterIndex >= 0)
            {
                m_AmmoCounters.RemoveAt(foundCounterIndex);
            }
        }

        void ChangeWeapon(WeaponController weapon)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(AmmoPanel);
        }
    }
}