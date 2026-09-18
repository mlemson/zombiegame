using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using ZombieTown.Multiplayer;

namespace Unity.FPS.AI
{
    public class ZombieSpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        public List<GameObject> ZombiePrefabs = new List<GameObject>();
        public List<Transform> SpawnPoints = new List<Transform>();
        public float SpawnRadius = 20f;

        public int TotalZombiesToSpawn = 20;
        public int MaxConcurrentZombies = 8;
        public float SpawnInterval = 3f;
        public SoloZombiePressureProfile soloPressure;

        [Header("Player Scaling")]
        public bool ScaleWithPlayerCount = true;
        public int BaseZombiesToSpawn = 70;
        public int AdditionalZombiesPerPlayer = 20;
        public int AdditionalConcurrentZombiesPerPlayer = 4;

        private int m_SpawnedCount = 0;
        private float m_LastSpawnTime;
        private Transform m_PlayerTransform;

        void Start()
        {
            PlayerCharacterController playerController = FindPlayerController();
            if (playerController != null)
            {
                m_PlayerTransform = playerController.transform;
            }
        }

        void Update()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
                return;
            if (!NetworkRoundGate.IsOpen)
                return;
            float pressure=soloPressure!=null && GetActivePlayerCount()==1?soloPressure.spawnRateMultiplier:1f;
            if (Time.time < m_LastSpawnTime + SpawnInterval/Mathf.Max(.1f,pressure))
                return;
            m_LastSpawnTime = Time.time;
            if (m_SpawnedCount >= GetEffectiveTotalZombies()) return;

            if (m_PlayerTransform == null)
            {
                PlayerCharacterController playerController = FindPlayerController();
                if (playerController != null)
                {
                    m_PlayerTransform = playerController.transform;
                }
                else
                {
                    return;
                }
            }

            int activeZombies = GameObject.FindGameObjectsWithTag("Enemy").Length;
            if (activeZombies < GetEffectiveMaxConcurrentZombies())
            {
                SpawnZombie();
            }
        }

        int GetActivePlayerCount()
        {
            var manager=NetworkManager.Singleton;
            if(manager==null || !manager.IsServer)return 1;
            int readyPlayers = 0;
            foreach(var client in manager.ConnectedClientsList){
                var player=client.PlayerObject!=null?client.PlayerObject.GetComponent<PlayerClassController>():null;
                if(player!=null && player.IsReady.Value)readyPlayers++;
            }
            return Mathf.Max(1, readyPlayers);
        }

        int GetEffectiveTotalZombies()
        {
            if (!ScaleWithPlayerCount) return TotalZombiesToSpawn;
            int extraPlayers = Mathf.Max(0, GetActivePlayerCount() - 1);
            return Mathf.Max(1, BaseZombiesToSpawn) + extraPlayers * Mathf.Max(0, AdditionalZombiesPerPlayer);
        }

        int GetEffectiveMaxConcurrentZombies()
        {
            int players=GetActivePlayerCount();
            int extraPlayers = ScaleWithPlayerCount?Mathf.Max(0, players - 1):0;
            int baseline = Mathf.Max(1, MaxConcurrentZombies);
            if(players==1 && soloPressure!=null)baseline=Mathf.CeilToInt(baseline*soloPressure.aliveLimitMultiplier);
            return baseline + extraPlayers * Mathf.Max(0, AdditionalConcurrentZombiesPerPlayer);
        }

        void SpawnZombie()
        {
            if (ZombiePrefabs.Count == 0) return;
            if (!TryFindSpawnPosition(out Vector3 spawnPosition, out Quaternion spawnRotation)) return;

            GameObject selectedPrefab = ZombiePrefabs[Random.Range(0, ZombiePrefabs.Count)];
            GameObject zombie = Instantiate(selectedPrefab, spawnPosition, spawnRotation);
            zombie.tag = "Enemy";
            NetworkObject networkObject = zombie.GetComponent<NetworkObject>();
            if (networkObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                networkObject.Spawn(true);

            m_SpawnedCount++;
        }

        bool TryFindSpawnPosition(out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            if (m_PlayerTransform != null)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vector2 offset = Random.insideUnitCircle * SpawnRadius;
                    Vector3 candidate = m_PlayerTransform.position + new Vector3(offset.x, 0f, offset.y);

                    // Keep the sample radius small so it can't snap up onto a scaled-up roof's navmesh.
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(hit.position.y - m_PlayerTransform.position.y) > 3f) continue;
                    if (Physics.CheckSphere(hit.position + Vector3.up * .5f, .4f, ~0, QueryTriggerInteraction.Ignore)) continue;

                    position = hit.position;
                    return true;
                }
            }

            if (SpawnPoints.Count > 0)
            {
                Transform spawnPoint = SpawnPoints[Random.Range(0, SpawnPoints.Count)];
                position = spawnPoint.position;
                rotation = spawnPoint.rotation;
                return true;
            }

            position = default;
            return false;
        }

        PlayerCharacterController FindPlayerController()
        {
            ZombieTown.Multiplayer.PlayerClassController[] players =
                FindObjectsByType<ZombieTown.Multiplayer.PlayerClassController>();
            if (players == null || players.Length == 0)
                return null;

            List<PlayerCharacterController> validPlayers = new();
            foreach (ZombieTown.Multiplayer.PlayerClassController player in players)
            {
                if (player == null || !player.IsSpawned || !player.IsReady.Value)
                    continue;

                PlayerCharacterController movement = player.GetComponent<PlayerCharacterController>();
                if (movement != null)
                    validPlayers.Add(movement);
            }

            if (validPlayers.Count == 0)
                return null;

            return validPlayers[Random.Range(0, validPlayers.Count)];
        }
    }
}
