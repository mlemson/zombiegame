using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Unity.FPS.Game;
using ZombieTown.Foundation;
namespace ZombieTown.Multiplayer
{
    public struct WeaponUpgradeState : INetworkSerializable, IEquatable<WeaponUpgradeState>
    {
        public FixedString64Bytes weaponKey; public byte upgradeLevel;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T:IReaderWriter { serializer.SerializeValue(ref weaponKey); serializer.SerializeValue(ref upgradeLevel); }
        public bool Equals(WeaponUpgradeState other)=>weaponKey.Equals(other.weaponKey) && upgradeLevel==other.upgradeLevel;
    }
    public sealed partial class PlayerClassController
    {
        public NetworkList<WeaponUpgradeState> WeaponUpgrades=new();
        public int GetUpgradeLevel(string key) { for(int i=0;i<WeaponUpgrades.Count;i++) if(WeaponUpgrades[i].weaponKey.ToString()==key) return WeaponUpgrades[i].upgradeLevel; return 0; }
        public bool ServerOwnsDefinition(WeaponDefinition definition) {
            if(!IsServer || definition==null || definition.weaponPrefab==null) return false;
            if(serverPurchasedKeys.Contains(definition.weaponKey) || serverPurchasedKeys.Contains(definition.weaponPrefab.name)) return true;
            var tuning=Get(SelectedClass.Value);
            return tuning.StartingWeapon==definition.weaponPrefab;
        }
        public bool TryPurchaseUpgrade(WeaponUpgradeMachine machine) {
            if(!IsServer || machine==null || machine.balance==null || machine.catalog==null) return false;
            string key=EquippedWeaponKey.Value.ToString();
            var weapon=machine.balance.weapons.Find(key); var upgrade=machine.catalog.Find(key);
            int next=GetUpgradeLevel(key)+1;
            if(!ServerOwnsDefinition(weapon) || upgrade==null || next>2 || weapon.weaponPrefab.IsMeleeWeapon) return false;
            var tier=upgrade.Get(next); int cost=machine.Cost(tier,next);
            if(Points.Value<cost) return false;
            Points.Value-=cost;
            var state=new WeaponUpgradeState {weaponKey=new FixedString64Bytes(key),upgradeLevel=(byte)next};
            for(int i=0;i<WeaponUpgrades.Count;i++) if(WeaponUpgrades[i].weaponKey.Equals(state.weaponKey)) { WeaponUpgrades[i]=state; UpgradeAmmoRefillRpc(state.weaponKey,state.upgradeLevel); return true; }
            WeaponUpgrades.Add(state); UpgradeAmmoRefillRpc(state.weaponKey,state.upgradeLevel); return true;
        }
        // Pack-a-Punch also tops off the upgraded weapon's ammo; owner-only since ammo is client-local.
        [Rpc(SendTo.Everyone)] void UpgradeAmmoRefillRpc(FixedString64Bytes weaponKey,byte level) {
            if(!IsOwner || weapons==null || !TryResolveNetworkWeapon(weaponKey.ToString(),out var weaponPrefab))return;
            var owned=weapons.HasWeapon(weaponPrefab);if(owned==null)return;
            var tier=GameplaySceneContext.Active?.balance?.upgrades?.Find(weaponKey.ToString())?.Get(level);if(tier==null)return;
            // Apply capacity before refilling, even if the NetworkList delta arrives later.
            var modifier=owned.GetComponent<RuntimeWeaponUpgrade>()??owned.gameObject.AddComponent<RuntimeWeaponUpgrade>();modifier.Apply(tier);owned.RefillAfterUpgrade();
        }
        void OnUpgradeChanged(NetworkListEvent<WeaponUpgradeState> change) { ApplyOwnedUpgrades(); }
        void ApplyOwnedUpgrades() {
            var context=GameplaySceneContext.Active;
            if(weapons==null || context?.balance?.upgrades==null) return;
            var movement=GetComponent<Unity.FPS.Gameplay.PlayerCharacterController>(); if(movement!=null) movement.CrouchTuning=context.balance.crouch;
            for(int i=0;i<9;i++) {
                var weapon=weapons.GetWeaponAtSlotIndex(i); if(weapon==null) continue;
                var definition=context.balance.weapons.Find(weapon); if(definition==null) continue;
                var upgrade=context.balance.upgrades.Find(definition.weaponKey); if(upgrade==null) continue;
                var tier=upgrade.Get(GetUpgradeLevel(definition.weaponKey));
                var modifier=weapon.GetComponent<RuntimeWeaponUpgrade>()??weapon.gameObject.AddComponent<RuntimeWeaponUpgrade>(); modifier.Apply(tier);
            }
        }
        int repairRound=-1, repairPoints;
        public void AwardBarrierRepair(EconomyProfile economy) {
            if(!IsServer || economy==null) return;
            int round=GameplaySceneContext.Active!=null?GameplaySceneContext.Active.Round:0;
            if(repairRound!=round) { repairRound=round; repairPoints=0; }
            int reward=Mathf.Clamp(economy.repairReward,0,Mathf.Max(0,economy.repairRewardCapPerRound-repairPoints));
            repairPoints+=reward; AwardKillPoints(reward);
        }
        float UpgradeDamage(string key) => GameplaySceneContext.Active?.balance?.upgrades?.Find(key)?.Get(GetUpgradeLevel(key))?.damageMultiplier??1;
        float UpgradeDelay(string key) => GameplaySceneContext.Active?.balance?.upgrades?.Find(key)?.Get(GetUpgradeLevel(key))?.attackDelayMultiplier??1;
    }
}
