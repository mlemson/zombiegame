using UnityEngine;

namespace ZombieTown.UI
{
    public static class PrefabIconSpriteRenderer
    {
        const int PreviewLayer = 31;

        public static Sprite Render(GameObject prefab, out Texture2D texture, int size = 128)
        {
            texture = null;
            if (prefab == null) return null;

            GameObject icon = Object.Instantiate(prefab, new Vector3(10000f, 10000f, 10000f), Quaternion.identity);
            icon.hideFlags = HideFlags.HideAndDontSave;
            SetLayer(icon.transform, PreviewLayer);

            Renderer[] renderers = icon.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                DestroyPreviewObject(icon);
                return null;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            GameObject cameraObject = new("Prefab Icon Camera", typeof(Camera));
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.cullingMask = 1 << PreviewLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.18f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = Mathf.Max(10f, bounds.size.magnitude * 4f);
            camera.transform.position = bounds.center - Vector3.forward * Mathf.Max(2f, bounds.size.magnitude * 1.5f);
            camera.transform.LookAt(bounds.center, Vector3.up);

            GameObject lightObject = new("Prefab Icon Light", typeof(Light));
            lightObject.hideFlags = HideFlags.HideAndDontSave;
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.cullingMask = 1 << PreviewLayer;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

            RenderTexture renderTexture = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = prefab.name + " UI Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
            texture.Apply(false, true);
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(renderTexture);
            DestroyPreviewObject(cameraObject);
            DestroyPreviewObject(lightObject);
            DestroyPreviewObject(icon);

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), size);
        }

        static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }

        static void DestroyPreviewObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}

