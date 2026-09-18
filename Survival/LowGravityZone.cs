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

        struct PlayerDefaults
        {
            public float Gravity;
            public float Jump;
            public float GroundSpeed;
            public float AirSpeed;
            public float AirAcceleration;
        }

        struct AgentDefaults
        {
            public float Speed;
            public float Acceleration;
        }

        readonly Dictionary<PlayerCharacterController, PlayerDefaults> m_Players = new();
        readonly Dictionary<NavMeshAgent, AgentDefaults> m_Agents = new();
        float m_NextScanTime;

        void OnEnable()
        {
            // Wait until the first Update. Unity calls Start on the player/enemy scripts
            // before that Update, so their authored movement values are initialized before
            // we capture and scale them.
            m_NextScanTime = Time.time;
        }

        void OnDisable() => RestoreAll();

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
                ApplyToNavAgent(enemy != null ? enemy.NavMeshAgent : null);

            foreach (ZombieAI zombie in FindObjectsByType<ZombieAI>())
                ApplyToNavAgent(zombie != null ? zombie.GetComponent<NavMeshAgent>() : null);
        }

        void ApplyToPlayer(PlayerCharacterController player)
        {
            if (player == null || m_Players.ContainsKey(player))
                return;

            m_Players[player] = new PlayerDefaults
            {
                Gravity = player.GravityDownForce,
                Jump = player.JumpForce,
                GroundSpeed = player.MaxSpeedOnGround,
                AirSpeed = player.MaxSpeedInAir,
                AirAcceleration = player.AccelerationSpeedInAir
            };

            player.GravityDownForce *= GravityMultiplier;
            player.JumpForce *= JumpForceMultiplier;
            player.MaxSpeedOnGround *= MoveSpeedMultiplier;
            player.MaxSpeedInAir *= MoveSpeedMultiplier;
            player.AccelerationSpeedInAir *= MoveSpeedMultiplier;
        }

        void ApplyToNavAgent(NavMeshAgent agent)
        {
            if (agent == null || m_Agents.ContainsKey(agent))
                return;

            m_Agents[agent] = new AgentDefaults
            {
                Speed = agent.speed,
                Acceleration = agent.acceleration
            };

            agent.speed *= MoveSpeedMultiplier;
            agent.acceleration *= MoveSpeedMultiplier;
        }

        void RestoreAll()
        {
            foreach (KeyValuePair<PlayerCharacterController, PlayerDefaults> pair in m_Players)
            {
                PlayerCharacterController player = pair.Key;
                if (player == null) continue;
                PlayerDefaults defaults = pair.Value;
                player.GravityDownForce = defaults.Gravity;
                player.JumpForce = defaults.Jump;
                player.MaxSpeedOnGround = defaults.GroundSpeed;
                player.MaxSpeedInAir = defaults.AirSpeed;
                player.AccelerationSpeedInAir = defaults.AirAcceleration;
            }

            foreach (KeyValuePair<NavMeshAgent, AgentDefaults> pair in m_Agents)
            {
                NavMeshAgent agent = pair.Key;
                if (agent == null) continue;
                AgentDefaults defaults = pair.Value;
                agent.speed = defaults.Speed;
                agent.acceleration = defaults.Acceleration;
            }

            m_Players.Clear();
            m_Agents.Clear();
        }
    }
}
