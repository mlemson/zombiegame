using UnityEngine;

namespace ZombieTown.Traversal
{
    // Keep the shared, depth-tested sign material bound to the dynamic font atlas,
    // including after a scene load or an atlas rebuild. No per-frame work.
    [ExecuteAlways, RequireComponent(typeof(TextMesh)), DisallowMultipleComponent]
    public sealed class WorldSignText : MonoBehaviour
    {
        TextMesh label;
        Renderer textRenderer;
        string originalText, translatedText;
        float nextTranslation;
        Camera view;
        void Update(){
            if(!Application.isPlaying || label==null || Time.realtimeSinceStartup<nextTranslation)return;
            nextTranslation=Time.realtimeSinceStartup+.25f;
            if(label.text!=translatedText)originalText=label.text;
            translatedText=Unity.FPS.Game.WorldTextLocalization.Translate(originalText);
            if(label.text!=translatedText)label.text=translatedText;
        }

        void OnEnable()
        {
            label = GetComponent<TextMesh>();
            textRenderer = GetComponent<Renderer>();
            Font.textureRebuilt += RefreshAtlas;
            RefreshAtlas(label.font);
        }

        void OnDisable() => Font.textureRebuilt -= RefreshAtlas;

        void LateUpdate()
        {
            if(view==null || !view.isActiveAndEnabled)view=Camera.main;
            if(view==null)return;
            Vector3 direction=transform.position-view.transform.position;
            direction.y=0;
            if(direction.sqrMagnitude>.0001f)transform.rotation=Quaternion.LookRotation(direction,Vector3.up);
        }

        void RefreshAtlas(Font font)
        {
            if (font == null || label == null || label.font != font || textRenderer == null) return;
            Material material = textRenderer.sharedMaterial;
            if (material != null && material.shader != null && material.shader.name == "ZombieTown/World Sign Text")
                material.mainTexture = font.material.mainTexture;
        }
    }
}
