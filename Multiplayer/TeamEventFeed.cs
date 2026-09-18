using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieTown.Multiplayer
{
    /// <summary>Small local HUD fed by already-synchronized player state.</summary>
    public sealed class TeamEventFeed : MonoBehaviour
    {
        struct Entry
        {
            public string Message;
            public Color Color;
            public float Expires;
        }

        static TeamEventFeed instance;
        readonly List<Entry> entries = new();
        Text label;

        public static void Show(string message, Color color)
        {
            Ensure().Add(message, color);
        }

        static TeamEventFeed Ensure()
        {
            if (instance != null) return instance;
            Canvas canvas = RuntimeMenuUI.CreateCanvas("Team Event Feed", 870);
            DontDestroyOnLoad(canvas.gameObject);
            instance = canvas.gameObject.AddComponent<TeamEventFeed>();
            RectTransform panel = RuntimeMenuUI.Block("Feed", canvas.transform, new Color(.02f, .03f, .04f, .58f));
            panel.anchorMin = new Vector2(.015f, .055f);
            panel.anchorMax = new Vector2(.34f, .2f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            instance.label = RuntimeMenuUI.Label("Messages", panel, string.Empty, 19,
                TextAnchor.LowerLeft, RuntimeMenuUI.White);
            RuntimeMenuUI.Stretch(instance.label.rectTransform, 12, 12, 8, 8);
            panel.gameObject.SetActive(false);
            return instance;
        }

        void Add(string message, Color color)
        {
            entries.Add(new Entry { Message = message, Color = color, Expires = Time.unscaledTime + 6f });
            while (entries.Count > 3) entries.RemoveAt(0);
            Refresh();
        }

        void Update()
        {
            bool changed = false;
            for (int i = entries.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= entries[i].Expires)
                {
                    entries.RemoveAt(i);
                    changed = true;
                }
            if (changed) Refresh();
        }

        void Refresh()
        {
            label.transform.parent.gameObject.SetActive(entries.Count > 0);
            label.text = string.Join("\n", entries.ConvertAll(entry => entry.Message));
            label.color = entries.Count > 0 ? entries[entries.Count - 1].Color : RuntimeMenuUI.White;
        }
    }
}
