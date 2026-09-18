using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ZombieTown.Multiplayer;

namespace ZombieTown.Traversal
{
    [DisallowMultipleComponent]
    public sealed class PlayerZiplineRider : MonoBehaviour
    {
        readonly Collider[] nearbyHits = new Collider[12];

        PlayerCharacterController locomotion;
        PlayerInputHandler input;
        CharacterController characterController;
        PlayerClassController networkPlayer;
        PlayerWeaponsManager weapons;
        WeaponController hiddenWeapon;
        VerticalZipline nearbyZipline;
        VerticalZipline activeZipline;
        Canvas promptCanvas;
        Text promptText;
        bool weaponsWereEnabled;
        bool collisionsWereEnabled;
        bool collisionStateCaptured;
        bool riding;
        bool travellingUp;
        float rideProgress;
        float canRideAgainAt;

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
                HidePrompt();
                return;
            }

            if (riding)
            {
                HidePrompt();
                if (input.GetJumpInputDown())
                {
                    FinishRide(rideProgress >= .5f);
                    return;
                }

                float target = travellingUp ? 1f : 0f;
                rideProgress = Mathf.MoveTowards(rideProgress, target,
                    activeZipline.RideSpeed / activeZipline.TravelDistance * Time.deltaTime);
                Vector3 ridePoint = activeZipline.GetRidePoint(rideProgress);
                characterController.Move(ridePoint - transform.position);
                locomotion.CharacterVelocity = Vector3.zero;

                if (Mathf.Approximately(rideProgress, target)) FinishRide(travellingUp);
                return;
            }

            nearbyZipline = FindNearbyZipline();
            bool goUp = false;
            bool canStart = Time.time >= canRideAgainAt && nearbyZipline != null &&
                            nearbyZipline.CanStartRide(transform.position, out goUp);
            if (canStart) ShowPrompt(goUp);
            else HidePrompt();

            if (canStart && InteractPressed()) BeginRide(nearbyZipline, goUp);
        }

        public void SetNearbyZipline(VerticalZipline zipline, bool inside)
        {
            if (inside && !riding) nearbyZipline = zipline;
            else if (!inside && nearbyZipline == zipline) nearbyZipline = null;
        }

        VerticalZipline FindNearbyZipline()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * .8f, 3.6f,
                nearbyHits, -1, QueryTriggerInteraction.Collide);
            VerticalZipline closest = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider hit = nearbyHits[i];
                nearbyHits[i] = null;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                VerticalZipline candidate = hit.GetComponentInParent<VerticalZipline>();
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    !candidate.CanStartRide(transform.position, out _)) continue;
                float distance = Mathf.Min(
                    Vector3.SqrMagnitude(candidate.BottomExitPoint - transform.position),
                    Vector3.SqrMagnitude(candidate.TopExitPoint - transform.position));
                if (distance >= closestDistance) continue;
                closest = candidate;
                closestDistance = distance;
            }
            return closest;
        }

        void BeginRide(VerticalZipline zipline, bool goUp)
        {
            activeZipline = zipline;
            nearbyZipline = null;
            travellingUp = goUp;
            rideProgress = goUp ? 0f : 1f;
            riding = true;
            HidePrompt();

            hiddenWeapon = weapons != null ? weapons.GetActiveWeapon() : null;
            weaponsWereEnabled = weapons != null && weapons.enabled;
            hiddenWeapon?.ShowWeapon(false);
            if (weaponsWereEnabled) weapons.enabled = false;

            locomotion.CharacterVelocity = Vector3.zero;
            locomotion.enabled = false;
            collisionsWereEnabled = characterController.detectCollisions;
            collisionStateCaptured = true;
            characterController.detectCollisions = false;
            characterController.enabled = false;
            transform.position = zipline.GetRidePoint(rideProgress);
            characterController.enabled = true;
        }

        void FinishRide(bool atTop)
        {
            VerticalZipline zipline = activeZipline;
            riding = false;
            activeZipline = null;
            canRideAgainAt = Time.time + .65f;

            characterController.enabled = false;
            transform.position = zipline.GetExitPoint(atTop);
            characterController.enabled = true;
            RestoreCollisions();
            locomotion.enabled = true;
            locomotion.CharacterVelocity = Vector3.zero;
            RestoreWeapon();
        }

        bool InteractPressed() =>
            (Keyboard.current != null && Unity.FPS.Game.GameplayInteraction.Pressed) ||
            (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);

        bool CanControlLocally()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return true;
            return networkPlayer != null && networkPlayer.IsSpawned && networkPlayer.IsOwner;
        }

        void ShowPrompt(bool goUp)
        {
            if (promptCanvas == null)
            {
                promptCanvas = RuntimeMenuUI.CreateCanvas("Zipline Interaction Prompt", 857);
                RectTransform panel = RuntimeMenuUI.Block("Prompt", promptCanvas.transform,
                    new Color(.015f, .09f, .12f, .94f));
                panel.anchorMin = new Vector2(.28f, .115f);
                panel.anchorMax = new Vector2(.72f, .175f);
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                promptText = RuntimeMenuUI.Label("Text", panel, string.Empty, 18,
                    TextAnchor.MiddleCenter, new Color(.35f, .92f, 1f));
                RuntimeMenuUI.Stretch(promptText.rectTransform, 12, 12, 4, 4);
            }
            promptCanvas.gameObject.SetActive(true);
            promptText.text = goUp
                ? GameLocalization.Text("E / X  ASCEND ZIPLINE", "E / X  ZIPLINE OMHOOG")
                : GameLocalization.Text("E / X  DESCEND ZIPLINE", "E / X  ZIPLINE OMLAAG");
        }

        void HidePrompt()
        {
            if (promptCanvas != null) promptCanvas.gameObject.SetActive(false);
        }

        void RestoreCollisions()
        {
            if (!collisionStateCaptured || characterController == null) return;
            characterController.detectCollisions = collisionsWereEnabled;
            collisionStateCaptured = false;
        }

        void RestoreWeapon()
        {
            if (weapons != null) weapons.enabled = weaponsWereEnabled;
            if (weaponsWereEnabled && hiddenWeapon != null && weapons != null &&
                weapons.GetActiveWeapon() == hiddenWeapon)
                hiddenWeapon.ShowWeapon(true);
            hiddenWeapon = null;
            weaponsWereEnabled = false;
        }

        void OnDisable()
        {
            HidePrompt();
            if (!riding || locomotion == null) return;
            riding = false;
            activeZipline = null;
            RestoreCollisions();
            locomotion.enabled = true;
            locomotion.CharacterVelocity = Vector3.zero;
            RestoreWeapon();
        }

        void OnDestroy()
        {
            if (promptCanvas != null) Destroy(promptCanvas.gameObject);
        }
    }
}
