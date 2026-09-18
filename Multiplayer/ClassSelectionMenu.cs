using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.FPS.Game;
using Unity.Netcode;

namespace ZombieTown.Multiplayer
{
    public sealed class ClassSelectionMenu : MonoBehaviour
    {
        const int PreviewLayer = 31;
        [SerializeField] PlayerClassController player;
        [Header("Menu Presentation")]
        [SerializeField] string menuTitle = "CHOOSE YOUR SURVIVOR";
        [SerializeField] string menuSubtitle = "Choose a class to start the round.";
        [SerializeField] Color previewBackground = new(.045f, .07f, .085f, 1f);
        [SerializeField] Color previewLightColor = new(1f, .92f, .82f, 1f);
        [SerializeField, Range(0f, 2f)] float previewLightIntensity = .55f;
        [SerializeField, Range(18f, 45f)] float previewFieldOfView = 28f;
        Canvas menu;
        readonly List<RenderTexture> previewTextures = new();
        readonly List<GameObject> previewWorlds = new();
        readonly List<Animator> previewAnimators = new();
        bool selectionMade;
        Text subtitle;
        Text lobbyStatus;
        Text roster;
        Button startButton;
        readonly Dictionary<PlayerArchetype, RectTransform> classCards = new();
        readonly Dictionary<PlayerArchetype, Image> classCardImages = new();
        readonly Dictionary<PlayerArchetype, Text> classCardStatus = new();
        PlayerArchetype? localChoice;
        float nextLobbyRefresh;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerClassController>();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        IEnumerator Start()
        {
            while (player != null && (!player.IsSpawned || !player.IsOwner))
            {
                if (player.IsSpawned && !player.IsOwner) yield break;
                yield return null;
            }
            if (player == null || !player.IsOwner) yield break;

            while (SceneManager.GetActiveScene().name != "ZombieTownScene" &&
                   SceneManager.GetActiveScene().name != "RadioOutpostScene" &&
                   SceneManager.GetActiveScene().name != "HarborEvacuationScene" &&
                   SceneManager.GetActiveScene().name != "RetreatDefenseScene" &&
                   SceneManager.GetActiveScene().name != "HarborViewCityScene" &&
                   SceneManager.GetActiveScene().name != "DeadOrbitScene" &&
                   SceneManager.GetActiveScene().name != "BusEscapeFinaleScene")
                yield return null;

            GetComponent<NetworkPlayerOwnership>()?.SetLocalGameplayReady(false);
            player.ClassConfirmed += OnClassConfirmed;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BuildMenu();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "RadioOutpostScene" || scene.name == "HarborEvacuationScene" ||
                scene.name == "RetreatDefenseScene" || scene.name == "HarborViewCityScene" ||
                scene.name == "DeadOrbitScene" || scene.name == "BusEscapeFinaleScene")
                StartCoroutine(ReopenForNextLevel());
        }

