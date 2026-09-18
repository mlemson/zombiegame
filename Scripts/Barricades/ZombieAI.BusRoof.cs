using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using ZombieTown.Foundation;
using ZombieTown.Multiplayer;

namespace Unity.FPS.AI
{
    public partial class ZombieAI
    {
        bool m_BoardedOnRoof;
        IEnumerator CrawlOntoBusRoof(VehicleZombieAccessZone zone,BusDefenseLayout layout)
        {
            var vehicle=zone.VehicleRoot;Vector3 start=vehicle.InverseTransformPoint(transform.position);
            Vector3 end=new Vector3(Mathf.Clamp(start.x,-1.05f,1.05f),layout.roofFloorHeight+GroundOffset/vehicle.lossyScale.y,Mathf.Clamp(start.z,-4.2f,5.4f));
            // Pull vertically above the lip before moving knees through the roof edge.
            Vector3 lip=start;lip.y=end.y+.12f;float elapsed=0;byte last=255;
            while(elapsed<2.4f && BusOpeningValid(zone)){
                if(!NetworkRoundGate.IsOpen){yield return null;continue;}
                elapsed+=Time.deltaTime;float t=Mathf.Clamp01(elapsed/2.4f);
                byte pose=t<.5f?(byte)1:(byte)5;if(pose!=last){busAnimator?.SetWindowPose(pose);SetCrouching(pose==5);last=pose;}
                Vector3 position=t<.5f?Vector3.Lerp(start,lip,Mathf.SmoothStep(0,1,t*2)):Vector3.Lerp(lip,end,Mathf.SmoothStep(0,1,(t-.5f)*2));
                transform.position=vehicle.TransformPoint(position);transform.rotation=Quaternion.LookRotation(-zone.GetOutsideDirection());yield return null;
            }
            if(!BusOpeningValid(zone))yield break;
            m_BoardedOnRoof=true;m_BoardedVehicle=vehicle;m_BoardedLocalPosition=end;transform.position=vehicle.TransformPoint(end);
        }
        void UpdateBusRoof()
        {
            var layout=m_BoardedVehicle.GetComponent<BusDefenseLayout>();
            if(layout==null){RestoreNavigationAfterVehicleBoarding();return;}
            if(m_PlayerTransform==null){transform.position=m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition);return;}
            if(!layout.IsOnRoof(m_PlayerTransform.position)){
                VehicleZombieAccessZone best=null;float distance=float.PositiveInfinity;
                foreach(var zone in layout.openings){if(!zone.IsWindow || !zone.CanClaim(this))continue;float d=(zone.transform.position-transform.position).sqrMagnitude;if(d<distance){best=zone;distance=d;}}
                if(best!=null && layout.ContainsPassenger(m_PlayerTransform.position,.15f) && best.Claim(this)){
                    m_BoardedOnRoof=false;m_BoardedVehicle=null;m_VehicleAccessTarget=best;
                    m_LinkTraversalRoutine=StartCoroutine(TraverseClingingBusOpening(best));return;
                }
                if(best!=null && !layout.ContainsPassenger(m_PlayerTransform.position,.15f)){
                    best.GetTraversalCandidates(out var outside,out _);outside+=best.GetOutsideDirection()*1.7f;
                    if(NavMesh.SamplePosition(outside,out var ground,3,NavMesh.AllAreas)){
                        m_LinkTraversalRoutine=StartCoroutine(DropFromBusRoof(best,ground.position));return;
                    }
                }
                transform.position=m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition);return;
            }
            Vector3 target=m_BoardedVehicle.InverseTransformPoint(m_PlayerTransform.position);target.x=Mathf.Clamp(target.x,-1.2f,1.2f);target.z=Mathf.Clamp(target.z,-4.4f,5.6f);target.y=m_BoardedLocalPosition.y;
            float gap=Vector3.Distance(transform.position,m_PlayerTransform.position);bool walking=gap>AttackDistance;
            if(walking)m_BoardedLocalPosition=Vector3.MoveTowards(m_BoardedLocalPosition,target,MoveSpeed*(IsCrouching?CrouchSpeedMultiplier:1)*Time.deltaTime/m_BoardedVehicle.lossyScale.x);
            else if(!m_IsAttacking && Time.time>=m_NextAttackTime)PerformAttack();
            transform.position=m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition);
            Vector3 direction=m_PlayerTransform.position-transform.position;direction.y=0;if(direction.sqrMagnitude>.01f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(direction),Time.deltaTime*8);
            if(m_Animator!=null){m_Animator.SetBool(IsWalkingHash,walking);m_Animator.SetFloat(SpeedHash,walking?MoveSpeed:0);}
        }
        IEnumerator DropFromBusRoof(VehicleZombieAccessZone zone,Vector3 ground)
        {
            var vehicle=m_BoardedVehicle;Vector3 start=m_BoardedLocalPosition;
            Vector3 edge=vehicle.InverseTransformPoint(ground);edge.y=start.y;
            var animator=GetComponent<NetworkZombieAnimator>();animator?.SetWindowPose(3);
            float elapsed=0;
            while(elapsed<1.1f && !m_IsDead && vehicle!=null){
                if(!NetworkRoundGate.IsOpen){yield return null;continue;}
                elapsed+=Time.deltaTime;float t=Mathf.Clamp01(elapsed/1.1f);
                transform.position=t<.4f?vehicle.TransformPoint(Vector3.Lerp(start,edge,t/.4f)):Vector3.Lerp(vehicle.TransformPoint(edge),ground+Vector3.up*GroundOffset,Mathf.SmoothStep(0,1,(t-.4f)/.6f));
                yield return null;
            }
            m_BoardedOnRoof=false;m_BoardedVehicle=null;m_LinkTraversalRoutine=null;animator?.SetWindowPose(0);
            if(!m_IsDead){transform.position=ground+Vector3.up*GroundOffset;RestoreNavigationAfterVehicleBoarding();}
        }
    }
}
