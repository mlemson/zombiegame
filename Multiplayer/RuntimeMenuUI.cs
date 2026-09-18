using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ZombieTown.Multiplayer
{
    public static class RuntimeMenuUI
    {
        public static readonly Color Background = new(0.012f, 0.022f, 0.031f, .985f);
        public static readonly Color Panel = new(0.035f, 0.055f, 0.067f, .98f);
        public static readonly Color Card = new(0.065f, 0.09f, 0.105f, 1f);
        public static readonly Color Accent = new(1f, .56f, .16f, 1f);
        public static readonly Color Green = new(.18f, .72f, .51f, 1f);
        public static readonly Color White = new(.94f, .955f, .96f, 1f);
        public static readonly Color Muted = new(.57f, .64f, .67f, 1f);
        static Font font;

        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            EnsureEventSystem();
            return canvas;
        }

        public static RectTransform Block(string name, Transform parent, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<RectTransform>();
        }

        public static RectTransform CardBlock(string name, Transform parent, Color color)
        {
            RectTransform rect = Block(name, parent, color);
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, .075f);
            outline.effectDistance = new Vector2(1f, -1f);
            Shadow shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, .42f);
            shadow.effectDistance = new Vector2(0f, -8f);
            return rect;
        }

        public static Text Label(string name, Transform parent, string value, int size, TextAnchor alignment, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button Button(string name, Transform parent, string value, Color color, int fontSize = 24)
        {
            RectTransform rect = Block(name, parent, color);
            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, .91f, .78f, 1f);
            colors.pressedColor = new Color(.76f, .78f, .8f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(.35f, .38f, .4f, .7f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .12f;
            button.colors = colors;
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, .09f);
            outline.effectDistance = new Vector2(1f, -1f);
            Text label = Label("Label", rect, value, fontSize, TextAnchor.MiddleCenter, White);
            Stretch(label.rectTransform, 12, 12, 6, 6);
            return button;
        }

        public static InputField Input(Transform parent, string value, bool useAsPlaceholder = false)
        {
            RectTransform rect = Block("Input", parent, new Color(.025f, .04f, .055f, 1f));
            InputField input = rect.gameObject.AddComponent<InputField>();
            Text text = Label("Text", rect, useAsPlaceholder ? string.Empty : value, 22, TextAnchor.MiddleLeft, White);
            Stretch(text.rectTransform, 16, 16, 4, 4);
            input.textComponent = text;
            if (useAsPlaceholder)
            {
                Text placeholder = Label("Placeholder", rect, value, 22, TextAnchor.MiddleLeft,
                    new Color(.48f, .54f, .58f, 1f));
                Stretch(placeholder.rectTransform, 16, 16, 4, 4);
                input.placeholder = placeholder;
                input.text = string.Empty;
            }
            else input.text = value;
            return input;
        }

        public static Dropdown Dropdown(Transform parent, float height = 48f)
        {
            RectTransform rect = Block("Dropdown", parent, new Color(.025f, .04f, .055f, 1f));
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            Dropdown dropdown = rect.gameObject.AddComponent<Dropdown>();

            Text caption = Label("Label", rect, string.Empty, 17, TextAnchor.MiddleLeft, White);
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = new Vector2(16f, 3f);
            caption.rectTransform.offsetMax = new Vector2(-40f, -3f);
            Text arrow = Label("Arrow", rect, "▼", 14, TextAnchor.MiddleCenter, White);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
            arrow.rectTransform.anchorMax = Vector2.one;
            arrow.rectTransform.pivot = new Vector2(1f, .5f);
            arrow.rectTransform.sizeDelta = new Vector2(38f, 0f);

            RectTransform template = Block("Template", rect, new Color(.035f, .05f, .065f, .99f));
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -2f);
            template.sizeDelta = new Vector2(0f, 210f);
            ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = Block("Viewport", template, Color.white);
            Stretch(viewport);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject contentObject = new("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewport, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject itemObject = new("Item", typeof(RectTransform), typeof(Toggle), typeof(LayoutElement));
            itemObject.transform.SetParent(content, false);
            itemObject.GetComponent<LayoutElement>().preferredHeight = 32f;
            Text checkmark = Label("Item Checkmark", itemObject.transform, "✓", 14,
                TextAnchor.MiddleCenter, Accent);
            checkmark.rectTransform.anchorMin = Vector2.zero;
            checkmark.rectTransform.anchorMax = new Vector2(.14f, 1f);
            checkmark.rectTransform.offsetMin = checkmark.rectTransform.offsetMax = Vector2.zero;
            Text itemLabel = Label("Item Label", itemObject.transform, string.Empty, 15,
                TextAnchor.MiddleLeft, White);
            itemLabel.rectTransform.anchorMin = new Vector2(.14f, 0f);
            itemLabel.rectTransform.anchorMax = Vector2.one;
            itemLabel.rectTransform.offsetMin = new Vector2(4f, 0f);
            itemLabel.rectTransform.offsetMax = new Vector2(-4f, 0f);
            itemObject.GetComponent<Toggle>().graphic = checkmark;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            dropdown.targetGraphic = rect.GetComponent<Image>();
            dropdown.captionText = caption;
            dropdown.template = template;
            dropdown.itemText = itemLabel;
            template.gameObject.SetActive(false);
            return dropdown;
        }

        public static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static void EnsureEventSystem()
        {
            EventSystem[] systems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            foreach (EventSystem system in systems)
            {
                if (!system.gameObject.activeInHierarchy) continue;
                if (system.GetComponent<BaseInputModule>() == null)
                    system.gameObject.AddComponent<InputSystemUIInputModule>();
                return;
            }

            GameObject eventSystem = new("Runtime Menu EventSystem", typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            Object.DontDestroyOnLoad(eventSystem);
        }
    }
}