        IEnumerator ReopenForNextLevel()
        {
            yield return null;
            if (player == null || !player.IsSpawned || !player.IsOwner) yield break;

            CleanupPreviewResources();
            if (menu != null) Destroy(menu.gameObject);
            menu = null;
            selectionMade = false;
            localChoice = null;
            classCards.Clear();
            classCardImages.Clear();
            classCardStatus.Clear();
            GetComponent<NetworkPlayerOwnership>()?.SetLocalGameplayReady(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BuildMenu();
        }

        void BuildMenu()
        {
            menu = RuntimeMenuUI.CreateCanvas("Character Selection", 1000);
            RectTransform backdrop = RuntimeMenuUI.Block("Backdrop", menu.transform, RuntimeMenuUI.Background);
            RuntimeMenuUI.Stretch(backdrop);

            Text title = RuntimeMenuUI.Label("Title", backdrop,
                GameLocalization.Text(menuTitle, "KIES JE SURVIVOR"), 48,
                TextAnchor.MiddleCenter, RuntimeMenuUI.Accent);
            title.fontStyle = FontStyle.Bold;
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(.1f, .89f); titleRect.anchorMax = new Vector2(.9f, .97f);
            titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
            subtitle = RuntimeMenuUI.Label("Subtitle", backdrop,
                GameLocalization.Text(menuSubtitle, "Kies een klasse om de ronde te starten."),
                20, TextAnchor.MiddleCenter, RuntimeMenuUI.White);
            RectTransform subtitleRect = subtitle.rectTransform;
            subtitleRect.anchorMin = new Vector2(.12f, .84f); subtitleRect.anchorMax = new Vector2(.88f, .89f);
            subtitleRect.offsetMin = subtitleRect.offsetMax = Vector2.zero;

            lobbyStatus = RuntimeMenuUI.Label("Lobby Status", backdrop, string.Empty, 18,
                TextAnchor.MiddleCenter, RuntimeMenuUI.White);
            lobbyStatus.rectTransform.anchorMin = new Vector2(.15f, .79f);
            lobbyStatus.rectTransform.anchorMax = new Vector2(.85f, .83f);
            lobbyStatus.rectTransform.offsetMin = lobbyStatus.rectTransform.offsetMax = Vector2.zero;

            roster = RuntimeMenuUI.Label("Selected Survivors", backdrop, string.Empty, 16,
                TextAnchor.MiddleCenter, RuntimeMenuUI.White);
            roster.rectTransform.anchorMin = new Vector2(.15f, .735f);
            roster.rectTransform.anchorMax = new Vector2(.85f, .785f);
            roster.rectTransform.offsetMin = roster.rectTransform.offsetMax = Vector2.zero;

            RectTransform row = new GameObject("Class Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            row.SetParent(backdrop, false);
            row.anchorMin = new Vector2(.035f, .08f); row.anchorMax = new Vector2(.965f, .82f);
            row.offsetMin = row.offsetMax = Vector2.zero;
            HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 18;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childControlHeight = true;

            for (byte i = 0; i < 4; i++) AddClassCard(row, (PlayerArchetype)i, i);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                startButton = RuntimeMenuUI.Button("Start Game", backdrop,
                    GameLocalization.Text("START GAME", "START SPEL"), RuntimeMenuUI.Green, 18);
                RectTransform startRect = startButton.GetComponent<RectTransform>();
                startRect.anchorMin = new Vector2(.39f, .02f);
                startRect.anchorMax = new Vector2(.61f, .07f);
                startRect.offsetMin = startRect.offsetMax = Vector2.zero;
                startButton.onClick.AddListener(() => player?.RequestStartRoundRpc());
            }
            RefreshLobby();
        }

        void AddClassCard(Transform row, PlayerArchetype type, int index)
        {
            ArchetypeTuning tuning = player.GetArchetype(type);
            RectTransform card = RuntimeMenuUI.Block(type.ToString(), row, RuntimeMenuUI.Card);
            card.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            classCards[type] = card;
            classCardImages[type] = card.GetComponent<Image>();

            RawImage preview = new GameObject("Character Preview", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement)).GetComponent<RawImage>();
            preview.transform.SetParent(card, false);
            preview.color = Color.white;
            preview.GetComponent<LayoutElement>().preferredHeight = 400;
            preview.texture = CreatePreview(tuning.CharacterPrefab, index);

            string displayName = string.IsNullOrWhiteSpace(tuning.DisplayName) ? type.ToString() : tuning.DisplayName;
            Text name = RuntimeMenuUI.Label("Name", card, displayName.ToUpperInvariant(), 28, TextAnchor.MiddleCenter, RuntimeMenuUI.Accent);
            name.fontStyle = FontStyle.Bold;
            name.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            Text benefit = RuntimeMenuUI.Label("Benefit", card, Describe(type, tuning.Benefit), 18, TextAnchor.UpperCenter, RuntimeMenuUI.White);
            benefit.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;

            Button select = RuntimeMenuUI.Button("Select", card,
                GameLocalization.Text("SELECT ", "KIES ") + displayName.ToUpperInvariant(),
                type == PlayerArchetype.Medic ? RuntimeMenuUI.Green : RuntimeMenuUI.Accent, 18);
            select.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
            select.onClick.AddListener(() => Choose(type));

            Text status = RuntimeMenuUI.Label("Status", card, string.Empty, 15, TextAnchor.MiddleCenter, RuntimeMenuUI.Green);
            status.fontStyle = FontStyle.Bold;
            status.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
            classCardStatus[type] = status;
        }

