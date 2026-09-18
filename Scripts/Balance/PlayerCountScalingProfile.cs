using System;
using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName = "Zombie Town/Player Count Scaling")]
    public sealed class PlayerCountScalingProfile : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public int playerCount;
            public float enemyBudgetMultiplier, enemyHealthMultiplier, enemyDamageMultiplier, specialEnemyMultiplier, gateCostMultiplier;
            public int maxAliveEnemies;
            public float rewardMultiplier = 1, weaponPurchaseMultiplier = 1, upgradePurchaseMultiplier = 1;
            public Entry(int n, float budget, int alive, float hp, float damage, float special, float gate)
            { playerCount=n; enemyBudgetMultiplier=budget; maxAliveEnemies=alive; enemyHealthMultiplier=hp; enemyDamageMultiplier=damage; specialEnemyMultiplier=special; gateCostMultiplier=gate; }
        }
        public Entry[] entries = {
            new(1,.65f,16,.95f,.85f,.50f,.65f), new(2,1,24,1,.95f,.75f,.90f),
            new(3,1.28f,30,1.03f,1,1,1.05f), new(4,1.55f,36,1.06f,1,1.20f,1.20f),
            new(5,1.78f,42,1.10f,1.03f,1.35f,1.35f), new(6,2,48,1.14f,1.05f,1.50f,1.50f),
            new(7,2.20f,54,1.18f,1.08f,1.65f,1.65f), new(8,2.40f,60,1.22f,1.10f,1.80f,1.80f) };
        public Entry Get(int players) => entries[Mathf.Clamp(players,1,8)-1];
        void OnValidate() { if(entries.Length != 8) Array.Resize(ref entries,8); for(int i=0;i<8;i++) { entries[i] ??= new Entry(i+1,1,24,1,1,1,1); entries[i].playerCount=i+1; } }
    }
}
