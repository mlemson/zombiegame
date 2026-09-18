using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Defense Supplies")]
    public sealed class DefenseSupplyProfile:ScriptableObject
    {
        [Min(0)] public int startingMaterials=30, materialsPerBuild=10;
        [Min(1)] public float repairHealthPerMaterial=20;
        [Min(.1f)] public float buildSeconds=2;
        [Min(0)] public float preparationSeconds=20, breathingSeconds=15;
        [Min(1)] public float pressureRampSeconds=120;
        [Range(.1f,1)] public float peakSpawnIntervalMultiplier=.35f;
    }
}
