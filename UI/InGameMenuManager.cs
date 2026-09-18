using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

namespace Unity.FPS.UI
{
    public class InGameMenuManager : MonoBehaviour
    {
        public static bool ExternalPauseMenuOwnsInput { get; set; }

        [Tooltip("Root GameObject of the menu used to toggle its activation")]
        public GameObject MenuRoot;

        [Tooltip("Master volume when menu is open")] [Range(0.001f, 1f)]
        public float VolumeWhenMenuOpen = 0.5f;

        [Tooltip("Slider component for look sensitivity")]
        public Slider LookSensitivitySlider;

        [Tooltip("Toggle component for shadows")]
        public Toggle ShadowsToggle;

        [Tooltip("Toggle component for invincibility")]
        public Toggle InvincibilityToggle;

        [Tooltip("Toggle component for framerate display")]
        public Toggle FramerateToggle;

        [Tooltip("Optional. Created from the framerate row at runtime when not assigned.")]
        public Toggle FullscreenToggle;

        [Tooltip("Optional. Created at runtime and populated with common resolutions up to 4K.")]
        public Dropdown ResolutionDropdown;

        [Tooltip("Optional Dutch-language setting; English remains the default.")]
        public Toggle DutchLanguageToggle;

        [Tooltip("GameObject for the controls")]
        public GameObject ControlImage;

        PlayerInputHandler m_PlayerInputsHandler;
        Health m_PlayerHealth;
        FramerateCounter m_FramerateCounter;
        bool m_Initialized;
        
        private InputAction m_SubmitAction;
        private InputAction m_CancelAction;
        private InputAction m_NavigateAction;
        private InputAction m_MenuAction;
        readonly List<Vector2Int> m_ResolutionOptions = new();
        const string MainMenuScene = "IntroMenu";
        const string Level1Scene = "ZombieTownScene";
        const string Level2Scene = "RadioOutpostScene";
        const string Level3Scene = "HarborEvacuationScene";
        const string Level4Scene = "RetreatDefenseScene";
        bool m_IsTransitioningToNextLevel;

        void OnEnable()
        {
            Objective.OnObjectiveCompleted += OnObjectiveCompleted;
        }

        void OnDisable()
        {
            Objective.OnObjectiveCompleted -= OnObjectiveCompleted;
        }

        void Start()
        {
            TryInitialize();
            if (MenuRoot != null)
                MenuRoot.SetActive(false);
        }

