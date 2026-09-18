using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.FPS.AI;

namespace ZombieTown.Foundation
{
    // The bus spawns separate network objects: NGO dynamic prefabs cannot contain nested NetworkObjects.
    public sealed class BusDefenseLayout : NetworkBehaviour
    {
        public VehicleZombieAccessZone[] openings;
        public GameObject windowPrefab;
        public GameObject plankPrefab;
        public Transform[] woodSlots;
        [Tooltip("Starting planks per opening, matching the openings array. Doors are ignored; missing entries start open.")]
        public int[] startingBoards;
        public bool allowRoofBoarding=true;
        public float roofFloorHeight=3.22f;
        public bool IsOnRoof(Vector3 point)
        {
            Vector3 p=transform.InverseTransformPoint(point);
            return allowRoofBoarding && Mathf.Abs(p.x)<1.75f && p.z> -5 && p.z<6.3f && p.y>=roofFloorHeight-.17f && p.y<roofFloorHeight+1.6f;
        }
        public bool ContainsPassenger(Vector3 point,float margin)
        {
            Vector3 p=transform.InverseTransformPoint(point);float m=margin/Mathf.Max(.1f,transform.lossyScale.x);
            return (Mathf.Abs(p.x)<=1.65f+m && p.z>=-5-m && p.z<=6.3f+m && p.y>=.4f-m && p.y<=3.1f+m) || IsOnRoof(point);
        }
        readonly List<NetworkObject> spawned = new();

        void Awake()
        {
            // These separate network objects follow the bus transform and must never
            // act as immovable supports against its dynamic chassis or suspension.
            int attachments = LayerMask.GetMask("BusAttachment");
            GetComponent<Rigidbody>().excludeLayers |= attachments;
            foreach (var wheel in GetComponentsInChildren<WheelCollider>(true))
                wheel.excludeLayers |= attachments;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            for (int i = 0; i < openings.Length; i++)
            {
                var zone = openings[i];
                if (!zone.IsWindow) continue;
                var go = Instantiate(windowPrefab, zone.transform.position, zone.transform.rotation);
                int count=startingBoards!=null && i<startingBoards.Length?Mathf.Clamp(startingBoards[i],0,4):0;
                go.GetComponent<BarricadeWindow>().initialMissingBoards=4-count;
                var attachment = go.GetComponent<BusWindowAttachment>();
                attachment.Configure(NetworkObject, i);
                var net = go.GetComponent<NetworkObject>();
                net.Spawn(); spawned.Add(net);
            }
            foreach (var slot in woodSlots)
            {
                var go = Instantiate(plankPrefab, slot.position, slot.rotation);
                var net = go.GetComponent<NetworkObject>();
                net.Spawn(); spawned.Add(net);
                go.GetComponent<CarryableDefenseItem>().RestOnVehicle(NetworkObject, slot.position, slot.rotation);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) foreach (var net in spawned)
                if (net != null && net.IsSpawned) net.Despawn(true);
            spawned.Clear();
        }
    }
}
