using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.FPS.Game;
using ZombieTown.UI;

namespace Unity.FPS.UI
{
    public class HitMarkerUI : MonoBehaviour
    {
        [Tooltip("The image component of the hit marker")]
        public Image HitMarkerImage;
        [Tooltip("Duration of the hit marker flash")]
        public float FlashDuration = 0.15f;
        [Tooltip("Audio to play on hit")]
        public AudioClip HitSfx;
        [Tooltip("Impact played only for confirmed sword hits")]
        public AudioClip MeleeHitSfx;
        [Tooltip("Heavier impact played for the Striker 360-degree hit")]
        public AudioClip SpecialMeleeHitSfx;
        public Color NormalHitColor = Color.white;
        public Color HeadshotColor = new Color(1f, 0.25f, 0.2f, 1f);
        public Color LethalHitColor = new Color(1f, 0.05f, 0.05f, 1f);
        [Tooltip("POLYGON skull prefab rendered once into the compact kill confirmation icon.")]
        public GameObject KillSkullPrefab;

        private float m_LastFlashTime = -100f;
        private Color m_FlashColor = Color.white;
        private Vector3 m_BaseScale = Vector3.one;
        private float m_FlashScale = 1f;
        private float m_CurrentFlashDuration;
        private RectTransform m_KillIconRow;
        private Sprite m_SkullSprite;
        private Texture2D m_SkullTexture;
        private readonly List<KillIconFeedback> m_KillIcons = new();

        sealed class KillIconFeedback
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public float SpawnTime;
        }

        void Start()
        {
            if (HitMarkerImage != null)
            {
                HitMarkerImage.color = new Color(1, 1, 1, 0);
                m_BaseScale = HitMarkerImage.rectTransform.localScale;
            }

            EventManager.AddListener<DamageEvent>(OnDamage);
            EventManager.AddListener<MeleeDamageEvent>(OnMeleeDamage);
            CreateKillIconRow();
        }

        void CreateKillIconRow()
        {
            if (m_KillIconRow != null) return;
            Transform parent = HitMarkerImage != null && HitMarkerImage.canvas != null
                ? HitMarkerImage.canvas.transform : transform;
            GameObject row = new("Confirmed Kill Icons");
            row.transform.SetParent(parent, false);
            m_KillIconRow = row.AddComponent<RectTransform>();
            m_KillIconRow.anchorMin = m_KillIconRow.anchorMax = new Vector2(.5f, .5f);
            m_KillIconRow.pivot = new Vector2(.5f, .5f);
            m_KillIconRow.anchoredPosition = new Vector2(0f, -86f);
            m_KillIconRow.sizeDelta = new Vector2(360f, 32f);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 5f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            m_SkullSprite = CreateSkullSprite();
        }

