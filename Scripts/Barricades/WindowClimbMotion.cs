using UnityEngine;
namespace ZombieTown.Foundation
{
    public static class WindowClimbMotion
    {
        static readonly RaycastHit[] hits=new RaycastHit[32];
        public static float FindSillLift(Vector3 start,Vector3 end){
            Vector3 direction=Vector3.ProjectOnPlane(end-start,Vector3.up);float length=direction.magnitude;if(length<.1f)return 0;
            bool Solid(float height){int count=Physics.RaycastNonAlloc(start+Vector3.up*height,direction/length,hits,length,~0,QueryTriggerInteraction.Ignore);for(int i=0;i<count;i++)if(hits[i].collider.GetComponentInParent<Unity.FPS.AI.ZombieAI>()==null && hits[i].collider.GetComponentInParent<ZombieTown.Multiplayer.PlayerClassController>()==null)return true;return count==hits.Length;}
            for(float lift=.05f;lift<2.4f;lift+=.1f)if(!Solid(lift) && !Solid(lift+.35f) && !Solid(lift+.65f))return lift<.15f?0:lift+.05f;
            return .8f;
        }
        // Lift outside the sill, cross at sill height, then land inside.
        public static Vector3 Position(Vector3 start,Vector3 end,float lift,float progress){
            float height=Mathf.Max(start.y,end.y)+Mathf.Max(0,lift);
            Vector3 upperStart=new Vector3(start.x,height,start.z),upperEnd=new Vector3(end.x,height,end.z);
            if(progress<.28f)return Vector3.Lerp(start,upperStart,Mathf.SmoothStep(0,1,progress/.28f));
            if(progress<.76f)return Vector3.Lerp(upperStart,upperEnd,Mathf.SmoothStep(0,1,(progress-.28f)/.48f));
            return Vector3.Lerp(upperEnd,end,Mathf.SmoothStep(0,1,(progress-.76f)/.24f));
        }
    }
}
