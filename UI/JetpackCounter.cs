using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class JetpackCounter : MonoBehaviour
    {
        [Tooltip("Image component representing jetpack fuel")]
        public Image JetpackFillImage;

        [Tooltip("Canvas group that contains the whole UI for the jetack")]
        public CanvasGroup MainCanvasGroup;

        [Tooltip("Component to animate the color when empty or full")]
        public FillBarColorChange FillBarColorChange;

        Jetpack m_Jetpack;

        void Awake()
        {
            m_Jetpack = FindActiveJetpack();
            if (FillBarColorChange != null) FillBarColorChange.Initialize(1f, 0f);
        }

        void Update()
        {
            // A network scene can briefly have no local player, or only remote players.
            // The HUD should wait for its local Jetpack instead of throwing every frame.
            if (m_Jetpack == null || !m_Jetpack.isActiveAndEnabled)
                m_Jetpack = FindActiveJetpack();

            if (m_Jetpack == null)
            {
                if (MainCanvasGroup != null) MainCanvasGroup.gameObject.SetActive(false);
                return;
            }

            if (MainCanvasGroup != null) MainCanvasGroup.gameObject.SetActive(m_Jetpack.IsJetpackUnlocked);

            if (m_Jetpack.IsJetpackUnlocked)
            {
                if (JetpackFillImage != null) JetpackFillImage.fillAmount = m_Jetpack.CurrentFillRatio;
                if (FillBarColorChange != null) FillBarColorChange.UpdateVisual(m_Jetpack.CurrentFillRatio);
            }
        }

        static Jetpack FindActiveJetpack()
        {
            foreach (Jetpack jetpack in FindObjectsByType<Jetpack>(FindObjectsInactive.Exclude))
                if (jetpack.isActiveAndEnabled) return jetpack;
            return null;
        }
    }
}
