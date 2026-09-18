using UnityEngine;
using Unity.FPS.Gameplay;
using ZombieTown.Progression;

namespace ZombieTown.Foundation
{
    // Presentation only: prices and availability always come from the existing terminal.
    public sealed class GameplayPointSign:MonoBehaviour
    {
        public Transform source;
        public TextMesh label;
        public Light accent;
        public Renderer visibilitySource;
        public string caption;
        public bool weaponPoint;
        [Min(1)] public float visibleDistance=16;
        Camera view;
        Renderer textRenderer;
        float nextRefresh;
        public void RefreshContent(){
            if(label==null)return;
            if(textRenderer==null)textRenderer=label.GetComponent<Renderer>();
            var terminal=source!=null?source.GetComponent<WeaponShopTerminal>():null;
            if(weaponPoint && terminal!=null)label.text=terminal.DisplayName+"\n"+(terminal.IsAvailable?"$"+terminal.Price+(terminal.WeaponPrefab!=null && terminal.WeaponPrefab.IsMeleeWeapon?"":"  |  MUNITIE $"+terminal.AmmoPrice):"NOG VERGRENDELD");
            else if(weaponPoint && source!=null && source.TryGetComponent<WeaponPickup>(out var pickup) && pickup.WeaponPrefab!=null)label.text=pickup.WeaponPrefab.WeaponName+"\nWAPENPUNT";
            else label.text=caption;
            label.text=Unity.FPS.Game.WorldTextLocalization.Translate(label.text);
        }
        void OnEnable(){RefreshContent();}
        void LateUpdate(){
            if(Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+.3f;RefreshContent();if(view==null || !view.isActiveAndEnabled)view=Camera.main;}
            bool show=view!=null && source!=null && source.GetComponent<ZombieTown.Traversal.ClimbableLadder>()==null && source.gameObject.activeInHierarchy && (visibilitySource==null || visibilitySource.enabled) && (view.transform.position-transform.position).sqrMagnitude<=visibleDistance*visibleDistance;
            if(textRenderer!=null)textRenderer.enabled=show;
            if(accent!=null)accent.enabled=show;
            if(show){Vector3 direction=transform.position-view.transform.position;direction.y=0;if(direction.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(direction,Vector3.up);}
        }
    }
}