        void Update()
        {
            if (!m_Initialized)
            {
                TryInitialize();
                if (!m_Initialized)
                    return;
            }

            // The runtime pause menu is the single owner of Escape, cursor state and
            // pause time. Keep this component alive for objective/level progression,
            // but do not let its legacy Tab menu compete for focus.
            if (ExternalPauseMenuOwnsInput)
            {
                if (MenuRoot != null && MenuRoot.activeSelf) MenuRoot.SetActive(false);
                return;
            }

            // A class-selection or lobby screen owns the cursor while gameplay input is
            // disabled. Ignoring Tab here prevents the pause menu from stealing focus,
            // freezing the preview animators and accidentally making the cards clickable.
            if (m_PlayerInputsHandler != null && !m_PlayerInputsHandler.GameplayInputEnabled)
            {
                if (MenuRoot != null && MenuRoot.activeSelf) MenuRoot.SetActive(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Time.timeScale = 1f;
                return;
            }

            if (MenuRoot != null && !MenuRoot.activeSelf && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (m_MenuAction != null && (m_MenuAction.WasPressedThisFrame()
                || (MenuRoot != null && MenuRoot.activeSelf && m_CancelAction != null && m_CancelAction.WasPressedThisFrame())))
            {
                if (ControlImage != null && ControlImage.activeSelf)
                {
                    ControlImage.SetActive(false);
                    return;
                }

                SetPauseMenuActivation(!(MenuRoot != null && MenuRoot.activeSelf));
            }

            if (m_NavigateAction != null && m_NavigateAction.ReadValue<Vector2>().y != 0)
            {
                if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
                {
                    if (EventSystem.current != null)
                    {
                        EventSystem.current.SetSelectedGameObject(null);
                    }
                    if (LookSensitivitySlider != null)
                        LookSensitivitySlider.Select();
                }
            }
        }

        void TryInitialize()
        {
            if (m_Initialized)
                return;

            if (m_PlayerInputsHandler == null)
            {
                m_PlayerInputsHandler = FindAnyObjectByType<PlayerInputHandler>();
                if (m_PlayerInputsHandler == null)
                    return;
            }

            if (m_PlayerHealth == null)
                m_PlayerHealth = m_PlayerInputsHandler.GetComponent<Health>();

            if (m_PlayerHealth == null)
                return;

            if (m_FramerateCounter == null)
                m_FramerateCounter = FindAnyObjectByType<FramerateCounter>();

            if (m_FramerateCounter == null)
                return;

            if (LookSensitivitySlider != null)
            {
                LookSensitivitySlider.value = m_PlayerInputsHandler.LookSensitivity;
                LookSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);
            }

            if (ShadowsToggle != null)
            {
                ShadowsToggle.isOn = QualitySettings.shadows != ShadowQuality.Disable;
                ShadowsToggle.onValueChanged.AddListener(OnShadowsChanged);
            }

            if (InvincibilityToggle != null)
            {
                InvincibilityToggle.isOn = m_PlayerHealth.Invincible;
                InvincibilityToggle.onValueChanged.AddListener(OnInvincibilityChanged);
            }

            if (FramerateToggle != null)
            {
                FramerateToggle.isOn = m_FramerateCounter.UIText.gameObject.activeSelf;
                FramerateToggle.onValueChanged.AddListener(OnFramerateCounterChanged);
            }

            EnsureDisplayOptions();
            PopulateResolutionOptions();

            if (ResolutionDropdown != null)
                ResolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            if (FullscreenToggle != null)
            {
                FullscreenToggle.isOn = Screen.fullScreen;
                FullscreenToggle.interactable = true;
                FullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
            }

            if (DutchLanguageToggle != null)
            {
                DutchLanguageToggle.isOn = GameLocalization.IsDutch;
                DutchLanguageToggle.interactable = true;
                DutchLanguageToggle.onValueChanged.AddListener(OnLanguageChanged);
            }

            GameLocalization.LanguageChanged += RefreshLocalizedLabels;
            RefreshLocalizedLabels();

            m_SubmitAction = InputSystem.actions.FindAction("UI/Submit");
            m_CancelAction = InputSystem.actions.FindAction("UI/Cancel");
            m_NavigateAction = InputSystem.actions.FindAction("UI/Navigate");
            m_MenuAction = InputSystem.actions.FindAction("UI/Menu");

            if (m_SubmitAction != null)
                m_SubmitAction.Enable();
            if (m_CancelAction != null)
                m_CancelAction.Enable();
            if (m_NavigateAction != null)
                m_NavigateAction.Enable();
            if (m_MenuAction != null)
                m_MenuAction.Enable();

            m_Initialized = true;
        }

        public void ClosePauseMenu()
        {
            SetPauseMenuActivation(false);
        }

        public void ReturnToMainMenu()
        {
            Time.timeScale = 1f;
            SetPauseMenuActivation(false);
            SceneLoadCoordinator.LoadScene(MainMenuScene);
        }

        public void PauseAndLoadLevel2()
        {
            if (m_IsTransitioningToNextLevel) return;
            m_IsTransitioningToNextLevel = true;
            StartCoroutine(PauseThenLoadScene(Level2Scene));
        }

        IEnumerator PauseThenLoadScene(string sceneName)
        {
            SetPauseMenuActivation(true);
            yield return null;

            Time.timeScale = 1f;
            SetPauseMenuActivation(false);
            SceneLoadCoordinator.LoadScene(sceneName);
        }

        void OnObjectiveCompleted(Objective objective)
        {
            if (m_IsTransitioningToNextLevel) return;
            if (objective == null || objective.IsOptional) return;

            string activeScene = SceneManager.GetActiveScene().name;
            string nextScene = activeScene == Level1Scene ? Level2Scene :
                activeScene == Level2Scene ? Level3Scene :
                activeScene == Level3Scene ? Level4Scene : string.Empty;
            if (string.IsNullOrEmpty(nextScene)) return;

            m_IsTransitioningToNextLevel = true;
            StartCoroutine(PauseThenLoadScene(nextScene));
        }

        void SetPauseMenuActivation(bool active)
        {
            MenuRoot.SetActive(active);

            if (MenuRoot.activeSelf)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Time.timeScale = 0f;
                AudioUtility.SetMasterVolume(VolumeWhenMenuOpen);

                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                Time.timeScale = 1f;
                AudioUtility.SetMasterVolume(1);
            }

        }

