using UnityEngine;
using Unity.Netcode;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class WeaponUpgradeMachine : NetworkBehaviour
    {
        public string machineId;
        public GameBalanceDatabase balance;
        public WeaponUpgradeCatalog catalog;
        public bool useDefinitionDefaults=true, overrideCost, overrideInteractionRadius, overrideUnlockCondition;
        public int localCostOverride=500, requiredStage=1;
        public float interactionRadius=3;
        public Transform interactionPoint;
        public Vector3 InteractionPosition=>interactionPoint!=null?interactionPoint.position:transform.position;
        public int Cost(WeaponUpgradeDefinition.Tier tier,int level) {
            int baseCost=!useDefinitionDefaults || overrideCost ? localCostOverride : tier.cost>=0 ? tier.cost : level==1?balance.economy.packAPunchLevel1Cost:balance.economy.packAPunchLevel2Cost;
            int players=GameplaySceneContext.Active!=null?GameplaySceneContext.Active.PlayerCount:1;
            return Mathf.Max(0,Mathf.RoundToInt(baseCost*balance.economy.upgradePricingMultiplier*balance.playerScaling.Get(players).upgradePurchaseMultiplier));
        }
        public bool IsAvailable=>GameplaySceneContext.Active!=null ? GameplaySceneContext.Active.Stage>=requiredStage : requiredStage<=1;
        void Update() { if(IsSpawned && IsAvailable && FoundationInteraction.Local(InteractionPosition,interactionRadius) is var p && p!=null && !p.IsUsingMountedGun && FoundationInteraction.Pressed) {Unity.FPS.Game.GameplayInteraction.Consume();PurchaseRpc();} }
        void OnGUI() {
            var p=FoundationInteraction.Local(InteractionPosition,interactionRadius);
            if(p==null || !IsAvailable || catalog==null || balance==null) return;
            string key=p.EquippedWeaponKey.Value.ToString(); var def=catalog.Find(key); int level=p.GetUpgradeLevel(key)+1;
            if(def==null) { FoundationInteraction.Prompt("NO COMPATIBLE ACTIVE WEAPON"); return; }
            if(level>2) { FoundationInteraction.Prompt("WEAPON FULLY UPGRADED"); return; }
            var tier=def.Get(level); var weapon=balance.weapons.Find(key);
            FoundationInteraction.Prompt($"F  UPGRADE {weapon?.displayName}\nLevel {level}  -  {Cost(tier,level)} points\nDamage x{tier.damageMultiplier:0.00}  Attack delay x{tier.attackDelayMultiplier:0.00}  Ammo x{tier.magazineMultiplier:0.00}");
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void PurchaseRpc(RpcParams rpc=default) {
            if(balance==null || catalog==null || !IsAvailable || !FoundationInteraction.TryPlayer(rpc.Receive.SenderClientId,InteractionPosition,interactionRadius,out var p)) return;
            p.TryPurchaseUpgrade(this);
        }
    }
}
