using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class PlayerHealthBar : MonoBehaviour
    {
        [Tooltip("Image component dispplaying current health")]
        public Image HealthFillImage;

        Health m_PlayerHealth;
        bool m_Initialized;

        void Start()
        {
            TryInitialize();
        }

        void Update()
        {
            if (!m_Initialized)
            {
                TryInitialize();
                if (!m_Initialized)
                    return;
            }

            // update health bar value
            HealthFillImage.fillAmount = m_PlayerHealth.CurrentHealth / m_PlayerHealth.MaxHealth;
        }

        void TryInitialize()
        {
            if (m_Initialized) return;

            PlayerCharacterController playerCharacterController =
                FindAnyObjectByType<PlayerCharacterController>();
            if (playerCharacterController == null)
                return;

            m_PlayerHealth = playerCharacterController.GetComponent<Health>();
            if (m_PlayerHealth == null)
                return;

            m_Initialized = true;
        }
    }
}