        Texture CreatePreview(GameObject characterPrefab, int index)
        {
            RenderTexture texture = new(360, 480, 24, RenderTextureFormat.ARGB32) { name = $"{characterPrefab?.name ?? "Class"} Preview", antiAliasing = 2 };
            texture.Create();
            previewTextures.Add(texture);

            Vector3 origin = new(10000 + index * 100, 10000, 10000);
            GameObject world = new($"Preview World {index}");
            world.transform.position = origin;
            previewWorlds.Add(world);

            if (characterPrefab != null)
            {
                // POLYGON humanoids face +Z; the preview camera is on +Z, so identity shows their front.
                GameObject model = Instantiate(characterPrefab, origin, Quaternion.identity, world.transform);
                SetLayer(model, PreviewLayer);
                TonePreviewMaterials(model);
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
                foreach (Behaviour behaviour in model.GetComponentsInChildren<Behaviour>(true))
                    if (behaviour is not Animator) behaviour.enabled = false;
                Animator animator = model.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    previewAnimators.Add(animator);
                    animator.applyRootMotion = false;
                    animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    if (player.LocomotionController != null)
                    {
                        animator.runtimeAnimatorController = player.LocomotionController;
                        animator.SetFloat("Speed", 0f);
                        animator.SetBool("Grounded", true);
                        animator.Play("Locomotion", 0, 0f);
                    }
                }
            }

            GameObject cameraObject = new("Preview Camera", typeof(Camera));
            cameraObject.transform.SetParent(world.transform, false);
            cameraObject.transform.position = origin + new Vector3(0, 1.1f, 3.1f);
            cameraObject.transform.LookAt(origin + new Vector3(0, 1.05f, 0));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.cullingMask = 1 << PreviewLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = previewBackground;
            camera.fieldOfView = previewFieldOfView;
            camera.allowHDR = false;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 10;
            camera.targetTexture = texture;

            GameObject lightObject = new("Preview Light", typeof(Light));
            lightObject.transform.SetParent(world.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(35, 150, 0);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = previewLightIntensity;
            light.color = previewLightColor;
            light.cullingMask = 1 << PreviewLayer;
            return texture;
        }

        static string Describe(PlayerArchetype type, string configured)
        {
            return type switch
            {
                PlayerArchetype.Guardian => GameLocalization.Text("Regenerating armor\nSurvive more damage", "Regenererend pantser\nMeer schade overleven"),
                PlayerArchetype.Gunslinger => GameLocalization.Text("Starts with a powerful revolver\nHigh damage per shot", "Start met een krachtige revolver\nHoge schade per schot"),
                PlayerArchetype.Striker => GameLocalization.Text("Large sword + three-hit combo\nExtra health drops", "Groot zwaard + driedelige combo\nExtra health drops"),
                PlayerArchetype.Medic => GameLocalization.Text("Heal a teammate with H\nSupports the whole team", "Genees een teamgenoot met H\nOndersteunt de hele groep"),
                _ => configured
            };
        }

        void Choose(PlayerArchetype type)
        {
            // Allow switching characters freely until the host actually starts the round.
            if (player == null || !player.IsOwner || player.RoundStarted.Value) return;
            if (selectionMade && localChoice == type) return;
            selectionMade = true;
            localChoice = type;
            player.SelectClassRpc(type);
            if (subtitle != null) subtitle.text = GameLocalization.Text(
                "Confirming character and preparing a safe spawn...",
                "Character bevestigen en veilige spawn voorbereiden...");
            RefreshLobby();
        }

