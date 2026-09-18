using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Economy")]
    public sealed class EconomyProfile : ScriptableObject
    {
        [Min(0)] public int startingPoints=100, pointsPerKill=10, headshotBonus=5;
        [Range(0,1)] public float levelTransitionCarryover=.5f;
        [Min(1)] public int gateRoundingStep=5;
        [Min(0)] public int repairReward=5, repairRewardCapPerRound=50, packAPunchLevel1Cost=500, packAPunchLevel2Cost=1000;
        [Min(0)] public float weaponPricingMultiplier=1, ammoPricingMultiplier=.5f, upgradePricingMultiplier=1;
        public int GateCost(int cost,float multiplier) => Mathf.Max(0,Mathf.RoundToInt(cost*multiplier/Mathf.Max(1,gateRoundingStep))*Mathf.Max(1,gateRoundingStep));
    }
}
