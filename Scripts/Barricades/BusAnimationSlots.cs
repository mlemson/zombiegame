using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Bus animation slots")]
    public sealed class BusAnimationSlots : ScriptableObject
    {
        [Tooltip("Humanoid, in-place attack while hanging from a window. Applied to BusHangAttack.")]
        public AnimationClip zombieHangAttack;
        [Tooltip("Humanoid, in-place crawl through the bus window. Applied to BusCrawl.")]
        public AnimationClip windowBusCrawl;
    }
}
