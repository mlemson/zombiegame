using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Barricade Definition")]
    public sealed class BarricadeDefinition : ScriptableObject
    {
        [Range(1,4)] public int boardCount=4;
        [Min(1)] public float boardHealth=45;
        [Min(.1f)] public float repairTimePerBoard=1.2f, repairCooldown=.3f, interactionRadius=3, traversalDuration=2.4f;
        public AnimationCurve traversalCurve=AnimationCurve.EaseInOut(0,0,1,1);
    }
}