        void AddKillIcon(bool headshot = false)
        {
            if (m_KillIconRow == null) CreateKillIconRow();
            if (m_KillIconRow == null || m_SkullSprite == null) return;
            while (m_KillIcons.Count >= 10) RemoveKillIcon(0);

            GameObject iconObject = new("Confirmed Kill Skull");
            iconObject.transform.SetParent(m_KillIconRow, false);
            RectTransform rect = iconObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(26f, 26f);
            rect.localScale = Vector3.one * .62f;
            LayoutElement layout = iconObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 26f;
            layout.preferredHeight = 26f;
            Image image = iconObject.AddComponent<Image>();
            image.sprite = m_SkullSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = headshot ? new Color(1f, .08f, .06f, 1f) : new Color(1f, .82f, .48f, 1f);
            Outline outline = iconObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, .9f);
            outline.effectDistance = new Vector2(2f, -2f);
            CanvasGroup group = iconObject.AddComponent<CanvasGroup>();
            m_KillIcons.Add(new KillIconFeedback { Rect = rect, Group = group, SpawnTime = Time.time });
        }

        Sprite CreateSkullSprite()
        {
            Sprite prefabSprite = PrefabIconSpriteRenderer.Render(KillSkullPrefab, out m_SkullTexture, 128);
            if (prefabSprite != null) return prefabSprite;

            const int size = 48;
            m_SkullTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Sniper Kill Skull",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32 clear = new(0, 0, 0, 0);
            Color32 solid = new(255, 255, 255, 255);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - 23.5f) / 15.5f;
                float dy = (y - 28f) / 16.5f;
                bool cranium = dx * dx + dy * dy <= 1f;
                bool jaw = y >= 6 && y <= 22 && x >= 15 + Mathf.Max(0, 12 - y) * .28f &&
                           x <= 32 - Mathf.Max(0, 12 - y) * .28f;
                bool eye = (new Vector2(x - 18.5f, y - 29f)).sqrMagnitude <= 17f ||
                           (new Vector2(x - 28.5f, y - 29f)).sqrMagnitude <= 17f;
                bool nose = y >= 18 && y <= 24 && Mathf.Abs(x - 23.5f) <= (24 - y) * .45f + .7f;
                bool toothGap = y >= 6 && y <= 14 && (Mathf.Abs(x - 19f) < .75f ||
                                Mathf.Abs(x - 23.5f) < .75f || Mathf.Abs(x - 28f) < .75f);
                pixels[y * size + x] = (cranium || jaw) && !eye && !nose && !toothGap ? solid : clear;
            }
            m_SkullTexture.SetPixels32(pixels);
            m_SkullTexture.Apply(false, true);
            return Sprite.Create(m_SkullTexture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), 48f);
        }

        void OnDamage(DamageEvent evt)
        {
            if (evt.Sender != null && evt.Sender.GetComponentInParent<Unity.FPS.Gameplay.PlayerCharacterController>() != null)
            {
                m_LastFlashTime = Time.time;
                m_FlashScale = 1f;
                m_CurrentFlashDuration = FlashDuration;
                m_FlashColor = evt.IsLethal ? LethalHitColor :
                    (evt.IsHeadshot ? HeadshotColor : NormalHitColor);
                if (evt.IsLethal) AddKillIcon(evt.IsHeadshot);
                
                if (HitSfx != null)
                {
                    AudioUtility.CreateSFX(HitSfx, transform.position, AudioUtility.AudioGroups.DamageTick, 0f);
                }
            }
        }

        void OnMeleeDamage(MeleeDamageEvent evt)
        {
            if (evt.Sender == null ||
                evt.Sender.GetComponentInParent<Unity.FPS.Gameplay.PlayerCharacterController>() == null)
                return;

            m_LastFlashTime = Time.time;
            m_FlashScale = evt.IsSpecial ? 2.35f : 1.65f;
            m_CurrentFlashDuration = evt.IsSpecial ? Mathf.Max(FlashDuration, .34f) :
                Mathf.Max(FlashDuration, .24f);
            m_FlashColor = evt.IsLethal ? LethalHitColor :
                (evt.IsSpecial ? new Color(1f, .72f, .18f, 1f) : NormalHitColor);
            for (int i = 0; i < Mathf.Max(evt.IsLethal ? 1 : 0, evt.KillCount); i++) AddKillIcon();

            AudioClip clip = evt.IsSpecial && SpecialMeleeHitSfx != null
                ? SpecialMeleeHitSfx : MeleeHitSfx;
            if (clip != null)
                AudioUtility.CreateSFX(clip, transform.position, AudioUtility.AudioGroups.Impact,
                    0f, 1f, evt.IsSpecial ? .5f : .32f, evt.IsSpecial ? .82f : 1.35f);
        }

        void Update()
        {
            if (HitMarkerImage != null)
            {
                float duration = Mathf.Max(.01f, m_CurrentFlashDuration > 0f ? m_CurrentFlashDuration : FlashDuration);
                float alpha = Mathf.Clamp01(1f - (Time.time - m_LastFlashTime) / duration);
                HitMarkerImage.color = new Color(m_FlashColor.r, m_FlashColor.g, m_FlashColor.b, alpha);
                float progress = 1f - alpha;
                float punch = 1f + Mathf.Sin(progress * Mathf.PI) * .18f;
                HitMarkerImage.rectTransform.localScale = m_BaseScale * Mathf.Lerp(1f, m_FlashScale * punch, alpha);
            }

            for (int i = m_KillIcons.Count - 1; i >= 0; i--)
            {
                KillIconFeedback icon = m_KillIcons[i];
                if (icon == null || icon.Group == null || icon.Rect == null) { m_KillIcons.RemoveAt(i); continue; }
                float age = Time.time - icon.SpawnTime;
                if (age >= 1.25f) { RemoveKillIcon(i); continue; }
                icon.Group.alpha = age <= .55f ? 1f : Mathf.Clamp01(1f - (age - .55f) / .7f);
                float pop = Mathf.Clamp01(age / .13f);
                icon.Rect.localScale = Vector3.one * Mathf.Lerp(.62f, 1f + Mathf.Sin(pop * Mathf.PI) * .16f, pop);
            }
        }

        void RemoveKillIcon(int index)
        {
            if (index < 0 || index >= m_KillIcons.Count) return;
            KillIconFeedback icon = m_KillIcons[index];
            m_KillIcons.RemoveAt(index);
            if (icon?.Group != null) Destroy(icon.Group.gameObject);
        }

        private void OnDestroy()
        {
            EventManager.RemoveListener<DamageEvent>(OnDamage);
            EventManager.RemoveListener<MeleeDamageEvent>(OnMeleeDamage);
            if (m_SkullSprite != null) Destroy(m_SkullSprite);
            if (m_SkullTexture != null) Destroy(m_SkullTexture);
        }
    }
}
