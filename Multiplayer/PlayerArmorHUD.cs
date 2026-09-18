using UnityEngine;
using UnityEngine.UI;
using Unity.FPS.Game;
using TMPro;

namespace ZombieTown.Multiplayer
{
    /// <summary>
    /// HUD-owned Guardian armor presenter. It intentionally lives on GameHUD,
    /// because the local network player can spawn after the scene UI.
    /// </summary>
    public sealed class PlayerArmorHUD : MonoBehaviour
    {
        [SerializeField] RectTransform armorRoot;
        [SerializeField] CanvasGroup mainCanvasGroup;
        [SerializeField] Image armorFillImage;
        [SerializeField] Text armorLabelText;
        [SerializeField] Text armorValueText;

        PlayerClassController player;
        TMP_Text pauseHint;
        TMP_Text pauseKey;

        void Awake()
        {
            SetVisible(false);
            foreach (TMP_Text candidate in GetComponentsInChildren<TMP_Text>(true))
            {
                if (candidate.text.Equals("Tab", System.StringComparison.OrdinalIgnoreCase))
                    pauseKey = candidate;
                else if (candidate.text.Contains("Pause/Options") || candidate.text.Contains("Pauze/Opties"))
                {
                    pauseHint = candidate;
                }
            }
        }

        void Update()
        {
            if (player == null || !player.IsSpawned || !player.IsOwner)
                player = FindOwningPlayer();

            if (pauseHint != null)
                pauseHint.text = GameLocalization.Text("Pause/Options", "Pauze/Opties");
            if (pauseKey != null)
                pauseKey.text = "ESC";

            if (player == null)
            {
                SetVisible(false);
                return;
            }

            ArchetypeTuning tuning = player.GetArchetype(player.SelectedClass.Value);
            bool visible = player.IsReady.Value && player.ArmorCapacity > 0f;
            SetVisible(visible);
            if (!visible) return;

            float ratio = Mathf.Clamp01(player.Armor.Value / player.ArmorCapacity);
            if (armorFillImage != null) armorFillImage.fillAmount = ratio;
            if (armorLabelText != null) armorLabelText.text = GameLocalization.Text("ARMOR", "PANTSER");
            if (armorValueText != null)
                armorValueText.text = $"{Mathf.CeilToInt(player.Armor.Value)} / {Mathf.CeilToInt(player.ArmorCapacity)}";
        }

        void SetVisible(bool visible)
        {
            if (armorRoot != null && armorRoot.gameObject.activeSelf != visible)
                armorRoot.gameObject.SetActive(visible);
            if (mainCanvasGroup != null)
            {
                mainCanvasGroup.alpha = visible ? 1f : 0f;
                mainCanvasGroup.blocksRaycasts = false;
                mainCanvasGroup.interactable = false;
            }
        }

        static PlayerClassController FindOwningPlayer()
        {
            foreach (PlayerClassController candidate in
                     FindObjectsByType<PlayerClassController>(FindObjectsInactive.Include))
                if (candidate.IsSpawned && candidate.IsOwner)
                    return candidate;
            return null;
        }
    }
}
