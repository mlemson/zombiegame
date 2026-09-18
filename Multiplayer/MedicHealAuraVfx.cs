using UnityEngine;

namespace ZombieTown.Multiplayer
{
    /// <summary>Open rings stay visible from both inside and outside the healing radius.</summary>
    public sealed class MedicHealAuraVfx : MonoBehaviour
    {
        const int SegmentCount = 64;
        readonly LineRenderer[] rings = new LineRenderer[3];
        float radius;
        Light glow;
        Material lineMaterial;

        public static GameObject Create(Transform parent, float radius)
        {
            GameObject root = new("Medic Heal Aura");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * .08f;
            root.AddComponent<MedicHealAuraVfx>().Initialize(radius);
            return root;
        }

        void Initialize(float newRadius)
        {
            radius = Mathf.Max(1f, newRadius);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ??
                            Shader.Find("Standard");
            lineMaterial = new Material(shader) { name = "Medic Aura Glow (Runtime)" };
            Color bright = new(.18f, 1f, .58f, 1f);
            lineMaterial.color = bright;
            if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", bright);
            if (lineMaterial.HasProperty("_EmissionColor"))
            {
                lineMaterial.SetColor("_EmissionColor", bright * 2.2f);
                lineMaterial.EnableKeyword("_EMISSION");
            }

            for (int i = 0; i < rings.Length; i++)
            {
                GameObject child = new($"Healing Ring {i + 1}");
                child.transform.SetParent(transform, false);
                LineRenderer ring = child.AddComponent<LineRenderer>();
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.positionCount = SegmentCount;
                ring.widthMultiplier = .045f + i * .012f;
                ring.sharedMaterial = lineMaterial;
                ring.numCornerVertices = 2;
                ring.numCapVertices = 2;
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;
                rings[i] = ring;
            }

            GameObject lightObject = new("Healing Glow", typeof(Light));
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = Vector3.up * .65f;
            glow = lightObject.GetComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(.2f, 1f, .55f);
            glow.range = radius * 1.15f;
            glow.intensity = 1.15f;
            glow.shadows = LightShadows.None;
        }

        void Update()
        {
            float time = Time.time;
            for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
            {
                float phase = time * (1.15f + ringIndex * .18f) + ringIndex * 2.1f;
                float ringRadius = radius * (.76f + ringIndex * .11f) + Mathf.Sin(phase) * .12f;
                float height = .08f + ringIndex * .24f + Mathf.Sin(phase * 1.3f) * .08f;
                float rotation = phase * (ringIndex % 2 == 0 ? 18f : -14f) * Mathf.Deg2Rad;
                Color color = new(.16f, 1f, .56f, .58f + Mathf.Sin(phase) * .16f);
                rings[ringIndex].startColor = rings[ringIndex].endColor = color;

                for (int i = 0; i < SegmentCount; i++)
                {
                    float angle = (i / (float)SegmentCount) * Mathf.PI * 2f + rotation;
                    float ripple = Mathf.Sin(angle * 4f + phase * 2f) * .035f;
                    rings[ringIndex].SetPosition(i,
                        new Vector3(Mathf.Cos(angle) * ringRadius, height + ripple,
                            Mathf.Sin(angle) * ringRadius));
                }
            }
            if (glow != null) glow.intensity = 1.05f + Mathf.Sin(time * 3.5f) * .2f;
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
