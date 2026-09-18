using System.Collections;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

using ZombieTown.Multiplayer;

namespace ZombieTown.Traversal
{
    [DisallowMultipleComponent]
    public sealed class PlayerLadderClimber : MonoBehaviour
    {
        ClimbableLadder nearbyLadder;
        ClimbableLadder activeLadder;
        PlayerCharacterController locomotion;
        PlayerInputHandler input;
        CharacterController characterController;
        PlayerClassController networkPlayer;
        PlayerWeaponsManager weapons;
        WeaponController weaponBeforeClimb;
        bool weaponsWereEnabled;
        bool controllerCollisionStateCaptured;
        bool controllerCollisionsWereEnabled;
        bool climbing;
        bool exitingAtTop;
        float canClimbAgainAt;
        Coroutine topExitRoutine;
        Collider[] nearbyLadderHits = new Collider[16];
        RaycastHit[] ladderLookHits = new RaycastHit[12];
        float nextLadderSearch;
        bool showClimbPrompt;
        Matrix4x4 previousLadderWorldToLocal;

        void Awake()
        {
            locomotion = GetComponent<PlayerCharacterController>();
            input = GetComponent<PlayerInputHandler>();
            characterController = GetComponent<CharacterController>();
            networkPlayer = GetComponent<PlayerClassController>();
            weapons = GetComponent<PlayerWeaponsManager>();
        }

        void Update()
        {
            if (!CanControlLocally() || locomotion == null || input == null || characterController == null)
            {
                return;
            }

            Vector3 move = input.GetMoveInput();
            if (exitingAtTop)
            {
                return;
            }

            if (!climbing)
            {
                ClimbableLadder candidateLadder = FindLookedAtLadder();
                if (candidateLadder == null)
                {
                    candidateLadder = FindNearbyLadder();
                }
                nearbyLadder = candidateLadder;

                Transform view = locomotion.PlayerCamera != null ? locomotion.PlayerCamera.transform : transform;
                bool guardInteractionHasPriority = networkPlayer != null && networkPlayer.HasNearbyGuardTakedown;
                bool canStartClimbing = !guardInteractionHasPriority && Time.time >= canClimbAgainAt &&
                                        nearbyLadder != null &&
                                        nearbyLadder.CanPlayerStartClimbing(transform.position, view.forward);
                showClimbPrompt = canStartClimbing && !GameplayInteraction.Carrying;

                bool interact = Keyboard.current != null && Unity.FPS.Game.GameplayInteraction.Pressed;
                if (canStartClimbing && interact)
                    BeginClimb(nearbyLadder);
                return;
            }

            if (input.GetJumpInputDown())
            {
                EndClimb(activeLadder.ExitDirection(transform.position.y >= activeLadder.TopPoint.y - .5f), true);
                return;
            }

            // Account for a ladder mounted on a translating or rotating vehicle.
            Vector3 carried=activeLadder.transform.TransformPoint(previousLadderWorldToLocal.MultiplyPoint3x4(transform.position));
            characterController.Move(carried-transform.position);
            Vector3 closest = activeLadder.ClosestClimbPoint(transform.position);
            Vector3 climbDirection = activeLadder.ClimbDirection;
            Vector3 snap = Vector3.ProjectOnPlane(closest - transform.position, climbDirection) * 8f;
            float vertical = Mathf.Abs(move.z) > .05f ? move.z : 0f;
            Vector3 climbVelocity = climbDirection * vertical * activeLadder.ClimbSpeed;
            characterController.Move((climbVelocity + snap) * Time.deltaTime);
            previousLadderWorldToLocal=activeLadder.transform.worldToLocalMatrix;
            FaceLadder(activeLadder);
            locomotion.CharacterVelocity = climbVelocity;

            bool reachedTop = transform.position.y >= activeLadder.TopPoint.y - .25f && vertical > 0f;
            bool reachedBottom = transform.position.y <= activeLadder.BottomPoint.y + .2f && vertical < 0f;
            if (reachedTop)
            {
                topExitRoutine = StartCoroutine(FinishTopExit(activeLadder));
                return;
            }
            if (reachedBottom)
                EndClimb(activeLadder.ExitDirection(false), false);
        }

        public void SetNearbyLadder(ClimbableLadder ladder, bool inside)
        {
            if (inside && !climbing) nearbyLadder = ladder;
            else if (!inside && nearbyLadder == ladder) nearbyLadder = null;
        }

