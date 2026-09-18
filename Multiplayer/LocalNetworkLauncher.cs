using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.FPS.Game;
using System.Collections.Generic;

namespace ZombieTown.Multiplayer
{
    public sealed class LocalNetworkLauncher : MonoBehaviour
    {
        const string DefaultGameScene = "ZombieTownScene";
        const string RadioOutpostScene = "RadioOutpostScene";
        const string HarborEvacuationScene = "HarborEvacuationScene";
        const string RetreatDefenseScene = "RetreatDefenseScene";
        const string HarborViewCityScene = "HarborViewCityScene";
        const string DeadOrbitScene = "DeadOrbitScene";
        const string BusEscapeFinaleScene = "BusEscapeFinaleScene";
        const float ConnectionTimeoutSeconds = 20f;
        const string RelayProtocol = "wss";
        Canvas menu;
        InputField addressInput;
        InputField portInput;
        InputField relayCodeInput;
        Text status;
        bool relayBusy;
        GameObject landingPage;
        GameObject createPage;
        GameObject joinPage;
        GameObject settingsPage;
        Dropdown resolutionDropdown;
        Dropdown levelDropdown;
        Button fullscreenButton;
        Button languageButton;
        readonly List<Vector2Int> resolutionOptions = new();
        NetworkManager networkManager;
        bool waitingForClientConnection;
        bool handlingConnectionFailure;
        float connectionDeadline;
        string relayAuthenticationProfile;
        bool relaySmokeMode;

        void Start()
        {
            networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;
            SubscribeNetworkEvents();
            if (networkManager.IsListening) return;
            ApplySavedDisplaySettings();
            BuildMenu();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (NetworkSessionBootstrap.ConsumeRestartAsHost(out string restartScene))
            {
                SelectGameScene(restartScene);
                StartHost();
            }
            else TryRunRelaySmokeMode();
        }

        void Update()
        {
            if (waitingForClientConnection && Time.realtimeSinceStartup >= connectionDeadline &&
                networkManager != null && !networkManager.IsConnectedClient)
            {
                HandleConnectionFailure(L(
                    "Online connection timed out. Check the join code and internet connection.",
                    "Online verbinden duurde te lang. Controleer de joincode en internetverbinding."));
            }

            // StartClient makes IsListening true before the server has accepted the
            // player. Keep the menu visible until the connection is genuinely ready.
            if (menu != null && networkManager != null && networkManager.IsListening &&
                (networkManager.IsServer || networkManager.IsConnectedClient))
                menu.gameObject.SetActive(false);
        }

        void SubscribeNetworkEvents()
        {
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            networkManager.OnTransportFailure += OnTransportFailure;
        }

        void UnsubscribeNetworkEvents()
        {
            if (networkManager == null) return;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            networkManager.OnTransportFailure -= OnTransportFailure;
        }

