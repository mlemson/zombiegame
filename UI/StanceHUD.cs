using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class StanceHUD : MonoBehaviour
    {
        [Tooltip("Image component for the stance sprites")]
        public Image StanceImage;

        [Tooltip("Sprite to display when standing")]
        public Sprite StandingSprite;

        [Tooltip("Sprite to display when crouching")]
        public Sprite CrouchingSprite;
        PlayerCharacterController m_Character;

        void Start()
        {
            TryBindCharacter();
        }

        void Update()
        {
            if (m_Character == null) TryBindCharacter();
        }

        void TryBindCharacter()
        {
            m_Character = FindAnyObjectByType<PlayerCharacterController>();
            if (m_Character == null) return;
            m_Character.OnStanceChanged += OnStanceChanged;
            OnStanceChanged(m_Character.IsCrouching);
        }

        void OnStanceChanged(bool crouched)
        {
            StanceImage.sprite = crouched ? CrouchingSprite : StandingSprite;
        }

        void OnDestroy()
        {
            if (m_Character != null) m_Character.OnStanceChanged -= OnStanceChanged;
        }
    }
}
