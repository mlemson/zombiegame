using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.FPS.Game;

namespace ZombieTown.Multiplayer
{
    public sealed class RelayHostCodePresenter : MonoBehaviour
    {
        static RelayHostCodePresenter instance;
        string joinCode;
        float createdAt;

        public static void Show(string code)
        {
            if (instance == null)
            {
                GameObject root = new("Persistent Relay Host Code");
                DontDestroyOnLoad(root);
                instance = root.AddComponent<RelayHostCodePresenter>();
            }
            instance.joinCode = code;
            instance.createdAt = Time.realtimeSinceStartup;
            instance.BuildOrRefresh();
            instance.CopyCode();
        }

        void BuildOrRefresh()
        {
            Canvas existing = GetComponentInChildren<Canvas>(true);
            if (existing != null) Destroy(existing.gameObject);
            Canvas canvas = RuntimeMenuUI.CreateCanvas("Relay Host Code", 1250);
            canvas.transform.SetParent(transform, false);
            RectTransform panel = RuntimeMenuUI.Block("Relay Code Panel", canvas.transform,
                new Color(.025f, .045f, .055f, .96f));
            panel.anchorMin = new Vector2(.755f, .82f);
            panel.anchorMax = new Vector2(.985f, .975f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            Text code = RuntimeMenuUI.Label("Code", panel,
                GameLocalization.Text($"ONLINE HOST CODE\n{joinCode}\nSend this to your friends\nC = copy again",
                    $"ONLINE HOSTCODE\n{joinCode}\nStuur deze naar je vrienden\nC = opnieuw kopieren"),
                18, TextAnchor.MiddleCenter, RuntimeMenuUI.Accent);
            code.fontStyle = FontStyle.Bold;
            RuntimeMenuUI.Stretch(code.rectTransform, 10f, 10f, 6f, 6f);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) CopyCode();
            NetworkManager manager = NetworkManager.Singleton;
            if (Time.realtimeSinceStartup > createdAt + 2f && (manager == null || !manager.IsListening || !manager.IsHost))
                Destroy(gameObject);
        }

        void CopyCode()
        {
            if (!string.IsNullOrEmpty(joinCode)) GUIUtility.systemCopyBuffer = joinCode;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
