using System.Collections;
using UnityEngine;
using ZombieTown.Foundation;
using ZombieTown.Multiplayer;

namespace Unity.FPS.AI
{
    public partial class ZombieAI
    {
        VehicleZombieAccessZone clingingOpening;
        NetworkZombieAnimator busAnimator;
        bool busSavedRootMotion;

        void ReleaseBusOpening()
        {
            if (clingingOpening == null) return;
            clingingOpening.Release(this);
            clingingOpening = null;
            if (m_Animator != null) m_Animator.applyRootMotion = busSavedRootMotion;
            busAnimator?.SetBusGrip(null);
            IsClimbing = false;
        }

        bool BusOpeningValid(VehicleZombieAccessZone zone) => !m_IsDead && zone != null &&
            zone.isActiveAndEnabled && zone.VehicleRoot != null && m_PlayerTransform != null &&
            IsPointWithinVehicleBounds(zone.VehicleRoot, m_PlayerTransform.position, .35f);

        IEnumerator TraverseClingingBusOpening(VehicleZombieAccessZone zone)
        {
            clingingOpening = zone;
            busAnimator = GetComponent<NetworkZombieAnimator>();
            var layout=zone.VehicleRoot.GetComponent<BusDefenseLayout>();
            bool enteringRoof=zone.IsWindow && layout!=null && layout.IsOnRoof(m_PlayerTransform.position);
            busAnimator?.SetBusGrip(zone.Barricade,enteringRoof);
            bool wasCrouching = IsCrouching;
            busSavedRootMotion = m_Animator != null && m_Animator.applyRootMotion;
            if (m_Animator != null) m_Animator.applyRootMotion = false;
            m_VehicleAccessCrossing = true;
            if (m_NavAgent != null && m_NavAgent.enabled)
            {
                if (m_NavAgent.isOnNavMesh) { m_NavAgent.ResetPath(); m_NavAgent.isStopped = true; }
                m_NavAgent.enabled = false;
            }
            bool completed = false;
            try
            {
                Vector3 start = transform.position;
                float elapsed = 0;
                if (zone.IsWindow)
                {
                    IsClimbing = true;
                    busAnimator?.SetWindowPose(3);
                    while (elapsed < zone.JumpSeconds && BusOpeningValid(zone))
                    {
                        if (!NetworkRoundGate.IsOpen) { yield return null; continue; }
                        elapsed += Time.deltaTime;
                        float t = Mathf.Clamp01(elapsed / zone.JumpSeconds);
                        transform.position = Vector3.Lerp(start, zone.GetLatchPosition(enteringRoof), Mathf.SmoothStep(0, 1, t)) +
                            Vector3.up * (Mathf.Sin(t * Mathf.PI) * .35f);
                        transform.rotation = Quaternion.LookRotation(-zone.GetOutsideDirection());
                        yield return null;
                    }
                    elapsed = 0;
                    float nextAttack = 0;
                    busAnimator?.SetWindowPose(4);
                    while (BusOpeningValid(zone) && (elapsed < zone.HangSeconds || (zone.IsBlocked && !enteringRoof)))
                    {
                        transform.position = zone.GetLatchPosition(enteringRoof);
                        transform.rotation = Quaternion.LookRotation(-zone.GetOutsideDirection());
                        if (NetworkRoundGate.IsOpen)
                        {
                            elapsed += Time.deltaTime;
                            if (zone.IsBlocked && !enteringRoof && Time.time >= nextAttack)
                            {
                                nextAttack = Time.time + Mathf.Max(.3f, AttackInterval);
                                zone.Barricade.AttackVehicleBoard(this);
                                busAnimator?.PlayBusBoardAttack();
                            }
                        }
                        yield return null;
                    }
                }
                if (!BusOpeningValid(zone)) yield break;
                if(enteringRoof){
                    yield return CrawlOntoBusRoof(zone,layout);
                    completed=!m_IsDead && m_BoardedOnRoof;
                    yield break;
                }
                // Keep all crossing positions in vehicle space so accelerating/turning cannot leave the zombie behind.
                Vector3 localStart = zone.VehicleRoot.InverseTransformPoint(transform.position);
                elapsed = 0;
                byte lastPose = 255;
                float duration = zone.IsWindow ? zone.CrawlSeconds : 1.1f;
                while (elapsed < duration && BusOpeningValid(zone))
                {
                    if (!NetworkRoundGate.IsOpen) { yield return null; continue; }
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    zone.GetTraversalCandidates(out _, out Vector3 inside);
                    inside += Vector3.up * GroundOffset;
                    Vector3 movingStart = zone.VehicleRoot.TransformPoint(localStart);
                    float lift = zone.IsWindow ? Mathf.Max(0, zone.Box.bounds.min.y + .06f - Mathf.Max(movingStart.y, inside.y)) : 0;
                    transform.position = WindowClimbMotion.Position(movingStart, inside, lift, t);
                    transform.rotation = Quaternion.LookRotation(-zone.GetOutsideDirection());
                    byte pose = zone.IsWindow ? (t < .28f ? (byte)1 : (byte)5) : (byte)2;
                    if (pose != lastPose) { busAnimator?.SetWindowPose(pose); SetCrouching(pose == 5 || pose == 2); lastPose = pose; }
                    yield return null;
                }
                if (!BusOpeningValid(zone)) yield break;
                zone.GetTraversalCandidates(out _, out Vector3 end);
                m_BoardedVehicle = zone.VehicleRoot;
                m_BoardedOnRoof=false;
                m_BoardedLocalPosition = m_BoardedVehicle.InverseTransformPoint(end + Vector3.up * GroundOffset);
                transform.position = m_BoardedVehicle.TransformPoint(m_BoardedLocalPosition);
                completed = true;
            }
            finally
            {
                ReleaseBusOpening();
                if(completed)busAnimator?.SetWindowPose(2);
                RestorePostureAfterTraversal(wasCrouching);
                ClearVehicleAccessTarget();
                m_LinkTraversalRoutine = null;
                if (!completed && !m_IsDead) RestoreNavigationAfterVehicleBoarding();
            }
        }
    }
}
