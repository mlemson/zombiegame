using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.AI;
using ZombieTown.Progression;
using ZombieTown.LevelTwo;

namespace Unity.FPS.AI
{
    public class MapWeaponLootSpawner : MonoBehaviour
    {
        public List<GameObject> WeaponPickupPrefabs = new List<GameObject>();
        public float MinimumDistance = 14f;
        public float MaximumDistance = 38f;

        IEnumerator Start()
        {
            yield return null;

            Vector3 center = ResolveLootCenter();

            // Keep placement deterministic between runs and distribute every weapon
            // around a complete circle instead of overlapping after the third pickup.
            WeaponPickupPrefabs.Sort((left, right) =>
            {
                WeaponPickup leftPickup = left != null ? left.GetComponent<WeaponPickup>() : null;
                WeaponPickup rightPickup = right != null ? right.GetComponent<WeaponPickup>() : null;
                string leftName = leftPickup != null && leftPickup.WeaponPrefab != null
                    ? leftPickup.WeaponPrefab.WeaponName : string.Empty;
                string rightName = rightPickup != null && rightPickup.WeaponPrefab != null
                    ? rightPickup.WeaponPrefab.WeaponName : string.Empty;
                return string.CompareOrdinal(leftName, rightName);
            });

            for (int i = 0; i < WeaponPickupPrefabs.Count; i++)
            {
                GameObject pickupPrefab = WeaponPickupPrefabs[i];
                if (pickupPrefab == null)
                    continue;

                Vector3 position = FindLootPosition(center, i, WeaponPickupPrefabs.Count);
                GameObject instance = Instantiate(pickupPrefab, position + Vector3.up * 0.15f,
                    Quaternion.Euler(0f, i * (360f / Mathf.Max(1, WeaponPickupPrefabs.Count)), 0f));
                WeaponShopConverter.Convert(instance.GetComponent<WeaponPickup>());
            }
        }

        Vector3 ResolveLootCenter()
        {
            // Shop terminals are intentionally local scene objects rather than networked
            // pickups. Every peer must therefore derive exactly the same world positions.
            // Never anchor them to FindAnyObjectByType<PlayerCharacterController>(), because
            // that can resolve to a different player on each machine.
            LevelPlayerSpawn[] starts = FindObjectsByType<LevelPlayerSpawn>();
            if (starts != null && starts.Length > 0)
            {
                System.Array.Sort(starts, (left, right) =>
                    string.CompareOrdinal(left != null ? left.name : string.Empty,
                        right != null ? right.name : string.Empty));
                if (starts[0] != null)
                    return starts[0].transform.position;
            }

            return transform.position;
        }

        Vector3 FindLootPosition(Vector3 center, int index, int count)
        {
            float spacing = 360f / Mathf.Max(1, count);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = index * spacing + attempt * 47f;
                float distance = Mathf.Lerp(MinimumDistance, MaximumDistance, (attempt % 7) / 6f);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 candidate = center + direction * distance;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            return center + Quaternion.Euler(0f, index * spacing, 0f) * Vector3.forward * MinimumDistance;
        }
    }
}
