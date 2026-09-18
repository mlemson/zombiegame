using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using ZombieTown.Multiplayer;

namespace ZombieTown.Progression
{
    [DisallowMultipleComponent]
    public sealed class EnemyPointReward : MonoBehaviour
    {
        Health health;
        int points;
        int headshotBonus;
        bool awarded;
        bool definitionReward;
        public void SetDefinitionReward(int reward,int bonus,float multiplier) { definitionReward=true; points=Mathf.RoundToInt(reward*multiplier); headshotBonus=Mathf.RoundToInt(bonus*multiplier); }

        public static EnemyPointReward Ensure(GameObject target, int reward, int headshotBonus = 0)
        {
            if (target == null) return null;
            EnemyPointReward result = target.GetComponent<EnemyPointReward>() ??
                                      target.AddComponent<EnemyPointReward>();
            if (result.definitionReward) return result;
            result.points = Mathf.Max(result.points, reward);
            result.headshotBonus = Mathf.Max(result.headshotBonus, headshotBonus);
            return result;
        }

        void Awake()
        {
            health = GetComponent<Health>();
            if (health != null) health.OnDie += Award;
        }

        void Award()
        {
            if (awarded || points <= 0 || health == null) return;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening && !manager.IsServer) return;

            GameObject source = health.LastDamageSource;
            PlayerClassController player = source != null
                ? source.GetComponentInParent<PlayerClassController>() : null;
            if (player == null && source != null)
            {
                WeaponController weapon = source.GetComponentInParent<WeaponController>();
                if (weapon != null && weapon.Owner != null)
                    player = weapon.Owner.GetComponentInParent<PlayerClassController>();
            }
            if (player == null) return;

            awarded = true;
            bool wasHeadshot = GetComponent<ZombieAI>()?.WasLethalHeadshot ?? false;
            player.AwardKillPoints(points + (wasHeadshot ? headshotBonus : 0));
        }

        void OnDestroy()
        {
            if (health != null) health.OnDie -= Award;
        }
    }
}
