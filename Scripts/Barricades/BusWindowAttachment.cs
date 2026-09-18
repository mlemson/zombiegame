using UnityEngine;
using Unity.Netcode;
using Unity.FPS.AI;

namespace ZombieTown.Foundation
{
    [DefaultExecutionOrder(-1100)]
    public sealed class BusWindowAttachment : NetworkBehaviour
    {
        public readonly NetworkVariable<NetworkObjectReference> Vehicle = new();
        public readonly NetworkVariable<int> OpeningIndex = new();
        VehicleZombieAccessZone zone;
        public VehicleZombieAccessZone Zone => zone;
        BarricadeWindow window;
        NetworkObject initialVehicle;
        int initialOpening;
        void Awake()
        {
            int layer = LayerMask.NameToLayer("BusAttachment");
            if (layer >= 0) foreach (var collider in GetComponentsInChildren<Collider>(true))
                collider.gameObject.layer = layer;
        }
        public void Configure(NetworkObject vehicle, int opening) { initialVehicle=vehicle; initialOpening=opening; }

        public override void OnNetworkSpawn() {
            if(IsServer && initialVehicle!=null){Vehicle.Value=new NetworkObjectReference(initialVehicle);OpeningIndex.Value=initialOpening;}
            window = GetComponent<BarricadeWindow>(); Follow();
        }
        void Update() { if (IsSpawned) Follow(); }
        void LateUpdate() { if (zone != null) Follow(); }
        void Follow()
        {
            if (zone == null)
            {
                if (!Vehicle.Value.TryGet(out var vehicle)) return;
                var layout = vehicle.GetComponent<BusDefenseLayout>();
                int index = OpeningIndex.Value;
                if (layout == null || index < 0 || index >= layout.openings.Length) return;
                zone = layout.openings[index];
                zone.Barricade = window;
            }
            // Zone forward points into the bus, matching the existing repair-window convention.
            transform.SetPositionAndRotation(zone.transform.TransformPoint(zone.Box.center), zone.transform.rotation);
            transform.localScale = new Vector3(zone.Box.size.x * zone.transform.lossyScale.x,
                zone.Box.size.y * zone.transform.lossyScale.y, zone.transform.lossyScale.z);
        }
        public override void OnNetworkDespawn() { if (zone != null && zone.Barricade == window) zone.Barricade = null; zone = null; }
    }
}
