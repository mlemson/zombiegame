using UnityEngine;

namespace ZombieTown.LevelThree
{
    [DisallowMultipleComponent]
    public sealed class HarborPowerSwitch : MonoBehaviour
    {
        [SerializeField] Light indicator;
        [SerializeField] Renderer indicatorRenderer;
        [SerializeField] Color inactiveColor = new(.75f, .08f, .035f);
        [SerializeField] Color activeColor = new(.15f, 1f, .25f);

        public void SetActivated(bool activated)
        {
            if (indicator != null)
            {
                indicator.enabled = true;
                indicator.color = activated ? activeColor : inactiveColor;
            }
            if (indicatorRenderer != null)
            {
                MaterialPropertyBlock block = new();
                indicatorRenderer.GetPropertyBlock(block);
                Color color = activated ? activeColor : inactiveColor;
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                block.SetColor("_EmissionColor", color * 1.5f);
                indicatorRenderer.SetPropertyBlock(block);
            }
        }
    }
}
