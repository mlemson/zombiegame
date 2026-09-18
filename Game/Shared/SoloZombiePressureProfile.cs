using UnityEngine;
namespace Unity.FPS.Game
{
    [CreateAssetMenu(menuName="Zombie Town/Early Levels Solo Zombie Pressure")]
    public sealed class SoloZombiePressureProfile:ScriptableObject
    {
        [Min(.1f),Tooltip("1 = original rate; 1.2 = 20% faster spawning. Solo levels 1-3 only.")]
        public float spawnRateMultiplier=1.2f;
        [Min(.1f),Tooltip("Multiplier for the simultaneous zombie limit, solo levels 1-3.")]
        public float aliveLimitMultiplier=1.15f;
    }
}
