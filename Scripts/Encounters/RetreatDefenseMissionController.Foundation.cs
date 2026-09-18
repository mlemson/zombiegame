using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.FPS.AI;
using Unity.FPS.Game;
using ZombieTown.Foundation;
using ZombieTown.Multiplayer;
using ZombieTown.Progression;
namespace ZombieTown.LevelFour
{
    public sealed partial class RetreatDefenseMissionController
    {
        public GameplaySceneContext gameplay;
        readonly List<ZombieSpawnPoint> eligibleSpawns=new();
        int stageSpawned;
        static readonly Unity.Profiling.ProfilerMarker SpawnMarker=new("ZombieTown.SpawnAuthoredEnemy");
        public bool IsSiege=>UsesFoundation && gameplay.encounter.continuousSiege && gameplay.balance.defenseSupplies!=null;
        public bool UsesFoundation=>gameplay!=null && gameplay.balance!=null && gameplay.encounter!=null;
        public IReadOnlyList<ZombieSpawnPoint> GetEligibleSpawnPoints(int stage,int players,EnemyRole role=EnemyRole.All) {
            eligibleSpawns.Clear(); if(!UsesFoundation) return eligibleSpawns;
            var data=gameplay.encounter.Get(stage);
            foreach(var p in ZombieSpawnPoint.Active) if(p!=null && p.gameObject.scene==gameObject.scene && p.Eligible(stage,role,data)) eligibleSpawns.Add(p);
            return eligibleSpawns;
        }
        void SpawnAuthoredEnemy() {
            using var sample=SpawnMarker.Auto();
            var stage=gameplay.encounter.Get(CurrentLine.Value+1); if(stage==null || stage.enemyPool==null || gameplay.balance.playerScaling==null || gameplay.balance.economy==null) return;
            var scale=gameplay.balance.playerScaling.Get(gameplay.PlayerCount);
            nextSpawnTime=Time.time+Random.Range(Mathf.Max(.1f,stage.spawnIntervalMin),Mathf.Max(stage.spawnIntervalMin,stage.spawnIntervalMax));
            if(IsSiege)nextSpawnTime=Time.time+(nextSpawnTime-Time.time)*Mathf.Lerp(1,gameplay.balance.defenseSupplies.peakSpawnIntervalMultiplier,Mathf.Clamp01(elapsedAtLine/Mathf.Max(1,gameplay.balance.defenseSupplies.pressureRampSeconds)));
            if(!IsSiege && stageSpawned>=Mathf.CeilToInt(stage.baseEnemyBudget*scale.enemyBudgetMultiplier)) return;
            int livingSpecials=CountLivingSpecials();
            float total=0;
            foreach(var e in stage.enemyPool) total+=PoolWeight(e,stage,scale,livingSpecials);
            if(total<=0) return;
            float choice=Random.value*total; EnemyDefinition definition=null;
            foreach(var e in stage.enemyPool) { float weight=PoolWeight(e,stage,scale,livingSpecials); if(weight<=0) continue; choice-=weight; if(choice<=0) { definition=e.enemyDefinition; break; } }
            if(definition==null) return;
            var points=GetEligibleSpawnPoints(CurrentLine.Value+1,gameplay.PlayerCount,definition.role);
            float spawnWeight=0; foreach(var p in points) if(p!=null && p.weight>0) spawnWeight+=p.weight;
            if(spawnWeight<=0) return;
            choice=Random.value*spawnWeight; ZombieSpawnPoint spawn=null;
            foreach(var p in points) { if(p==null || p.weight<=0) continue; choice-=p.weight; if(choice<=0) { spawn=p; break; } }
            if(spawn==null || !spawn.TryPosition(out var position)) return;
            var instance=Instantiate(definition.prefab,position+Vector3.up*.05f,spawn.transform.rotation);
            var hp=instance.GetComponent<Health>(); if(hp!=null) { hp.MaxHealth*=definition.baseHealthMultiplier*scale.enemyHealthMultiplier; hp.CurrentHealth=hp.MaxHealth; }
            var ai=instance.GetComponent<ZombieAI>(); if(ai!=null) { ai.AttackDamage*=scale.enemyDamageMultiplier; if(spawn.breachWindow!=null) { var traversal=instance.AddComponent<ZombieBreachTraversal>(); traversal.window=spawn.breachWindow; } }
            var reward=EnemyPointReward.Ensure(instance,0); reward.SetDefinitionReward(definition.pointValue<0?gameplay.balance.economy.pointsPerKill:definition.pointValue,gameplay.balance.economy.headshotBonus,scale.rewardMultiplier);
            var network=instance.GetComponent<NetworkObject>(); if(network==null) { Destroy(instance); return; } network.Spawn(true); stageSpawned++;
        }
        float PoolWeight(LevelEncounterProfile.EnemyPoolEntry e,LevelEncounterProfile.Stage stage,PlayerCountScalingProfile.Entry scaling,int livingSpecials) {
            if(e==null || e.enemyDefinition==null || e.enemyDefinition.prefab==null || CurrentLine.Value+1<e.enemyDefinition.minimumStage || CurrentLine.Value+1<e.minimumWave || (e.maximumWave>0 && CurrentLine.Value+1>e.maximumWave)) return 0;
            bool special=(e.enemyDefinition.role&(EnemyRole.Tank|EnemyRole.Ranged|EnemyRole.Special))!=0;
            if(special && livingSpecials>=stage.maximumAliveSpecials) return 0;
            return Mathf.Max(0,e.weight*e.enemyDefinition.baseWeight*(special?scaling.specialEnemyMultiplier*stage.specialWeightMultiplier:1));
        }
        int CountLivingSpecials() { int count=0; foreach(var zombie in ZombieAI.ActiveZombies) if(zombie!=null && zombie.GetComponent<Health>() is Health hp && hp.CurrentHealth>0 && hp.MaxHealth>250) count++; return count; }
        public bool TryPurchaseAuthoredGate(ProgressionGate gate,PlayerClassController player) {
            if(!IsServer || !UsesFoundation || gate==null || gate.IsOpen || player==null || !player.IsReady.Value || player.IsDowned.Value || (!IsSiege && CurrentPhase!=MissionPhase.UnlockGate) || gate.RequiredStage!=CurrentLine.Value+1) return false;
            int index=gates.IndexOf(gate.transform); if(index<0 || index!=CurrentLine.Value || !IsPlayerNearGate(player.transform.position,gate.transform,index)) return false;
            var stage=gameplay.encounter.Get(CurrentLine.Value+1); if(stage.gateUnlockedOnCompletion!=gate.gateId) return false;
            int cost=gate.ResolveCost(gameplay.PlayerCount); if(player.Points.Value<cost) return false;
            player.Points.Value-=cost; OpenGateMask.Value|=1<<index; gate.ApplyMissionState(true); if(IsSiege) {gatePreparationStarted=true;gatePreparationRemaining=gatePreparationSeconds;secondsLeft=gatePreparationRemaining;SecondsRemaining.Value=Mathf.CeilToInt(secondsLeft);Phase.Value=(byte)MissionPhase.Prepare;} if(!IsSiege) {CurrentLine.Value=gate.NextStage-1; Phase.Value=(byte)MissionPhase.FallBack;} return true;
        }
    }
}
