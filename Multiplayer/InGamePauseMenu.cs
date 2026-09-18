using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.FPS.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;

namespace ZombieTown.Multiplayer
{
    /// <summary>
    /// Owns the single in-game pause flow. The legacy FPS menu remains available for
    /// level progression, but no longer competes for Escape, cursor or UI focus.
    /// </summary>
    public sealed class InGamePauseMenu : MonoBehaviour
    {
        const float PausedVolume = .36f;
        const string MainMenuScene = "IntroMenu";

        readonly List<Vector2Int> resolutionOptions = new();
        Canvas menu;
        GameObject pausePage;
        GameObject settingsPage;
        PlayerInputHandler playerInput;
        Slider sensitivitySlider;
        Dropdown resolutionDropdown;
        Button fullscreenButton;
        Button languageButton;
        bool open;

        public static bool IsInstalled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<InGamePauseMenu>() != null) return;
            GameObject root = new("In-Game Pause Menu");
            DontDestroyOnLoad(root);
            root.AddComponent<InGamePauseMenu>();
        }

        void Awake()
        {
            IsInstalled = true;
            InGameMenuManager.ExternalPauseMenuOwnsInput = true;
        }

        void OnDestroy()
        {
            if (open) RestoreGameplay();
            InGameMenuManager.ExternalPauseMenuOwnsInput = false;
            IsInstalled = false;
        }

        void Update()
        {
            if (!IsGameplayScene())
            {
                if (open) SetOpen(false);
                return;
            }

            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (!open)
            {
                ResolvePlayerInput();
                if (playerInput != null && !playerInput.GameplayInputEnabled) return;
            }
            SetOpen(!open);
        }

        static bool IsGameplayScene()
        {
            string scene = SceneManager.GetActiveScene().name;
            return scene == "ZombieTownScene" || scene == "RadioOutpostScene" ||
                   scene == "HarborEvacuationScene" || scene == "RetreatDefenseScene" ||
                   scene == "HarborViewCityScene" || scene == "DeadOrbitScene" || scene == "BusEscapeFinaleScene";
        }

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            if (open)
            {
                ResolvePlayerInput();
                if (playerInput != null) playerInput.SetGameplayInputEnabled(false);
                // A host owns the authoritative simulation. Pausing global time there
                // would freeze every connected survivor, so online menus only block
                // this local player's input. Offline sessions still pause normally.
                Time.timeScale = ShouldPauseSimulation() ? 0f : 1f;
                AudioUtility.SetMasterVolume(PausedVolume);
                BuildMenu();
                ShowPage(pausePage);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                if (menu != null) Destroy(menu.gameObject);
                menu = null;
                pausePage = null;
                settingsPage = null;
                RestoreGameplay();
            }
        }

        void RestoreGameplay()
        {
            Time.timeScale = 1f;
            AudioUtility.SetMasterVolume(1f);
            ResolvePlayerInput();
            if (playerInput != null) playerInput.SetGameplayInputEnabled(true);
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        static bool ShouldPauseSimulation()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager == null || !manager.IsListening;
        }

        void ResolvePlayerInput()
        {
            if (playerInput != null) return;
            foreach (PlayerInputHandler candidate in
                     FindObjectsByType<PlayerInputHandler>(FindObjectsInactive.Exclude))
            {
                if (!candidate.isActiveAndEnabled) continue;
                playerInput = candidate;
                break;
            }
        }

        void BuildMenu()
        {
            if (menu != null) return;
            menu = RuntimeMenuUI.CreateCanvas("Pause Menu Canvas", 2000);
            RectTransform backdrop = RuntimeMenuUI.Block("Backdrop", menu.transform,
                new Color(.006f, .013f, .019f, .92f));
            RuntimeMenuUI.Stretch(backdrop);

            RectTransform glow = RuntimeMenuUI.Block("Warm Edge", backdrop,
                new Color(1f, .34f, .08f, .15f));
            glow.anchorMin = new Vector2(0f, 0f);
            glow.anchorMax = new Vector2(.012f, 1f);
            glow.offsetMin = glow.offsetMax = Vector2.zero;

            RectTransform panel = RuntimeMenuUI.CardBlock("Pause Panel", backdrop, RuntimeMenuUI.Panel);
            panel.anchorMin = new Vector2(.32f, .11f);
            panel.anchorMax = new Vector2(.68f, .89f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;

            Text eyebrow = RuntimeMenuUI.Label("Eyebrow", panel,
                GameLocalization.Text("MISSION PAUSED", "MISSIE GEPAUZEERD"), 14,
                TextAnchor.MiddleCenter, RuntimeMenuUI.Accent);
            eyebrow.fontStyle = FontStyle.Bold;
            eyebrow.rectTransform.anchorMin = new Vector2(.08f, .875f);
            eyebrow.rectTransform.anchorMax = new Vector2(.92f, .93f);
            eyebrow.rectTransform.offsetMin = eyebrow.rectTransform.offsetMax = Vector2.zero;

            Text title = RuntimeMenuUI.Label("Title", panel, "ZOMBIE TOWN", 42,
                TextAnchor.MiddleCenter, RuntimeMenuUI.White);
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(.08f, .79f);
            title.rectTransform.anchorMax = new Vector2(.92f, .88f);
            title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;

            Text hint = RuntimeMenuUI.Label("Hint", panel,
                GameLocalization.Text("ESC  closes this menu", "ESC  sluit dit menu"), 14,
                TextAnchor.MiddleCenter, RuntimeMenuUI.Muted);
            hint.rectTransform.anchorMin = new Vector2(.08f, .08f);
            hint.rectTransform.anchorMax = new Vector2(.92f, .14f);
            hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;

            RectTransform pageArea = new GameObject("Page Area", typeof(RectTransform)).GetComponent<RectTransform>();
            pageArea.SetParent(panel, false);
            pageArea.anchorMin = new Vector2(.11f, .17f);
            pageArea.anchorMax = new Vector2(.89f, .77f);
            pageArea.offsetMin = pageArea.offsetMax = Vector2.zero;

            pausePage = CreatePage("Pause", pageArea);
            AddButton(pausePage.transform, "Resume",
                GameLocalization.Text("RESUME", "DOORGAAN"), RuntimeMenuUI.Green,
                () => SetOpen(false), 72f, 22);
            AddButton(pausePage.transform, "Settings",
                GameLocalization.Text("SETTINGS", "INSTELLINGEN"), RuntimeMenuUI.Accent,
                () => ShowPage(settingsPage), 64f, 20);
            AddButton(pausePage.transform, "Main Menu",
                GameLocalization.Text("RETURN TO MAIN MENU", "TERUG NAAR HOOFDMENU"),
                RuntimeMenuUI.Card, ReturnToMainMenu, 58f, 17);

            settingsPage = CreatePage("Settings", pageArea);
            AddHeading(settingsPage.transform, GameLocalization.Text("SETTINGS", "INSTELLINGEN"));
            sensitivitySlider = AddSlider(settingsPage.transform,
                GameLocalization.Text("LOOK SENSITIVITY", "KIJKGEVOELIGHEID"), .15f, 3f,
                playerInput != null ? playerInput.LookSensitivity : 1f);
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
            resolutionDropdown = RuntimeMenuUI.Dropdown(settingsPage.transform, 46f);
            PopulateResolutionOptions();
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            fullscreenButton = AddButton(settingsPage.transform, "Fullscreen", string.Empty,
                RuntimeMenuUI.Card, ToggleFullscreen, 46f, 16);
            languageButton = AddButton(settingsPage.transform, "Language", string.Empty,
                RuntimeMenuUI.Card, ToggleLanguage, 46f, 16);
            RefreshSettingButtons();
            AddButton(settingsPage.transform, "Back",
                GameLocalization.Text("BACK", "TERUG"), RuntimeMenuUI.Accent,
                () => ShowPage(pausePage), 50f, 17);
        }

        static GameObject CreatePage(string name, Transform parent)
        {
            GameObject page = new(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            page.transform.SetParent(parent, false);
            RuntimeMenuUI.Stretch(page.GetComponent<RectTransform>());
            VerticalLayoutGroup layout = page.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 12;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return page;
        }

        static void AddHeading(Transform parent, string value)
        {
            Text heading = RuntimeMenuUI.Label("Heading", parent, value, 22,
                TextAnchor.MiddleCenter, RuntimeMenuUI.White);
            heading.fontStyle = FontStyle.Bold;
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        }

        static Button AddButton(Transform parent, string name, string label, Color color,
            UnityEngine.Events.UnityAction action, float height, int fontSize)
        {
            Button button = RuntimeMenuUI.Button(name, parent, label, color, fontSize);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            button.onClick.AddListener(action);
            return button;
        }

        static Slider AddSlider(Transform parent, string label, float min, float max, float value)
        {
            GameObject row = new("Sensitivity", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 70f;
            VerticalLayoutGroup layout = row.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 5f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            Text title = RuntimeMenuUI.Label("Label", row.transform, label, 14,
                TextAnchor.MiddleLeft, RuntimeMenuUI.Muted);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            RectTransform track = RuntimeMenuUI.Block("Track", row.transform, RuntimeMenuUI.Card);
            track.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            Slider slider = track.gameObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            RectTransform fill = RuntimeMenuUI.Block("Fill", track, RuntimeMenuUI.Accent);
            RuntimeMenuUI.Stretch(fill, 3, 3, 8, 8);
            slider.fillRect = fill;
            slider.targetGraphic = track.GetComponent<Image>();
            return slider;
        }

        void ShowPage(GameObject page)
        {
            if (pausePage != null) pausePage.SetActive(page == pausePage);
            if (settingsPage != null) settingsPage.SetActive(page == settingsPage);
            RuntimeMenuUI.EnsureEventSystem();
            if (EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            Button first = page != null ? page.GetComponentInChildren<Button>(true) : null;
            if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        void PopulateResolutionOptions()
        {
            resolutionOptions.Clear();
            HashSet<Vector2Int> unique = new()
            {
                new(1280, 720), new(1600, 900), new(1920, 1080),
                new(2560, 1440), new(3440, 1440), new(3840, 2160)
            };
            foreach (Resolution resolution in Screen.resolutions)
            {
                Vector2Int size = new(resolution.width, resolution.height);
                if (size.x >= 1024 && size.y >= 576 && size.x <= 3840 && size.y <= 2160)
                    unique.Add(size);
            }
            unique.Add(new Vector2Int(Screen.width, Screen.height));
            resolutionOptions.AddRange(unique);
            resolutionOptions.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));

            List<Dropdown.OptionData> options = new();
            foreach (Vector2Int resolution in resolutionOptions)
                options.Add(new Dropdown.OptionData($"{resolution.x} x {resolution.y}"));
            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(options);

            int selected = 0;
            long best = long.MaxValue;
            for (int i = 0; i < resolutionOptions.Count; i++)
            {
                long dx = resolutionOptions[i].x - Screen.width;
                long dy = resolutionOptions[i].y - Screen.height;
                long distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; selected = i; }
            }
            resolutionDropdown.SetValueWithoutNotify(selected);
            resolutionDropdown.RefreshShownValue();
        }

        void OnSensitivityChanged(float value)
        {
            if (playerInput != null) playerInput.LookSensitivity = value;
            PlayerPrefs.SetFloat("LookSensitivity", value);
        }

        void OnResolutionChanged(int index)
        {
            if (index < 0 || index >= resolutionOptions.Count) return;
            Vector2Int resolution = resolutionOptions[index];
            Screen.SetResolution(resolution.x, resolution.y, Screen.fullScreenMode);
            PlayerPrefs.SetInt("DisplayWidth", resolution.x);
            PlayerPrefs.SetInt("DisplayHeight", resolution.y);
            PlayerPrefs.Save();
        }

        void ToggleFullscreen()
        {
            FullScreenMode mode = Screen.fullScreen ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            Vector2Int resolution = resolutionDropdown != null &&
                                    resolutionDropdown.value < resolutionOptions.Count
                ? resolutionOptions[resolutionDropdown.value]
                : new Vector2Int(Screen.width, Screen.height);
            Screen.SetResolution(resolution.x, resolution.y, mode);
            PlayerPrefs.SetInt("DisplayFullscreen", mode != FullScreenMode.Windowed ? 1 : 0);
            PlayerPrefs.Save();
            RefreshSettingButtons();
        }

        void ToggleLanguage()
        {
            GameLocalization.SetDutch(!GameLocalization.IsDutch);
            if (menu != null) Destroy(menu.gameObject);
            menu = null;
            pausePage = null;
            settingsPage = null;
            BuildMenu();
            ShowPage(settingsPage);
        }

        void RefreshSettingButtons()
        {
            Text fullscreen = fullscreenButton != null ? fullscreenButton.GetComponentInChildren<Text>() : null;
            if (fullscreen != null)
                fullscreen.text = Screen.fullScreen
                    ? GameLocalization.Text("FULLSCREEN: ON", "FULLSCREEN: AAN")
                    : GameLocalization.Text("FULLSCREEN: OFF", "FULLSCREEN: UIT");
            Text language = languageButton != null ? languageButton.GetComponentInChildren<Text>() : null;
            if (language != null)
                language.text = GameLocalization.IsDutch ? "TAAL: NEDERLANDS" : "LANGUAGE: ENGLISH";
        }

        void ReturnToMainMenu()
        {
            open = false;
            Time.timeScale = 1f;
            AudioUtility.SetMasterVolume(1f);
            if (menu != null) Destroy(menu.gameObject);
            SceneLoadCoordinator.LoadScene(MainMenuScene);
        }
    }
}