        void Update()
        {
            if (menu != null && menu.gameObject.activeSelf)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (Time.timeScale == 0f) Time.timeScale = 1f;
                KeepPreviewAnimationsPlaying();
                if (Time.unscaledTime >= nextLobbyRefresh)
                {
                    nextLobbyRefresh = Time.unscaledTime + .2f;
                    RefreshLobby();
                }
            }
            if (!selectionMade || player == null || !player.RoundStarted.Value) return;
            FinishSelection();
        }

        void OnClassConfirmed() => RefreshLobby();

        void RefreshLobby()
        {
            if (player == null || lobbyStatus == null || roster == null) return;
            PlayerClassController[] players = FindObjectsByType<PlayerClassController>();
            int selected = 0;
            string selectedNames = string.Empty;
            Dictionary<PlayerArchetype, int> pickedBy = new();
            foreach (PlayerClassController other in players)
            {
                if (other == null || !other.IsReady.Value) continue;
                selected++;
                bool isMe = other == player;
                selectedNames += (selectedNames.Length == 0 ? string.Empty : "   |   ") +
                    other.SelectedClass.Value + (isMe ? GameLocalization.Text(" (You)", " (Jij)") : string.Empty);
                pickedBy.TryGetValue(other.SelectedClass.Value, out int count);
                pickedBy[other.SelectedClass.Value] = count + 1;
            }
            roster.text = selectedNames.Length > 0
                ? GameLocalization.Text("Selected: ", "Gekozen: ") + selectedNames
                : GameLocalization.Text("No survivor selected yet", "Nog geen survivor gekozen");
            lobbyStatus.text = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost
                ? GameLocalization.Text($"{selected}/{players.Length} ready - start when everyone has chosen",
                    $"{selected}/{players.Length} klaar - start wanneer iedereen gekozen heeft")
                : GameLocalization.Text($"{selected}/{players.Length} ready - waiting for host",
                    $"{selected}/{players.Length} klaar - wachten op host");
            if (startButton != null)
                startButton.interactable = player.IsReady.Value && !player.RoundStarted.Value;

            foreach (KeyValuePair<PlayerArchetype, Image> entry in classCardImages)
            {
                bool isLocalPick = localChoice == entry.Key;
                if (entry.Value != null)
                    entry.Value.color = isLocalPick ? new Color(.2f, .6f, .32f, 1f) : RuntimeMenuUI.Card;
                if (classCardStatus.TryGetValue(entry.Key, out Text status) && status != null)
                {
                    pickedBy.TryGetValue(entry.Key, out int count);
                    status.text = isLocalPick
                        ? GameLocalization.Text("YOUR PICK", "JOUW KEUZE")
                        : count > 0 ? GameLocalization.Text($"PICKED ({count})", $"GEKOZEN ({count})") : string.Empty;
                }
            }
        }

        void KeepPreviewAnimationsPlaying()
        {
            foreach (Animator animator in previewAnimators)
            {
                if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                    continue;

                animator.speed = 1f;
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                // Some imported POLYGON idle clips are not marked Loop Time. Restart
                // those clips explicitly so the selection portraits never freeze.
                if (!animator.IsInTransition(0) && !state.loop && state.normalizedTime >= .98f)
                    animator.Play(state.shortNameHash, 0, 0f);
            }
        }

        void FinishSelection()
        {
            if (!selectionMade) return;
            selectionMade = false;
            if (menu != null) menu.gameObject.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayer(child.gameObject, layer);
        }

        static void TonePreviewMaterials(GameObject model)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.materials;
                foreach (Material material in materials)
                {
                    if (material == null) continue;
                    if (material.HasProperty("_BaseColor"))
                    {
                        Color color = material.GetColor("_BaseColor");
                        material.SetColor("_BaseColor", new Color(color.r * .72f, color.g * .72f, color.b * .72f, color.a));
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        Color color = material.color;
                        material.color = new Color(color.r * .72f, color.g * .72f, color.b * .72f, color.a);
                    }
                }
            }
        }

        void OnDestroy()
        {
            if (player != null) player.ClassConfirmed -= OnClassConfirmed;
            if (menu != null) Destroy(menu.gameObject);
            CleanupPreviewResources();
        }

        void CleanupPreviewResources()
        {
            foreach (GameObject world in previewWorlds) if (world != null) Destroy(world);
            foreach (RenderTexture texture in previewTextures)
                if (texture != null) { texture.Release(); Destroy(texture); }
            previewWorlds.Clear();
            previewTextures.Clear();
            previewAnimators.Clear();
        }
    }
}