        ClimbableLadder FindNearbyLadder()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * .7f, 2.8f,
                nearbyLadderHits, -1, QueryTriggerInteraction.Collide);
            // Dense railings can fill the buffer before the ladder trigger is
            // returned. Grow only on saturation; subsequent frames reuse it.
            while (count == nearbyLadderHits.Length)
            {
                System.Array.Resize(ref nearbyLadderHits, nearbyLadderHits.Length * 2);
                count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * .7f, 2.8f,
                    nearbyLadderHits, -1, QueryTriggerInteraction.Collide);
            }
            ClimbableLadder closest = null;
            float closestSqr = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider hit = nearbyLadderHits[i];
                if (hit == null) continue;
                ClimbableLadder candidate = hit.GetComponentInParent<ClimbableLadder>();
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                float sqr = (candidate.ClosestClimbPoint(transform.position) - transform.position).sqrMagnitude;
                if (sqr >= closestSqr) continue;
                closest = candidate;
                closestSqr = sqr;
            }
            return closest;
        }

        ClimbableLadder FindLookedAtLadder()
        {
            Transform view = locomotion != null && locomotion.PlayerCamera != null
                ? locomotion.PlayerCamera.transform : transform;
            int count = Physics.SphereCastNonAlloc(view.position, .16f, view.forward,
                ladderLookHits, 3.4f, -1, QueryTriggerInteraction.Collide);
            while (count == ladderLookHits.Length)
            {
                System.Array.Resize(ref ladderLookHits, ladderLookHits.Length * 2);
                count = Physics.SphereCastNonAlloc(view.position, .16f, view.forward,
                    ladderLookHits, 3.4f, -1, QueryTriggerInteraction.Collide);
            }
            ClimbableLadder closest = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider hit = ladderLookHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                ClimbableLadder ladder = hit.GetComponentInParent<ClimbableLadder>();
                if (ladder == null || !ladder.isActiveAndEnabled || ladderLookHits[i].distance >= closestDistance)
                    continue;
                closest = ladder;
                closestDistance = ladderLookHits[i].distance;
            }
            return closest;
        }

        void BeginClimb(ClimbableLadder ladder)
        {
            GameplayInteraction.Consume();
            showClimbPrompt = false;
            activeLadder = ladder;
            climbing = true;
            weaponBeforeClimb = weapons != null ? weapons.GetActiveWeapon() : null;
            weaponsWereEnabled = weapons != null && weapons.enabled;
            weaponBeforeClimb?.ShowWeapon(false);
            if (weaponsWereEnabled) weapons.enabled = false;
            locomotion.CharacterVelocity = Vector3.zero;
            locomotion.enabled = false;

            // Rails, walls and the ladder mesh used to block the one-frame snap,
            // leaving the capsule offset to one side and therefore looking at the
            // ladder diagonally. The ladder owns movement while climbing, so keep
            // controller collisions off until the exit has finished.
            controllerCollisionsWereEnabled = characterController.detectCollisions;
            controllerCollisionStateCaptured = true;
            characterController.detectCollisions = false;
            Vector3 target = ladder.ClosestClimbPoint(transform.position);
            previousLadderWorldToLocal=ladder.transform.worldToLocalMatrix;
            characterController.enabled = false;
            transform.position = target;
            FaceLadder(ladder);
            characterController.enabled = true;
        }

        void FaceLadder(ClimbableLadder ladder)
        {
            Vector3 face = ladder.FacingDirection(transform.position);
            if (face.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(face, Vector3.up);
        }

        void EndClimb(Vector3 exitDirection, bool jumped)
        {
            climbing = false;
            activeLadder = null;
            canClimbAgainAt = Time.time + .35f;
            RestoreControllerCollisions();
            locomotion.enabled = true;
            RestoreWeaponAfterClimb();
            Vector3 velocity = exitDirection.normalized * (jumped ? 3f : 1.5f);
            if (jumped) velocity += Vector3.up * 3f;
            locomotion.CharacterVelocity = velocity;
        }

        IEnumerator FinishTopExit(ClimbableLadder ladder)
        {
            climbing = false;
            activeLadder = null;
            exitingAtTop = true;
            canClimbAgainAt = Time.time + .8f;

            // The previous implementation released movement before the controller
            // reached the roof-side exit point. Keep control briefly and guide the
            // capsule fully over the edge so gravity cannot pull it back down.
            float finishAt = Time.time + .32f;
            while (Time.time < finishAt && characterController != null && ladder!=null)
            {
                Vector3 target = ladder.TopPoint + Vector3.up * .12f;
                Vector3 next = Vector3.MoveTowards(transform.position, target, 5.5f * Time.deltaTime);
                characterController.Move(next - transform.position);
                yield return null;
            }

            RestoreControllerCollisions();
            locomotion.enabled = true;
            locomotion.CharacterVelocity = Vector3.zero;
            RestoreWeaponAfterClimb();
            yield return new WaitForSeconds(.18f);
            exitingAtTop = false;
            topExitRoutine = null;
        }

        bool CanControlLocally()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return true;
            return networkPlayer != null && networkPlayer.IsSpawned && networkPlayer.IsOwner;
        }

        void RestoreWeaponAfterClimb()
        {
            if (weapons != null) weapons.enabled = weaponsWereEnabled;
            if (weaponsWereEnabled && weaponBeforeClimb != null && weapons != null &&
                weapons.GetActiveWeapon() == weaponBeforeClimb)
                weaponBeforeClimb.ShowWeapon(true);
            weaponBeforeClimb = null;
            weaponsWereEnabled = false;
        }

        void RestoreControllerCollisions()
        {
            if (!controllerCollisionStateCaptured || characterController == null) return;
            characterController.detectCollisions = controllerCollisionsWereEnabled;
            controllerCollisionStateCaptured = false;
        }

        void OnGUI()
        {
            if (showClimbPrompt && !climbing && !exitingAtTop && CanControlLocally() && input != null && input.CanProcessInput() && !GameplayInteraction.Carrying)
                ZombieTown.Foundation.FoundationInteraction.Prompt(GameLocalization.Text("Press F to climb", "Druk op F om te klimmen"));
        }

        void OnDisable()
        {
            showClimbPrompt = false;
            if (topExitRoutine != null)
            {
                StopCoroutine(topExitRoutine);
                topExitRoutine = null;
            }
            if ((climbing || exitingAtTop) && locomotion != null)
            {
                climbing = false;
                exitingAtTop = false;
                activeLadder = null;
                RestoreControllerCollisions();
                locomotion.enabled = true;
                locomotion.CharacterVelocity = Vector3.zero;
                RestoreWeaponAfterClimb();
            }
        }

    }
}
