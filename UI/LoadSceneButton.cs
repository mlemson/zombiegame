using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

namespace Unity.FPS.UI
{
    public class LoadSceneButton : MonoBehaviour
    {
        public string SceneName = "";

        private InputAction m_SubmitAction;
        
        void Start()
        {
            m_SubmitAction = InputSystem.actions.FindAction("UI/Submit");
            m_SubmitAction.Enable();
            GameLocalization.LanguageChanged += RefreshLabel;
            RefreshLabel();
        }

        void RefreshLabel()
        {
            if (SceneName != "ZombieTownScene") return;
            TMP_Text label = GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = GameLocalization.Text("PLAY AGAIN", "OPNIEUW SPELEN");
        }

        void OnDestroy() => GameLocalization.LanguageChanged -= RefreshLabel;
        
        void Update()
        {
            if (EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == gameObject
                && m_SubmitAction.WasPressedThisFrame())
            {
                LoadTargetScene();
            }
        }

        public void LoadTargetScene()
        {
            Time.timeScale = 1f;
            SceneLoadCoordinator.LoadScene(SceneName);
        }
    }
}
