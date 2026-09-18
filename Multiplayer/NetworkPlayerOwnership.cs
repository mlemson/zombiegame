using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace ZombieTown.Multiplayer
{
    public sealed class NetworkPlayerOwnership : NetworkBehaviour
    {
        PlayerInputHandler input;
        PlayerCharacterController controller;
        PlayerWeaponsManager weapons;
        Jetpack jetpack;
        PlayerAudio playerAudio;
        bool gameplayReady;

        void Awake()
        {
            input = GetComponent<PlayerInputHandler>();
            controller = GetComponent<PlayerCharacterController>();
            weapons = GetComponent<PlayerWeaponsManager>();
            jetpack = GetComponent<Jetpack>();
            playerAudio = GetComponent<PlayerAudio>();
        }

        public override void OnNetworkSpawn()
        {
            gameplayReady = false;
            ApplyOwnershipState();
        }

        public override void OnGainedOwnership() => ApplyOwnershipState();
        public override void OnLostOwnership() => ApplyOwnershipState();

        public void SetLocalGameplayReady(bool ready)
        {
            if (!IsOwner) return;
            gameplayReady = ready;
            ApplyOwnershipState();
        }

        void ApplyOwnershipState()
        {
            bool local = IsSpawned && IsOwner;
            SetEnabled(input, local);
            // Camera/menu may be active immediately, but physics and weapon initialization
            // must wait until the server has assigned a safe gameplay spawn.
            SetEnabled(controller, local && gameplayReady);
            SetEnabled(weapons, local && gameplayReady);
            SetEnabled(jetpack, local && gameplayReady);
            if (local && input != null) input.SetGameplayInputEnabled(gameplayReady);

            foreach (Camera camera in GetComponentsInChildren<Camera>(true)) camera.enabled = local;
            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true)) listener.enabled = local;
            playerAudio?.SetLocalListener(local);
        }

        static void SetEnabled(Behaviour component, bool enabled) { if (component != null) component.enabled = enabled; }
    }
}
