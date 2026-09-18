using UnityEngine;
using Unity.FPS.Game;
namespace ZombieTown.Foundation
{
    public sealed class RuntimeWeaponUpgrade : MonoBehaviour
    {
        WeaponController weapon;
        float delay,damage; int magazine,reserve;
        Material[][] materials; Renderer[] renderers;
        readonly System.Collections.Generic.List<Material> glowMaterials=new();
        WeaponUpgradeDefinition.Tier appliedTier;
        bool initialized;
        public float AppliedGlowIntensity {get;private set;}
        bool IsHand(Renderer renderer){for(var t=renderer.transform;t!=null && t!=transform;t=t.parent){string n=t.name.ToLowerInvariant();if(n.Contains("hands") || n.Contains("arms"))return true;}return false;}
        void ClearGlow(){foreach(var material in glowMaterials)if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}glowMaterials.Clear();}
        void OnDestroy()=>ClearGlow();
        void Awake() { Initialize(); }
        void Initialize(){if(weapon!=null)return;weapon=GetComponent<WeaponController>(); delay=weapon.DelayBetweenShots; damage=weapon.MeleeDamage; magazine=weapon.ClipSize; reserve=weapon.MaxAmmo; renderers=GetComponentsInChildren<Renderer>(true); materials=new Material[renderers.Length][]; for(int i=0;i<renderers.Length;i++) materials[i]=renderers[i].sharedMaterials;}
        public void Apply(WeaponUpgradeDefinition.Tier tier) {
            Initialize();
            if(weapon==null) return;
            if(initialized && appliedTier==tier)return;
            initialized=true;appliedTier=tier;ClearGlow();AppliedGlowIntensity=tier?.glowIntensity??0;
            weapon.DelayBetweenShots=delay*(tier?.attackDelayMultiplier??1);
            weapon.MeleeDamage=damage*(tier?.damageMultiplier??1);
            weapon.RuntimeDamageMultiplier=tier?.damageMultiplier??1;
            weapon.ClipSize=Mathf.Max(1,Mathf.CeilToInt(magazine*(tier?.magazineMultiplier??1)));
            weapon.MaxAmmo=Mathf.Max(1,Mathf.CeilToInt(reserve*(tier?.reserveMultiplier??1)));
            var audio=GetComponent<WeaponAudio>(); if(audio!=null) { audio.UpgradePitchOffset=tier?.audioPitchOffset??0; audio.UpgradeVolumeOffset=tier?.audioVolumeOffset??0; }
            for(int i=0;i<renderers.Length;i++) if(renderers[i]!=null && !IsHand(renderers[i])) {
                var resolved=(Material[])materials[i].Clone();
                if(tier!=null)for(int j=0;j<resolved.Length;j++){
                    var source=tier.visualPreset!=null?tier.visualPreset:resolved[j];if(source==null)continue;
                    var glowing=new Material(source);glowMaterials.Add(glowing);resolved[j]=glowing;
                    if(glowing.HasProperty("_EmissionColor")){glowing.EnableKeyword("_EMISSION");glowing.SetColor("_EmissionColor",tier.glowColor*tier.glowIntensity);if(glowing.HasProperty("_EmissionMap"))glowing.SetTexture("_EmissionMap",Texture2D.whiteTexture);}
                }
                renderers[i].sharedMaterials=resolved;
            }
        }
    }
}