        void OnMouseSensitivityChanged(float newValue)
        {
            m_PlayerInputsHandler.LookSensitivity = newValue;
        }

        void OnShadowsChanged(bool newValue)
        {
            QualitySettings.shadows = newValue ? ShadowQuality.All : ShadowQuality.Disable;
        }

        void OnInvincibilityChanged(bool newValue)
        {
            m_PlayerHealth.Invincible = newValue;
        }

        void OnFramerateCounterChanged(bool newValue)
        {
            m_FramerateCounter.UIText.gameObject.SetActive(newValue);
        }

        void OnFullscreenChanged(bool fullscreen)
        {
            FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Vector2Int resolution = GetSelectedResolution();
            Screen.SetResolution(resolution.x, resolution.y, mode);
            PlayerPrefs.SetInt("DisplayFullscreen", fullscreen ? 1 : 0);
        }

        void OnResolutionChanged(int index)
        {
            if (index < 0 || index >= m_ResolutionOptions.Count) return;
            Vector2Int resolution = m_ResolutionOptions[index];
            Screen.SetResolution(resolution.x, resolution.y, Screen.fullScreenMode);
            PlayerPrefs.SetInt("DisplayWidth", resolution.x);
            PlayerPrefs.SetInt("DisplayHeight", resolution.y);
            PlayerPrefs.Save();
        }

        void OnLanguageChanged(bool dutch) => GameLocalization.SetDutch(dutch);

        void EnsureDisplayOptions()
        {
            if (FramerateToggle == null) return;
            Transform templateRow = FramerateToggle.transform.parent;
            if (FullscreenToggle == null)
                FullscreenToggle = CloneSettingRow(templateRow, "FullscreenPanel", "Fullscreen");
            if (ResolutionDropdown == null)
                ResolutionDropdown = CreateResolutionRow(templateRow);
            if (DutchLanguageToggle == null)
                DutchLanguageToggle = CloneSettingRow(templateRow, "LanguagePanel", "Dutch language");
        }

        void PopulateResolutionOptions()
        {
            m_ResolutionOptions.Clear();
            HashSet<Vector2Int> unique = new();
            Vector2Int[] common =
            {
                new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080),
                new(2560, 1440), new(3440, 1440), new(3840, 2160)
            };
            foreach (Vector2Int resolution in common) unique.Add(resolution);
            foreach (Resolution resolution in Screen.resolutions)
            {
                Vector2Int size = new(resolution.width, resolution.height);
                if (size.x >= 1024 && size.y >= 576 && size.x <= 3840 && size.y <= 2160)
                    unique.Add(size);
            }
            unique.Add(new Vector2Int(Screen.width, Screen.height));

            m_ResolutionOptions.AddRange(unique);
            m_ResolutionOptions.Sort((a, b) =>
            {
                int pixels = (a.x * a.y).CompareTo(b.x * b.y);
                return pixels != 0 ? pixels : a.x.CompareTo(b.x);
            });

            ResolutionDropdown.ClearOptions();
            List<Dropdown.OptionData> labels = new();
            foreach (Vector2Int resolution in m_ResolutionOptions)
                labels.Add(new Dropdown.OptionData($"{resolution.x} x {resolution.y}"));
            ResolutionDropdown.AddOptions(labels);

