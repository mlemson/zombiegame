using TMPro;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.UI;
using ZombieTown.UI;

namespace Unity.FPS.UI
{
    public class EdgeWarningIndicator : MonoBehaviour
    {
        const float DisplayDuration = 1.5f;
        const float IndicatorRadius = 225f;

        [Tooltip("POLYGON exclamation/warning prefab rendered into the fall-danger panel.")]
        public GameObject WarningIconPrefab;

        Canvas m_Canvas;
        CanvasGroup m_CanvasGroup;
        RectTransform m_Indicator;
        RectTransform m_Arrow;
        Texture2D m_WarningTexture;
        Sprite m_WarningSprite;
        float m_HideTime;

        void Awake()
        {
            m_Canvas = GetComponentInParent<Canvas>();
            EventManager.AddListener<EdgeWarningEvent>(OnEdgeWarning);
        }

        public void Configure(RectTransform notificationPanel)
        {
            if (notificationPanel != null)
                m_Canvas = notificationPanel.GetComponentInParent<Canvas>();
        }

        void Update()
        {
            if (m_CanvasGroup == null || Time.unscaledTime < m_HideTime) return;
            m_CanvasGroup.alpha = 0f;
        }

        void OnEdgeWarning(EdgeWarningEvent evt)
        {
            if (evt.DangerDirection.sqrMagnitude < .01f) return;
            EnsureIndicator();
            if (m_Indicator == null) return;

            Camera camera = Camera.main;
            if (camera == null)
                camera = FindAnyObjectByType<Camera>();
            if (camera == null) return;
            Vector3 localDirection = camera.transform.InverseTransformDirection(evt.DangerDirection.normalized);
            float angle = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            m_Indicator.anchoredPosition = Quaternion.Euler(0f, 0f, -angle) * Vector3.up * IndicatorRadius;
            m_Arrow.localRotation = Quaternion.Euler(0f, 0f, -angle);
            m_CanvasGroup.alpha = 1f;
            m_HideTime = Time.unscaledTime + DisplayDuration;
        }

        void EnsureIndicator()
        {
            if (m_Indicator != null) return;
            EnsureCanvas();
            if (m_Canvas == null) return;

            GameObject indicator = new("Edge Danger Indicator", typeof(RectTransform), typeof(CanvasGroup));
            indicator.transform.SetParent(m_Canvas.transform, false);
            m_Indicator = indicator.GetComponent<RectTransform>();
            m_Indicator.anchorMin = m_Indicator.anchorMax = new Vector2(.5f, .5f);
            m_Indicator.sizeDelta = new Vector2(300f, 78f);
            m_CanvasGroup = indicator.GetComponent<CanvasGroup>();
            m_CanvasGroup.alpha = 0f;

            Image backing = indicator.AddComponent<Image>();
            backing.color = new Color(.035f, .025f, .025f, .86f);
            backing.raycastTarget = false;
            Outline border = indicator.AddComponent<Outline>();
            border.effectColor = new Color(1f, .12f, .08f, .9f);
            border.effectDistance = new Vector2(2f, -2f);

            TextMeshProUGUI leftStripes = CreateSymbol("Left Hazard Stripes", indicator.transform, "/////", 34, new Color(1f, .22f, .13f));
            ConfigureStrip(leftStripes.rectTransform, -87f);
            TextMeshProUGUI rightStripes = CreateSymbol("Right Hazard Stripes", indicator.transform, "/////", 34, new Color(1f, .22f, .13f));
            ConfigureStrip(rightStripes.rectTransform, 87f);

            m_WarningSprite = PrefabIconSpriteRenderer.Render(WarningIconPrefab, out m_WarningTexture, 128);
            if (m_WarningSprite != null)
            {
                GameObject iconObject = new("Fall Warning Icon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(indicator.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, .5f);
                iconRect.sizeDelta = new Vector2(52f, 52f);
                Image icon = iconObject.GetComponent<Image>();
                icon.sprite = m_WarningSprite;
                icon.preserveAspect = true;
                icon.color = new Color(1f, .22f, .13f);
                icon.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI danger = CreateSymbol("Fall Warning", indicator.transform, "!", 48, new Color(1f, .22f, .13f));
                danger.rectTransform.anchorMin = danger.rectTransform.anchorMax = new Vector2(.5f, .5f);
                danger.rectTransform.sizeDelta = new Vector2(54f, 54f);
            }

            TextMeshProUGUI arrow = CreateSymbol("Direction Arrow", indicator.transform, "▲", 34, new Color(1f, .22f, .13f));
            m_Arrow = arrow.rectTransform;
            m_Arrow.anchorMin = new Vector2(.5f, .5f);
            m_Arrow.anchorMax = new Vector2(.5f, .5f);
            m_Arrow.sizeDelta = new Vector2(44f, 44f);
            m_Arrow.anchoredPosition = new Vector2(0f, 54f);
        }

        void EnsureCanvas()
        {
            if (m_Canvas != null) return;
            GameObject canvasObject = new("Edge Warning Overlay", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            m_Canvas = canvasObject.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 1000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            canvasObject.GetComponent<GraphicRaycaster>().enabled = false;
        }

        static void ConfigureStrip(RectTransform rect, float x)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(104f, 52f);
            rect.anchoredPosition = new Vector2(x, 0f);
        }

        static TextMeshProUGUI CreateSymbol(string name, Transform parent, string value, float size, Color color)
        {
            GameObject symbol = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            symbol.transform.SetParent(parent, false);
            TextMeshProUGUI text = symbol.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
            return text;
        }

        void OnDestroy()
        {
            EventManager.RemoveListener<EdgeWarningEvent>(OnEdgeWarning);
            DestroyRuntimeObject(m_WarningSprite);
            DestroyRuntimeObject(m_WarningTexture);
        }

        static void DestroyRuntimeObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
