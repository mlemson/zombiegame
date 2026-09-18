using UnityEngine;

namespace ZombieTown.LevelTwo
{
    [DisallowMultipleComponent]
    public sealed class OutpostGuardAwarenessIndicator : MonoBehaviour
    {
        [SerializeField] OutpostGuardAI guard;
        [SerializeField] Vector3 localOffset = new(0f, 1.85f, 0f);
        TextMesh label;

        public void Initialize(OutpostGuardAI target)
        {
            guard = target;
            localOffset = new Vector3(0f, 1.85f, 0f);
            EnsureLabel();
            if (label != null) label.transform.localPosition = localOffset;
        }

        void Awake() => EnsureLabel();

        void EnsureLabel()
        {
            Transform existing = transform.Find("Awareness Symbol");
            GameObject symbol = existing != null ? existing.gameObject : new GameObject("Awareness Symbol");
            symbol.transform.SetParent(transform, false);
            symbol.transform.localPosition = localOffset;
            label = symbol.GetComponent<TextMesh>();
            // Unity's destroyed-object null behaves differently from C#'s ??,
            // so use the overloaded Unity null check before recreating it.
            if (label == null) label = symbol.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 96;
            label.characterSize = .035f;
            label.fontStyle = FontStyle.Bold;
            label.text = string.Empty;
        }

        void LateUpdate()
        {
            if (label == null) EnsureLabel();
            if (guard == null || label == null) return;
            switch (guard.CurrentState)
            {
                case OutpostGuardAI.GuardState.Suspicious:
                    label.text = "?";
                    label.color = new Color(1f, .82f, .12f, 1f);
                    break;
                case OutpostGuardAI.GuardState.Alert:
                case OutpostGuardAI.GuardState.Engaging:
                    label.text = "!";
                    label.color = new Color(1f, .18f, .08f, 1f);
                    break;
                default:
                    label.text = string.Empty;
                    break;
            }
        }

        void OnWillRenderObject()
        {
            if (label == null || Camera.current == null) return;
            label.transform.rotation = Quaternion.LookRotation(Camera.current.transform.forward,
                Camera.current.transform.up);
        }
    }
}