            int selected = 0;
            long closestDistance = long.MaxValue;
            for (int i = 0; i < m_ResolutionOptions.Count; i++)
            {
                long dx = m_ResolutionOptions[i].x - Screen.width;
                long dy = m_ResolutionOptions[i].y - Screen.height;
                long distance = dx * dx + dy * dy;
                if (distance < closestDistance) { closestDistance = distance; selected = i; }
            }
            ResolutionDropdown.SetValueWithoutNotify(selected);
            ResolutionDropdown.RefreshShownValue();
        }

        Vector2Int GetSelectedResolution()
        {
            int index = ResolutionDropdown != null ? ResolutionDropdown.value : -1;
            return index >= 0 && index < m_ResolutionOptions.Count
                ? m_ResolutionOptions[index]
                : new Vector2Int(Screen.width, Screen.height);
        }

        static Dropdown CreateResolutionRow(Transform templateRow)
        {
            Transform existing = templateRow.parent.Find("ResolutionPanel");
            if (existing != null)
            {
                Dropdown existingDropdown = existing.GetComponentInChildren<Dropdown>(true);
                if (existingDropdown != null) return existingDropdown;
            }

            GameObject row = Instantiate(templateRow.gameObject, templateRow.parent);
            row.name = "ResolutionPanel";
            row.transform.SetSiblingIndex(templateRow.GetSiblingIndex() + 2);
            Toggle oldToggle = row.GetComponentInChildren<Toggle>(true);
            if (oldToggle != null) oldToggle.gameObject.SetActive(false);
            TMP_Text title = row.GetComponentInChildren<TMP_Text>(true);
            if (title != null) title.text = "Resolution";

            GameObject dropdownObject = new("ResolutionDropdown", typeof(RectTransform), typeof(Image), typeof(Dropdown));
            dropdownObject.transform.SetParent(row.transform, false);
            RectTransform dropdownRect = dropdownObject.GetComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(.52f, .12f);
            dropdownRect.anchorMax = new Vector2(.96f, .88f);
            dropdownRect.offsetMin = dropdownRect.offsetMax = Vector2.zero;
            Image background = dropdownObject.GetComponent<Image>();
            background.color = new Color(.08f, .1f, .12f, 1f);

            Text caption = CreateDropdownText("Label", dropdownRect, TextAnchor.MiddleLeft, 15);
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = new Vector2(10f, 2f);
            caption.rectTransform.offsetMax = new Vector2(-28f, -2f);
            Text arrow = CreateDropdownText("Arrow", dropdownRect, TextAnchor.MiddleCenter, 14);
            arrow.text = "▼";
            arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
            arrow.rectTransform.anchorMax = Vector2.one;
            arrow.rectTransform.pivot = new Vector2(1f, .5f);
            arrow.rectTransform.sizeDelta = new Vector2(28f, 0f);

            GameObject templateObject = new("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            templateObject.transform.SetParent(dropdownRect, false);
            RectTransform dropdownTemplate = templateObject.GetComponent<RectTransform>();
            dropdownTemplate.anchorMin = new Vector2(0f, 0f);
            dropdownTemplate.anchorMax = new Vector2(1f, 0f);
            dropdownTemplate.pivot = new Vector2(.5f, 1f);
            dropdownTemplate.anchoredPosition = new Vector2(0f, -2f);
            dropdownTemplate.sizeDelta = new Vector2(0f, 180f);
            templateObject.GetComponent<Image>().color = new Color(.035f, .045f, .055f, .99f);

            GameObject viewportObject = new("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(dropdownTemplate, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport);
            viewportObject.GetComponent<Image>().color = Color.white;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            GameObject contentObject = new("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
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
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject itemObject = new("Item", typeof(RectTransform), typeof(Toggle), typeof(LayoutElement));
            itemObject.transform.SetParent(content, false);
            RectTransform item = itemObject.GetComponent<RectTransform>();
            item.sizeDelta = new Vector2(0f, 30f);
            itemObject.GetComponent<LayoutElement>().preferredHeight = 30f;
            Text checkmark = CreateDropdownText("Item Checkmark", item, TextAnchor.MiddleCenter, 14);
            checkmark.text = "✓";
            checkmark.rectTransform.anchorMin = Vector2.zero;
            checkmark.rectTransform.anchorMax = new Vector2(.14f, 1f);
            checkmark.rectTransform.offsetMin = checkmark.rectTransform.offsetMax = Vector2.zero;
            Text itemLabel = CreateDropdownText("Item Label", item, TextAnchor.MiddleLeft, 14);
            itemLabel.rectTransform.anchorMin = new Vector2(.14f, 0f);
            itemLabel.rectTransform.anchorMax = Vector2.one;
            itemLabel.rectTransform.offsetMin = new Vector2(4f, 0f);
            itemLabel.rectTransform.offsetMax = new Vector2(-4f, 0f);
            Toggle itemToggle = itemObject.GetComponent<Toggle>();
            itemToggle.graphic = checkmark;

            ScrollRect scroll = templateObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            Dropdown dropdown = dropdownObject.GetComponent<Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.captionText = caption;
            dropdown.template = dropdownTemplate;
            dropdown.itemText = itemLabel;
            templateObject.SetActive(false);
            return dropdown;
        }

        static Text CreateDropdownText(string name, Transform parent, TextAnchor alignment, int fontSize)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;
            text.fontSize = fontSize;
            text.alignment = alignment;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static Toggle CloneSettingRow(Transform templateRow, string name, string label)
        {
            Transform existing = templateRow.parent.Find(name);
            GameObject row = existing != null ? existing.gameObject : Instantiate(templateRow.gameObject, templateRow.parent);
            row.name = name;
            if (existing == null) row.transform.SetSiblingIndex(templateRow.GetSiblingIndex() + 1);
            Toggle toggle = row.GetComponentInChildren<Toggle>(true);
            if (toggle != null) toggle.onValueChanged = new Toggle.ToggleEvent();
            TMP_Text text = row.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
            return toggle;
        }

        void RefreshLocalizedLabels()
        {
            foreach (TMP_Text text in MenuRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                string value = text.text.Trim();
                if (value == "Menu") text.text = GameLocalization.Text("Pause / Options", "Pauze / Opties");
                else if (value == "Look Sensitivity" || value == "Kijkgevoeligheid")
                    text.text = GameLocalization.Text("Look Sensitivity", "Kijkgevoeligheid");
                else if (value == "Shadows" || value == "Schaduwen")
                    text.text = GameLocalization.Text("Shadows", "Schaduwen");
                else if (value == "Invincibility" || value == "Onkwetsbaarheid")
                    text.text = GameLocalization.Text("Invincibility", "Onkwetsbaarheid");
                else if (value == "Framerate Counter" || value == "Framerate-teller")
                    text.text = GameLocalization.Text("Framerate Counter", "Framerate-teller");
                else if (value == "Controls" || value == "Besturing")
                    text.text = GameLocalization.Text("Controls", "Besturing");
                else if (value == "Show" || value == "Tonen")
                    text.text = GameLocalization.Text("Show", "Tonen");
                else if (value == "Fullscreen" || value == "Volledig scherm")
                    text.text = GameLocalization.Text("Fullscreen", "Volledig scherm");
                else if (value == "Resolution" || value == "Resolutie")
                    text.text = GameLocalization.Text("Resolution", "Resolutie");
                else if (value == "Dutch language" || value == "Nederlandse taal")
                    text.text = GameLocalization.Text("Dutch language", "Nederlandse taal");
            }
        }

        void OnDestroy()
        {
            GameLocalization.LanguageChanged -= RefreshLocalizedLabels;
        }

        public void OnShowControlButtonClicked(bool show)
        {
            ControlImage.SetActive(show);
        }
    }
}
