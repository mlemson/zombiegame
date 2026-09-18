using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Level Encounter")]
    public sealed class LevelEncounterProfile : ScriptableObject
    {
        [System.Serializable] public sealed class EnemyPoolEntry
        { public EnemyDefinition enemyDefinition; [Min(0)] public float weight=1; public int minimumWave=1, maximumWave; }
        [System.Serializable] public sealed class Stage
        {
            public string stageId, displayName;
            [Min(1)] public int baseEnemyBudget=40;
            [Min(0)] public int baseMaxAliveOverride;
            public float spawnIntervalMin=1, spawnIntervalMax=1.6f, waveDuration=60;
            public EnemyPoolEntry[] enemyPool=System.Array.Empty<EnemyPoolEntry>();
            public string[] allowedSpawnGroups=System.Array.Empty<string>();
            public string gateUnlockedOnCompletion;
            [Min(0)] public int maximumAliveSpecials=6;
            [Min(0)] public float specialWeightMultiplier=1;
        }
        public bool continuousSiege;
        public Stage[] stages=System.Array.Empty<Stage>();
        public Stage Get(int stage) => stage>=1 && stage<=stages.Length ? stages[stage-1] : null;
    }
}