        void BuildMenu()
        {
            menu = RuntimeMenuUI.CreateCanvas("Local Multiplayer Menu", 900);
            RectTransform backdrop = RuntimeMenuUI.Block("Backdrop", menu.transform, RuntimeMenuUI.Background);
            RuntimeMenuUI.Stretch(backdrop);

            RectTransform haze = RuntimeMenuUI.Block("Harbor Haze", backdrop,
                new Color(.05f, .19f, .22f, .2f));
            haze.anchorMin = new Vector2(0f, .62f);
            haze.anchorMax = new Vector2(1f, 1f);
            haze.offsetMin = haze.offsetMax = Vector2.zero;

            RectTransform accentRail = RuntimeMenuUI.Block("Accent Rail", backdrop, RuntimeMenuUI.Accent);
            accentRail.anchorMin = new Vector2(.128f, .1f);
            accentRail.anchorMax = new Vector2(.135f, .9f);
            accentRail.offsetMin = accentRail.offsetMax = Vector2.zero;

            RectTransform panel = RuntimeMenuUI.CardBlock("Panel", backdrop, RuntimeMenuUI.Panel);
            panel.anchorMin = new Vector2(.135f, .1f);
            panel.anchorMax = new Vector2(.865f, .9f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            RectTransform divider = RuntimeMenuUI.Block("Divider", panel, new Color(1f, 1f, 1f, .075f));
            divider.anchorMin = new Vector2(.36f, .1f);
            divider.anchorMax = new Vector2(.3615f, .9f);
            divider.offsetMin = divider.offsetMax = Vector2.zero;

            Text title = RuntimeMenuUI.Label("Title", panel, "ZOMBIE\nTOWN", 54, TextAnchor.MiddleLeft, RuntimeMenuUI.White);
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(.055f, .66f);
            title.rectTransform.anchorMax = new Vector2(.32f, .86f);
            title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;

            Text subtitle = RuntimeMenuUI.Label("Subtitle", panel, "SURVIVE TOGETHER", 15, TextAnchor.MiddleLeft, RuntimeMenuUI.Accent);
            subtitle.fontStyle = FontStyle.Bold;
            subtitle.rectTransform.anchorMin = new Vector2(.055f, .59f);
            subtitle.rectTransform.anchorMax = new Vector2(.32f, .66f);
            subtitle.rectTransform.offsetMin = subtitle.rectTransform.offsetMax = Vector2.zero;

            Text briefing = RuntimeMenuUI.Label("Briefing", panel,
                L("Three districts. Eight survivors. One way out.",
                    "Drie districten. Acht overlevenden. Eén uitweg."),
                18, TextAnchor.UpperLeft, RuntimeMenuUI.Muted);
            briefing.rectTransform.anchorMin = new Vector2(.055f, .31f);
            briefing.rectTransform.anchorMax = new Vector2(.315f, .53f);
            briefing.rectTransform.offsetMin = briefing.rectTransform.offsetMax = Vector2.zero;

            Text version = RuntimeMenuUI.Label("Version", panel,
                L("CO-OP SURVIVAL  •  ONLINE / LAN", "CO-OP SURVIVAL  •  ONLINE / LAN"),
                12, TextAnchor.LowerLeft, new Color(.42f, .5f, .53f, 1f));
            version.rectTransform.anchorMin = new Vector2(.055f, .1f);
            version.rectTransform.anchorMax = new Vector2(.32f, .2f);
            version.rectTransform.offsetMin = version.rectTransform.offsetMax = Vector2.zero;

            RectTransform pageArea = new GameObject("Page Area", typeof(RectTransform)).GetComponent<RectTransform>();
            pageArea.SetParent(panel, false);
            pageArea.anchorMin = new Vector2(.405f, .18f);
            pageArea.anchorMax = new Vector2(.94f, .84f);
            pageArea.offsetMin = pageArea.offsetMax = Vector2.zero;

            landingPage = CreatePage("Start", pageArea);
            AddPageHeading(landingPage.transform, L("PLAY", "SPELEN"));
            AddPageButton(landingPage.transform, "Create", L("CREATE GAME", "MAAK SPEL"), RuntimeMenuUI.Accent, ShowCreatePage, 78);
            AddPageButton(landingPage.transform, "Join", L("JOIN GAME", "JOIN SPEL"), RuntimeMenuUI.Green, ShowJoinPage, 78);
            AddPageButton(landingPage.transform, "Settings", L("SETTINGS", "INSTELLINGEN"),
                RuntimeMenuUI.Card, ShowSettingsPage, 54, 17);

            createPage = CreatePage("Create Game", pageArea);
            AddPageHeading(createPage.transform, L("CREATE GAME", "MAAK SPEL"));
            levelDropdown = RuntimeMenuUI.Dropdown(createPage.transform);
            levelDropdown.options = new List<Dropdown.OptionData>
            {
                new(L("LEVEL 1 - ZOMBIE TOWN", "LEVEL 1 - ZOMBIESTAD")),
                new(L("LEVEL 2 - RADIO OUTPOST", "LEVEL 2 - RADIOPOST")),
                new(L("LEVEL 3 - HARBOR EVACUATION", "LEVEL 3 - HAVENEVACUATIE")),
                new(L("LEVEL 4 - FALLBACK PROTOCOL", "LEVEL 4 - TERUGTREKPROTOCOL")),
                new(L("LEVEL 5 - IN THE CITY", "LEVEL 5 - IN THE CITY")),
                new(L("LEVEL 6 - DEAD ORBIT", "LEVEL 6 - DODE OMLOOPBAAN")),
                new(L("LEVEL 7 - LAST EXIT", "LEVEL 7 - LAATSTE UITWEG"))
            };
            levelDropdown.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            levelDropdown.RefreshShownValue();
            AddPageButton(createPage.transform, "Relay Host", L("HOST ONLINE (FRIENDS)", "HOST ONLINE (VRIENDEN)"), RuntimeMenuUI.Green, StartRelayHost);
            AddPageButton(createPage.transform, "Local Host", L("HOST LAN (SAME NETWORK)", "HOST LAN (ZELFDE NETWERK)"), RuntimeMenuUI.Accent, StartHost);
            AddPageButton(createPage.transform, "Back", L("BACK", "TERUG"), new Color(.24f, .28f, .31f, 1f), ShowLandingPage, 44, 17);

            joinPage = CreatePage("Join Game", pageArea);
            AddPageHeading(joinPage.transform, L("JOIN GAME", "JOIN SPEL"));
            relayCodeInput = RuntimeMenuUI.Input(joinPage.transform, L("RELAY CODE", "RELAY-CODE"), true);
            relayCodeInput.characterLimit = 12;
            relayCodeInput.onValidateInput = RelayJoinCodeUtility.ValidateCharacter;
            relayCodeInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            AddPageButton(joinPage.transform, "Relay Join", L("JOIN WITH CODE", "JOIN VIA CODE"), RuntimeMenuUI.Green, StartRelayClient);

            RectTransform lanRow = new GameObject("LAN", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            lanRow.SetParent(joinPage.transform, false);
            lanRow.GetComponent<LayoutElement>().preferredHeight = 48;
            HorizontalLayoutGroup lanLayout = lanRow.GetComponent<HorizontalLayoutGroup>();
            lanLayout.spacing = 8;
            lanLayout.childControlHeight = true;
            lanLayout.childForceExpandHeight = true;
            addressInput = RuntimeMenuUI.Input(lanRow, "127.0.0.1");
            addressInput.gameObject.AddComponent<LayoutElement>().flexibleWidth = 3;
            portInput = RuntimeMenuUI.Input(lanRow, "7777");
            portInput.contentType = InputField.ContentType.IntegerNumber;
            portInput.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            AddPageButton(joinPage.transform, "LAN Join", L("JOIN LAN", "JOIN LOKAAL"), new Color(.35f, .4f, .44f, 1f), StartClient, 48, 17);
            AddPageButton(joinPage.transform, "Back", L("BACK", "TERUG"), new Color(.24f, .28f, .31f, 1f), ShowLandingPage, 42, 17);

            settingsPage = CreatePage("Settings", pageArea);
            AddPageHeading(settingsPage.transform, L("SETTINGS", "INSTELLINGEN"));
            Text displayLabel = RuntimeMenuUI.Label("Display Label", settingsPage.transform,
                L("DISPLAY RESOLUTION", "SCHERMRESOLUTIE"), 14, TextAnchor.MiddleLeft, RuntimeMenuUI.Muted);
            displayLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;
            resolutionDropdown = RuntimeMenuUI.Dropdown(settingsPage.transform);
            PopulateResolutionOptions();
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            fullscreenButton = AddPageButton(settingsPage.transform, "Fullscreen", string.Empty,
                RuntimeMenuUI.Card, ToggleFullscreen, 48, 17);
            languageButton = AddPageButton(settingsPage.transform, "Language", string.Empty,
                RuntimeMenuUI.Card, ToggleLanguage, 48, 17);
            RefreshFullscreenButton();
            RefreshLanguageButton();
            AddPageButton(settingsPage.transform, "Back", L("BACK", "TERUG"),
                RuntimeMenuUI.Accent, ShowLandingPage, 46, 17);

            status = RuntimeMenuUI.Label("Status", panel, L("Choose how you want to play.", "Kies hoe je wilt spelen."), 16, TextAnchor.MiddleCenter, new Color(.7f, .76f, .8f, 1f));
            status.rectTransform.anchorMin = new Vector2(.4f, .065f);
            status.rectTransform.anchorMax = new Vector2(.94f, .15f);
            status.rectTransform.offsetMin = status.rectTransform.offsetMax = Vector2.zero;

            ShowLandingPage();
        }

        static GameObject CreatePage(string name, Transform parent)
        {
            GameObject page = new(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            page.transform.SetParent(parent, false);
            RuntimeMenuUI.Stretch(page.GetComponent<RectTransform>());
            VerticalLayoutGroup layout = page.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.spacing = 12;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return page;
        }

        static void AddPageHeading(Transform parent, string value)
        {
            Text heading = RuntimeMenuUI.Label(value, parent, value, 24, TextAnchor.MiddleCenter, RuntimeMenuUI.White);
            heading.fontStyle = FontStyle.Bold;
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
        }

        static Button AddPageButton(Transform parent, string name, string label, Color color,
            UnityEngine.Events.UnityAction action, float height = 62f, int fontSize = 21)
        {
            Button button = RuntimeMenuUI.Button(name, parent, label, color, fontSize);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            button.onClick.AddListener(action);
            return button;
        }

        void ShowLandingPage() => ShowPage(landingPage);
        void ShowCreatePage() => ShowPage(createPage);
        void ShowJoinPage() => ShowPage(joinPage);
        void ShowSettingsPage() => ShowPage(settingsPage);

        void ShowPage(GameObject page)
        {
            if (landingPage != null) landingPage.SetActive(page == landingPage);
            if (createPage != null) createPage.SetActive(page == createPage);
            if (joinPage != null) joinPage.SetActive(page == joinPage);
            if (settingsPage != null) settingsPage.SetActive(page == settingsPage);
            string message = page == landingPage
                ? L("Choose how you want to play.", "Kies hoe je wilt spelen.")
                : page == createPage
                    ? L("For friends over the internet, choose Host Online.", "Kies Host Online voor vrienden via internet.")
                    : page == joinPage
                        ? L("Use a Relay code for internet play; LAN is only for the same network.",
                            "Gebruik een Relay-code via internet; LAN werkt alleen op hetzelfde netwerk.")
                        : L("Changes are saved automatically.", "Wijzigingen worden automatisch opgeslagen.");
            SetStatus(message, new Color(.7f, .76f, .8f, 1f));
        }

        void ApplySavedDisplaySettings()
        {
            if (!PlayerPrefs.HasKey("DisplayWidth") || !PlayerPrefs.HasKey("DisplayHeight"))
            {
                if (!Screen.fullScreen && (Screen.width < 1600 || Screen.height < 900))
                    Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                return;
            }
            int width = Mathf.Clamp(PlayerPrefs.GetInt("DisplayWidth", 1600), 1024, 3840);
            int height = Mathf.Clamp(PlayerPrefs.GetInt("DisplayHeight", 900), 576, 2160);
            bool fullscreen = PlayerPrefs.GetInt("DisplayFullscreen", Screen.fullScreen ? 1 : 0) != 0;
            Screen.SetResolution(width, height,
                fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        void PopulateResolutionOptions()
        {
            resolutionOptions.Clear();
            HashSet<Vector2Int> unique = new()
            {
                new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080),
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
            resolutionOptions.Sort((a, b) =>
            {
                int pixels = (a.x * a.y).CompareTo(b.x * b.y);
                return pixels != 0 ? pixels : a.x.CompareTo(b.x);
            });

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
            bool fullscreen = !Screen.fullScreen;
            int index = resolutionDropdown != null ? resolutionDropdown.value : -1;
            Vector2Int resolution = index >= 0 && index < resolutionOptions.Count
                ? resolutionOptions[index]
                : new Vector2Int(Screen.width, Screen.height);
            Screen.SetResolution(resolution.x, resolution.y,
                fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            PlayerPrefs.SetInt("DisplayFullscreen", fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            RefreshFullscreenButton();
        }

        void RefreshFullscreenButton()
        {
            Text label = fullscreenButton != null ? fullscreenButton.GetComponentInChildren<Text>() : null;
            if (label != null)
                label.text = Screen.fullScreen
                    ? L("FULLSCREEN: ON", "FULLSCREEN: AAN")
                    : L("FULLSCREEN: OFF", "FULLSCREEN: UIT");
        }

        void ToggleLanguage()
        {
            GameLocalization.SetDutch(!GameLocalization.IsDutch);
            if (menu != null) Destroy(menu.gameObject);
            menu = null;
            landingPage = null;
            createPage = null;
            joinPage = null;
            settingsPage = null;
            BuildMenu();
            ShowSettingsPage();
        }

        void RefreshLanguageButton()
        {
            Text label = languageButton != null ? languageButton.GetComponentInChildren<Text>() : null;
            if (label != null)
                label.text = GameLocalization.IsDutch ? "TAAL: NEDERLANDS" : "LANGUAGE: ENGLISH";
        }

        void StartHost()
        {
            if (!Configure()) return;
            NetworkManager.Singleton.GetComponent<NetworkSessionBootstrap>()?.EnsureConfigured();
            if (NetworkManager.Singleton.StartHost())
                NetworkManager.Singleton.SceneManager.LoadScene(SelectedGameScene, LoadSceneMode.Single);
            else SetStatus(L("Could not start the host. Check the Console.", "Host starten is mislukt. Bekijk de Console."), Color.red);
        }

        void StartClient()
        {
            if (!Configure()) return;
            NetworkManager.Singleton.GetComponent<NetworkSessionBootstrap>()?.EnsureConfigured();
            if (!NetworkManager.Singleton.StartClient()) SetStatus(L("Connection failed. Check the address and port.", "Verbinden is mislukt. Controleer adres en poort."), Color.red);
            else
            {
                BeginClientConnection();
                SetStatus(L("Connecting to LAN host...", "Verbinden met LAN-host..."), RuntimeMenuUI.Green);
            }
        }

        void StartServer()
        {
            if (!Configure()) return;
            NetworkManager.Singleton.GetComponent<NetworkSessionBootstrap>()?.EnsureConfigured();
            if (!NetworkManager.Singleton.StartServer()) SetStatus(L("Could not start the server.", "Server starten is mislukt."), Color.red);
        }

        async void StartRelayHost()
        {
            if (relayBusy) return;
            relayBusy = true;
            SetStatus(L("Initializing Relay...", "Relay initialiseren..."), RuntimeMenuUI.Accent);
            try
            {
                await EnsureUnityServices();
                var allocation = await RelayService.Instance.CreateAllocationAsync(7);
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.UseWebSockets = true;
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayProtocol));
                NetworkManager.Singleton.GetComponent<NetworkSessionBootstrap>()?.EnsureConfigured();
                if (!NetworkManager.Singleton.StartHost()) throw new InvalidOperationException(L("NetworkManager could not start the Relay host.", "NetworkManager kon de Relay-host niet starten."));
                RelayHostCodePresenter.Show(joinCode);
                relayBusy = false;
                Debug.Log("Relay host started through secure WebSockets.");
                if (relaySmokeMode) Debug.Log($"[RelaySmoke] HOST_CODE={joinCode}");
                SetStatus(L($"Relay code {joinCode} was copied to the clipboard.", $"Relay-code {joinCode} is naar het klembord gekopieerd."), RuntimeMenuUI.Green);
                NetworkManager.Singleton.SceneManager.LoadScene(SelectedGameScene, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Relay host failed: {exception}");
                SetStatus(L("Relay host failed: ", "Relay-host mislukt: ") + FriendlyRelayError(exception), Color.red);
                relayBusy = false;
            }
        }

        string SelectedGameScene => levelDropdown != null
            ? levelDropdown.value == 6 ? BusEscapeFinaleScene
                : levelDropdown.value == 5 ? DeadOrbitScene
                : levelDropdown.value == 4 ? HarborViewCityScene
                : levelDropdown.value == 3 ? RetreatDefenseScene
                : levelDropdown.value == 2 ? HarborEvacuationScene
                : levelDropdown.value == 1 ? RadioOutpostScene : DefaultGameScene
            : DefaultGameScene;

        void SelectGameScene(string sceneName)
        {
            if (levelDropdown != null)
                levelDropdown.value = string.Equals(sceneName, BusEscapeFinaleScene, StringComparison.Ordinal) ? 6
                    : string.Equals(sceneName, DeadOrbitScene, StringComparison.Ordinal) ? 5
                    : string.Equals(sceneName, HarborViewCityScene, StringComparison.Ordinal) ? 4
                    : string.Equals(sceneName, RetreatDefenseScene, StringComparison.Ordinal) ? 3
                    : string.Equals(sceneName, HarborEvacuationScene, StringComparison.Ordinal) ? 2
                    : string.Equals(sceneName, RadioOutpostScene, StringComparison.Ordinal) ? 1 : 0;
        }

        async void StartRelayClient()
        {
            if (relayBusy) return;
            if (!RelayJoinCodeUtility.TryNormalize(relayCodeInput.text, out string joinCode))
            {
                SetStatus(L("Enter the 6-12 character Relay code shown by the host.",
                    "Vul de Relay-code van 6-12 tekens in die bij de host in beeld staat."), Color.red);
                return;
            }
            relayBusy = true;
            SetStatus(L("Finding Relay session...", "Relay-sessie zoeken..."), RuntimeMenuUI.Accent);
            try
            {
                await EnsureUnityServices();
                var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.UseWebSockets = true;
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayProtocol));
                NetworkManager.Singleton.GetComponent<NetworkSessionBootstrap>()?.EnsureConfigured();
                if (!NetworkManager.Singleton.StartClient()) throw new InvalidOperationException(L("NetworkManager could not start the Relay client.", "NetworkManager kon de Relay-client niet starten."));
                BeginClientConnection();
                SetStatus(L("Connecting through firewall-friendly Relay...", "Verbinden via firewallvriendelijke Relay..."), RuntimeMenuUI.Green);
            }
            catch (Exception exception)
            {
                string safeError = exception.ToString().Replace(joinCode, "<redacted>",
                    StringComparison.OrdinalIgnoreCase);
                Debug.LogWarning($"Relay join failed: {safeError}");
                SetStatus(L("Relay join failed: ", "Relay-join mislukt: ") + FriendlyRelayError(exception), Color.red);
                relayBusy = false;
            }
        }

        async Task EnsureUnityServices()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                InitializationOptions options = new();
                if (!string.IsNullOrEmpty(relayAuthenticationProfile)) options.SetProfile(relayAuthenticationProfile);
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        void TryRunRelaySmokeMode()
        {
            string joinCode = null;
            bool host = false;
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument.Equals("--relay-smoke-host", StringComparison.OrdinalIgnoreCase)) host = true;
                else if (argument.StartsWith("--relay-smoke-join=", StringComparison.OrdinalIgnoreCase))
                    joinCode = argument.Substring("--relay-smoke-join=".Length);
                else if (argument.StartsWith("--relay-profile=", StringComparison.OrdinalIgnoreCase))
                    relayAuthenticationProfile = argument.Substring("--relay-profile=".Length);
            }
            if (host)
            {
                relaySmokeMode = true;
                ShowCreatePage();
                StartRelayHost();
            }
            else if (!string.IsNullOrEmpty(joinCode))
            {
                relaySmokeMode = true;
                ShowJoinPage();
                relayCodeInput.text = joinCode;
                StartRelayClient();
            }
        }

        static string FriendlyRelayError(Exception exception)
        {
            string message = exception.Message;
            if (message.Contains("project", StringComparison.OrdinalIgnoreCase))
                return L("check whether the Unity Cloud Project and Relay are enabled.", "controleer of het Unity Cloud Project en Relay zijn geactiveerd.");
            if (message.Contains("join", StringComparison.OrdinalIgnoreCase) || message.Contains("allocation", StringComparison.OrdinalIgnoreCase))
                return L("the join code has expired or is invalid.", "de joincode is verlopen of ongeldig.");
            return message;
        }

        bool Configure()
        {
            if (!ushort.TryParse(portInput.text, out ushort port))
            {
                SetStatus(L("The port must be a number between 1 and 65535.", "De poort moet een getal tussen 1 en 65535 zijn."), Color.red);
                return false;
            }
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null) return false;
            transport.UseWebSockets = false;
            transport.SetConnectionData(addressInput.text.Trim(), port, "0.0.0.0");
            return true;
        }

        void BeginClientConnection()
        {
            waitingForClientConnection = true;
            handlingConnectionFailure = false;
            connectionDeadline = Time.realtimeSinceStartup + ConnectionTimeoutSeconds;
        }

        void OnClientConnected(ulong clientId)
        {
            if (networkManager != null && networkManager.IsServer && clientId != networkManager.LocalClientId)
                Debug.Log($"Relay remote client connected: {clientId}.");
            if (networkManager == null || clientId != networkManager.LocalClientId) return;
            waitingForClientConnection = false;
            relayBusy = false;
            Debug.Log("Network client connected and was approved by the host.");
            if (!networkManager.IsHost) Debug.Log("[RelaySmoke] CLIENT_CONNECTED");
            if (menu != null) menu.gameObject.SetActive(false);
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (networkManager == null || networkManager.IsServer) return;
            if (!waitingForClientConnection && clientId != networkManager.LocalClientId) return;
            string reason = string.IsNullOrWhiteSpace(networkManager.DisconnectReason)
                ? L("The host could not be reached or closed the game.", "De host kon niet worden bereikt of heeft het spel afgesloten.")
                : networkManager.DisconnectReason;
            HandleConnectionFailure(reason);
        }

        void OnTransportFailure()
        {
            string reason = L("The network transport stopped. Check the internet connection and try again.",
                "De netwerkverbinding is gestopt. Controleer internet en probeer opnieuw.");
            HandleConnectionFailure(reason);
        }

        void HandleConnectionFailure(string reason)
        {
            if (handlingConnectionFailure) return;
            handlingConnectionFailure = true;
            waitingForClientConnection = false;
            relayBusy = false;
            Debug.LogWarning($"Network connection failed: {reason}");
            StartCoroutine(RestoreMenuAfterFailure(reason));
        }

        IEnumerator RestoreMenuAfterFailure(string reason)
        {
            if (networkManager != null && networkManager.IsListening)
                networkManager.Shutdown();
            yield return null;

            if (SceneManager.GetActiveScene().name != "IntroMenu")
            {
                if (networkManager != null) SessionRestartRunner.Begin(networkManager, "IntroMenu");
                yield break;
            }

            if (menu != null)
            {
                menu.gameObject.SetActive(true);
                ShowJoinPage();
                SetStatus(L("Connection failed: ", "Verbinden mislukt: ") + reason, Color.red);
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            handlingConnectionFailure = false;
        }

        void SetStatus(string message, Color color) { if (status != null) { status.text = message; status.color = color; } }
        static string L(string english, string dutch) => GameLocalization.Text(english, dutch);
        void OnDestroy()
        {
            UnsubscribeNetworkEvents();
            if (menu != null) Destroy(menu.gameObject);
        }
    }
}
