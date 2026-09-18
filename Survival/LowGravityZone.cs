using System.Collections.Generic;
using Unity.FPS.AI;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieTown.Survival
{
    // Gives a space level its floaty feel by lowering gravity and movement speed for players and enemies,
    // including bots/zombies spawned after this component starts running.
    public sealed class LowGravityZone : MonoBehaviour
    {
        [Range(0.1f, 1f)] public float GravityMultiplier = 0.28f;
        [Range(0.1f, 1f)] public float MoveSpeedMultiplier = 0.58f;
        [Range(0.1f, 1f)] public float JumpForceMultiplier = 0.6f;
        [Min(0.5f)] public float RescanInterval = 1.5f;

        readonly HashSet<EntityId> m_AppliedIds = new();
        float m_NextScanTime;

        void OnEnable() => Scan();

        void Update()
        {
            if (Time.time < m_NextScanTime)
                return;
            m_NextScanTime = Time.time + RescanInterval;
            Scan();
        }

        void Scan()
        {
            foreach (PlayerCharacterController player in FindObjectsByType<PlayerCharacterController>())
                ApplyToPlayer(player);

            foreach (EnemyController enemy in FindObjectsByType<EnemyController>())
                ApplyToNavAgent(enemy.GetEntityId(), enemy.NavMeshAgent);

            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
                ApplyToNavAgent(zombie.GetEntityId(), zombie.GetComponent<NavMeshAgent>());
        }

        void ApplyToPlayer(PlayerCharacterController player)
        {
            if (!m_AppliedIds.Add(player.GetEntityId()))
                return;

            player.GravityDownForce *= GravityMultiplier;
            player.JumpForce *= JumpForceMultiplier;
            player.MaxSpeedOnGround *= MoveSpeedMultiplier;
            player.MaxSpeedInAir *= MoveSpeedMultiplier;
            player.AccelerationSpeedInAir *= MoveSpeedMultiplier;
        }

        void ApplyToNavAgent(EntityId instanceId, NavMeshAgent agent)
        {
            if (agent == null || !m_AppliedIds.Add(instanceId))
                return;

            agent.speed *= MoveSpeedMultiplier;
            agent.acceleration *= MoveSpeedMultiplier;
        }
    }
}
