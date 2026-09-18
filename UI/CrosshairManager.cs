using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace Unity.FPS.UI
{
    public class CrosshairManager : MonoBehaviour
    {
        public Image CrosshairImage;
        bool m_Initialized;
        public Sprite NullCrosshairSprite;
        public float CrosshairUpdateshrpness = 5f;

        PlayerWeaponsManager m_WeaponsManager;
        bool m_WasPointingAtEnemy;
        RectTransform m_CrosshairRectTransform;
        CrosshairData m_CrosshairDataDefault;
        CrosshairData m_CrosshairDataTarget;
        CrosshairData m_CurrentCrosshair;
        RectTransform[] m_ScopeBackdrop;
        RawImage m_ScopeMask;
        Texture2D m_ScopeTexture;
        WeaponController m_ScopedWeapon;
        Renderer[] m_ScopedRenderers = Array.Empty<Renderer>();
        bool[] m_ScopedRendererStates = Array.Empty<bool>();

        void Start()
        {
            TryInitialize();
        }

        bool TryInitialize()
        {
            PlayerWeaponsManager[] availableWeaponsManagers =
                FindObjectsByType<PlayerWeaponsManager>();
            foreach (PlayerWeaponsManager weaponsManager in availableWeaponsManagers)
            {
                NetworkObject networkObject = weaponsManager.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsOwner)
                {
                    m_WeaponsManager = weaponsManager;
                    break;
                }

                PlayerInputHandler inputHandler = weaponsManager.GetComponent<PlayerInputHandler>();
                if (inputHandler != null && inputHandler.GameplayInputEnabled)
                {
                    m_WeaponsManager = weaponsManager;
                    break;
                }
            }

            if (m_WeaponsManager == null && availableWeaponsManagers.Length > 0)
                m_WeaponsManager = availableWeaponsManagers[0];

            if (m_WeaponsManager == null)
                return false;

            OnWeaponChanged(m_WeaponsManager.GetActiveWeapon());

            m_WeaponsManager.OnSwitchedToWeapon += OnWeaponChanged;
            m_Initialized = true;
            return true;
        }

        void Update()
        {
            if (!m_Initialized && !TryInitialize()) return;
            if (m_WeaponsManager == null) { m_Initialized = false; return; }
            UpdateCrosshairPointingAtEnemy(false);
            m_WasPointingAtEnemy = m_WeaponsManager.IsPointingAtEnemy;
            UpdateSniperScope();
        }

        void UpdateSniperScope()
        {
            WeaponController weapon = m_WeaponsManager.GetActiveWeapon();
            bool scoped = weapon != null && !string.IsNullOrEmpty(weapon.WeaponName) && m_WeaponsManager.IsAiming &&
                          weapon.WeaponName.IndexOf("sniper", StringComparison.OrdinalIgnoreCase) >= 0;

            if (scoped && m_ScopedWeapon != weapon) EnterScope(weapon);
            else if (!scoped && m_ScopedWeapon != null) ExitScope();

            if (!scoped) return;
            EnsureScopeBackdrop();
            foreach (RectTransform panel in m_ScopeBackdrop) panel.gameObject.SetActive(true);
            m_ScopeMask.gameObject.SetActive(true);
            CrosshairImage.enabled = false;
        }

        void EnterScope(WeaponController weapon)
        {
            ExitScope();
            m_ScopedWeapon = weapon;
            // Some legacy weapon prefabs contain visible presentation children beside
            // WeaponRoot. Scope mode must hide the complete held weapon, not only the
            // conventionally-authored subtree.
            m_ScopedRenderers = weapon.GetComponentsInChildren<Renderer>(true);
            m_ScopedRendererStates = new bool[m_ScopedRenderers.Length];
            for (int i = 0; i < m_ScopedRenderers.Length; i++)
            {
                m_ScopedRendererStates[i] = m_ScopedRenderers[i].enabled;
                m_ScopedRenderers[i].enabled = false;
            }
        }

        void ExitScope()
        {
            for (int i = 0; i < m_ScopedRenderers.Length && i < m_ScopedRendererStates.Length; i++)
                if (m_ScopedRenderers[i] != null) m_ScopedRenderers[i].enabled = m_ScopedRendererStates[i];
            m_ScopedRenderers = Array.Empty<Renderer>();
            m_ScopedRendererStates = Array.Empty<bool>();
            m_ScopedWeapon = null;
            if (m_ScopeBackdrop != null)
                foreach (RectTransform panel in m_ScopeBackdrop) panel.gameObject.SetActive(false);
            if (m_ScopeMask != null) m_ScopeMask.gameObject.SetActive(false);
            if (m_Initialized && CrosshairImage != null)
            {
                CrosshairImage.enabled = m_WeaponsManager != null && m_WeaponsManager.GetActiveWeapon() != null;
                UpdateCrosshairPointingAtEnemy(true);
            }
        }

        void EnsureScopeBackdrop()
        {
            if (m_ScopeBackdrop != null) return;
            Transform parent = CrosshairImage.canvas.transform;
            m_ScopeBackdrop = new RectTransform[4];
            for (int i = 0; i < m_ScopeBackdrop.Length; i++)
            {
                GameObject go = new($"Scope Backdrop {i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                go.transform.SetSiblingIndex(Mathf.Max(0, CrosshairImage.transform.GetSiblingIndex()));
                Image image = go.GetComponent<Image>();
                image.color = Color.black;
                image.raycastTarget = false;
                m_ScopeBackdrop[i] = go.GetComponent<RectTransform>();
            }

            GameObject mask = new("Sniper Scope Mask", typeof(RectTransform), typeof(RawImage));
            mask.transform.SetParent(parent, false);
            mask.transform.SetSiblingIndex(CrosshairImage.transform.GetSiblingIndex() + 1);
            m_ScopeMask = mask.GetComponent<RawImage>();
            m_ScopeMask.raycastTarget = false;
            m_ScopeTexture = CreateScopeTexture(1024);
            m_ScopeMask.texture = m_ScopeTexture;
            RectTransform maskRect = m_ScopeMask.rectTransform;
            maskRect.anchorMin = maskRect.anchorMax = new Vector2(.5f, .5f);
            maskRect.pivot = new Vector2(.5f, .5f);
            maskRect.anchoredPosition = Vector2.zero;
            maskRect.sizeDelta = Vector2.one * 900f;

            // A centered 900x900 viewing window; panels mask the rest on every aspect ratio.
            SetRect(m_ScopeBackdrop[0], new Vector2(0f, .5f), new Vector2(1f, 1f),
                new Vector2(0f, 450f), Vector2.zero);
            SetRect(m_ScopeBackdrop[1], Vector2.zero, new Vector2(1f, .5f),
                Vector2.zero, new Vector2(0f, -450f));
            SetRect(m_ScopeBackdrop[2], Vector2.zero, new Vector2(.5f, 1f),
                Vector2.zero, new Vector2(-450f, 0f));
            SetRect(m_ScopeBackdrop[3], new Vector2(.5f, 0f), Vector2.one,
                new Vector2(450f, 0f), Vector2.zero);
        }

        static Texture2D CreateScopeTexture(int size)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Sniper Scope",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * .5f;
            float radius = size * .475f;
            float innerRadius = radius - 4f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                Color32 color = distance > radius
                    ? new Color32(0, 0, 0, 255)
                    : distance > innerRadius
                        ? new Color32(18, 18, 18, 245)
                        : new Color32(0, 0, 0, 0);

                bool inReticle = distance < innerRadius - 10f;
                bool centerGap = Mathf.Abs(dx) < 17f && Mathf.Abs(dy) < 17f;
                if (inReticle && !centerGap && (Mathf.Abs(dx) <= 1.5f || Mathf.Abs(dy) <= 1.5f))
                    color = new Color32(185, 20, 20, 220);
                else if (distance <= 3f)
                    color = new Color32(220, 30, 30, 255);
                pixels[y * size + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        void UpdateCrosshairPointingAtEnemy(bool force)
        {
            if (m_CrosshairDataDefault.CrosshairSprite == null)
                return;

            if ((force || !m_WasPointingAtEnemy) && m_WeaponsManager.IsPointingAtEnemy)
            {
                m_CurrentCrosshair = m_CrosshairDataTarget;
                CrosshairImage.sprite = m_CurrentCrosshair.CrosshairSprite;
                m_CrosshairRectTransform.sizeDelta = m_CurrentCrosshair.CrosshairSize * Vector2.one;
            }
            else if ((force || m_WasPointingAtEnemy) && !m_WeaponsManager.IsPointingAtEnemy)
            {
                m_CurrentCrosshair = m_CrosshairDataDefault;
                CrosshairImage.sprite = m_CurrentCrosshair.CrosshairSprite;
                m_CrosshairRectTransform.sizeDelta = m_CurrentCrosshair.CrosshairSize * Vector2.one;
            }

            CrosshairImage.color = Color.Lerp(CrosshairImage.color, m_CurrentCrosshair.CrosshairColor,
                Time.deltaTime * CrosshairUpdateshrpness);

            m_CrosshairRectTransform.sizeDelta = Mathf.Lerp(m_CrosshairRectTransform.sizeDelta.x,
                m_CurrentCrosshair.CrosshairSize,
                Time.deltaTime * CrosshairUpdateshrpness) * Vector2.one;
        }

        void OnWeaponChanged(WeaponController newWeapon)
        {
            if (newWeapon)
            {
                CrosshairImage.enabled = true;
                m_CrosshairDataDefault = newWeapon.CrosshairDataDefault;
                m_CrosshairDataTarget = newWeapon.CrosshairDataTargetInSight;
                m_CrosshairRectTransform = CrosshairImage.GetComponent<RectTransform>();
                DebugUtility.HandleErrorIfNullGetComponent<RectTransform, CrosshairManager>(m_CrosshairRectTransform,
                    this, CrosshairImage.gameObject);
            }
            else
            {
                if (NullCrosshairSprite)
                {
                    CrosshairImage.sprite = NullCrosshairSprite;
                }
                else
                {
                    CrosshairImage.enabled = false;
                }
            }

            UpdateCrosshairPointingAtEnemy(true);
        }

        void OnDestroy()
        {
            ExitScope();
            if (m_ScopeTexture != null) Destroy(m_ScopeTexture);
            if (m_WeaponsManager != null) m_WeaponsManager.OnSwitchedToWeapon -= OnWeaponChanged;
        }
    }
}
