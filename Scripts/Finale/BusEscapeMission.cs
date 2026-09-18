using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.FPS.Game;
using Unity.FPS.AI;
using ZombieTown.Multiplayer;
using ZombieTown.Foundation;

namespace ZombieTown.Finale
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BusEscapeMission : NetworkBehaviour
    {
        public enum Stage : byte { Prepare, Drive, Checkpoint, FinalRun, Complete }
        public BusEscapeObjective objective;
        public Transform bus;
        public Transform departureConsole, checkpointConsole;
        public BoxCollider checkpointArea, extractionArea;
        public GameObject checkpointGate, departureMarker, checkpointMarker, extractionMarker;
        public GameObject[] zombiePrefabs;
        public Transform[] spawnPoints;
        public Transform[] routeCheckpoints;
        public readonly NetworkVariable<int> RouteProgress=new();
        public float defenseSeconds = 35;
        public float extractionSeconds = 4;
        public readonly NetworkVariable<byte> Phase = new();
        public readonly NetworkVariable<int> SecondsRemaining = new();
        public readonly NetworkVariable<bool> WaitingForBus = new();
        public readonly NetworkVariable<int> GateReleaseStep = new();
        readonly List<NetworkObject> enemies = new();
        float timer, nextSpawn, nextPresentation;
        bool completedLocally;
        public Stage CurrentStage => (Stage)Phase.Value;

        public override void OnNetworkSpawn()
        {
            Phase.OnValueChanged += Changed;
            GateReleaseStep.OnValueChanged += GateStepChanged;
            Refresh();
        }
        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= Changed;
            GateReleaseStep.OnValueChanged -= GateStepChanged;
            if(IsServer)foreach(var enemy in enemies)if(enemy!=null && enemy.IsSpawned)enemy.Despawn(true);
            enemies.Clear();
        }
        void Changed(byte oldValue,byte value) => Refresh();
        void GateStepChanged(int before,int after)
        {
            if(after>before && checkpointGate!=null)DefenseAudioSettings.Play(DefenseAudioSettings.Cue.Gate,checkpointGate.transform.position);
            Refresh();
        }
        void Update()
        {
            if(!IsSpawned)return;
            if(Time.unscaledTime>=nextPresentation){nextPresentation=Time.unscaledTime+.2f;Refresh();}
            if(!NetworkRoundGate.IsOpen)return;
            var player=NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerClassController>();
            Transform terminal=CurrentStage==Stage.Prepare?departureConsole:CurrentStage==Stage.Drive?checkpointConsole:null;
            if(player!=null && !player.IsDowned.Value && terminal!=null && Vector3.Distance(player.transform.position,terminal.position)<3.2f && GameplayInteraction.Pressed)
            {GameplayInteraction.Consume();InteractRpc();}
            if(!IsServer || CurrentStage==Stage.Complete)return;
            if(CurrentStage!=Stage.Prepare && Time.time>=nextSpawn)SpawnPressure();
            if(CurrentStage==Stage.Checkpoint)
            {
                WaitingForBus.Value=!Contains(checkpointArea,bus.position);
                if(!WaitingForBus.Value)timer=Mathf.Max(0,timer-Time.deltaTime);
                SecondsRemaining.Value=Mathf.CeilToInt(timer);
                GateReleaseStep.Value=Mathf.Clamp(Mathf.FloorToInt((1-timer/Mathf.Max(.01f,defenseSeconds))*5+.0001f),0,5);
                if(timer<=0){Phase.Value=(byte)Stage.FinalRun;timer=0;}
            }
            if(CurrentStage==Stage.FinalRun)
            {
                int routeCount=routeCheckpoints?.Length??0;
                if(RouteProgress.Value<routeCount && Vector3.Distance(bus.position,routeCheckpoints[RouteProgress.Value].position)<13)
                    RouteProgress.Value++;
                bool together=RouteProgress.Value>=routeCount && Contains(extractionArea,bus.position) && EveryoneAboard();
                WaitingForBus.Value=!together;
                timer=together?timer+Time.deltaTime:0;
                SecondsRemaining.Value=Mathf.CeilToInt(Mathf.Max(0,extractionSeconds-timer));
                if(timer>=extractionSeconds)Phase.Value=(byte)Stage.Complete;
            }
        }
        public static bool Contains(BoxCollider area,Vector3 position)
        {
            if(area==null)return false;
            Vector3 local=area.transform.InverseTransformPoint(position)-area.center;
            Vector3 half=area.size*.5f;
            return Mathf.Abs(local.x)<=half.x && Mathf.Abs(local.y)<=half.y && Mathf.Abs(local.z)<=half.z;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void InteractRpc(RpcParams rpc=default)
        {
            if(!NetworkRoundGate.IsOpen || !NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId,out var client) || client.PlayerObject==null)return;
            var p=client.PlayerObject.GetComponent<PlayerClassController>();
            if(p==null || !p.IsReady.Value || !p.RoundStarted.Value || p.IsDowned.Value)return;
            Transform terminal=CurrentStage==Stage.Prepare?departureConsole:CurrentStage==Stage.Drive?checkpointConsole:null;
            if(terminal==null || Vector3.Distance(p.transform.position,terminal.position)>3.5f)return;
            if(CurrentStage==Stage.Prepare){Phase.Value=(byte)Stage.Drive;nextSpawn=Time.time+4;}
            else if(Contains(checkpointArea,bus.position)){timer=defenseSeconds;SecondsRemaining.Value=Mathf.CeilToInt(timer);Phase.Value=(byte)Stage.Checkpoint;}
        }
        public bool EveryoneAboard()
        {
            int count=0;
            foreach(var client in NetworkManager.ConnectedClientsList)
            {
                var p=client.PlayerObject!=null?client.PlayerObject.GetComponent<PlayerClassController>():null;
                if(p==null || !p.IsReady.Value || !p.RoundStarted.Value)continue;
                count++;
                var layout=bus.GetComponent<BusDefenseLayout>();
                if(layout!=null){if(!layout.ContainsPassenger(p.transform.position,.1f))return false;continue;}
                Vector3 local=bus.InverseTransformPoint(p.transform.position);
                if(Mathf.Abs(local.x)>1.65f || local.y<.25f || local.y>3.25f || local.z< -4.9f || local.z>6.25f)return false;
            }
            return count>0;
        }
        void SpawnPressure()
        {
            int players=Mathf.Max(1,NetworkManager.ConnectedClients.Count);
            int limit=CurrentStage==Stage.Checkpoint?10+4*(players-1):6+3*(players-1);
            nextSpawn=Time.time+(players==1?2.2f:1.3f);
            enemies.RemoveAll(n=>n==null || !n.IsSpawned);
            for(int i=enemies.Count-1;i>=0;i--){
                var enemy=enemies[i];if(Vector3.Distance(enemy.transform.position,bus.position)<95)continue;
                bool nearSurvivor=false;
                foreach(var client in NetworkManager.ConnectedClientsList)if(client.PlayerObject!=null && Vector3.Distance(client.PlayerObject.transform.position,enemy.transform.position)<90){nearSurvivor=true;break;}
                if(!nearSurvivor){enemy.Despawn(true);enemies.RemoveAt(i);}
            }
            int living=0;foreach(var n in enemies)if(n.TryGetComponent<Health>(out var health) && health.CurrentHealth>0)living++;
            if(living>=Mathf.Min(38,limit) || zombiePrefabs==null || zombiePrefabs.Length==0 || spawnPoints==null)return;
            // Authored roadside entrances keep spawns away from the bus and off the road ahead.
            int start=Random.Range(0,spawnPoints.Length);
            for(int i=0;i<spawnPoints.Length;i++)
            {
                var anchor=spawnPoints[(start+i)%spawnPoints.Length];
                float gap=Vector3.Distance(anchor.position,bus.position);
                if(gap<11 || gap>42)continue;
                bool tooClose=false;
                foreach(var client in NetworkManager.ConnectedClientsList)if(client.PlayerObject!=null && Vector3.Distance(anchor.position,client.PlayerObject.transform.position)<9){tooClose=true;break;}
                if(tooClose || !NavMesh.SamplePosition(anchor.position,out var hit,2,NavMesh.AllAreas))continue;
                var go=Instantiate(zombiePrefabs[Random.Range(0,zombiePrefabs.Length)],hit.position,Quaternion.identity);
                var net=go.GetComponent<NetworkObject>();net.Spawn();enemies.Add(net);
                return;
            }
        }
        void Refresh()
        {
            if(checkpointGate!=null)checkpointGate.SetActive(Phase.Value<(byte)Stage.FinalRun);
            if(checkpointGate!=null)for(int i=0;i<checkpointGate.transform.childCount;i++)
            {
                var indicator=checkpointGate.transform.GetChild(i);
                if(indicator.name.StartsWith("Release lock ") && int.TryParse(indicator.name.Substring(13),out int step))indicator.gameObject.SetActive(step>GateReleaseStep.Value);
            }
            if(departureMarker!=null)departureMarker.SetActive(CurrentStage==Stage.Prepare);
            if(checkpointMarker!=null)checkpointMarker.SetActive(CurrentStage==Stage.Drive || CurrentStage==Stage.Checkpoint);
            bool routeDone=RouteProgress.Value>=(routeCheckpoints?.Length??0);
            if(extractionMarker!=null)extractionMarker.SetActive(CurrentStage==Stage.FinalRun && routeDone);
            if(routeCheckpoints!=null)for(int i=0;i<routeCheckpoints.Length;i++){
                var marker=routeCheckpoints[i].Find("Route beacon");if(marker!=null)marker.gameObject.SetActive(CurrentStage==Stage.FinalRun && RouteProgress.Value==i);
            }
            if(objective==null)return;
            if(CurrentStage==Stage.Complete){if(!completedLocally){completedLocally=true;objective.CompleteObjective("","",GameLocalization.Text("You escaped together", "Jullie zijn samen ontsnapt"));}return;}
            string title=CurrentStage switch {
                Stage.Prepare=>GameLocalization.Text("Prepare the bus", "Maak de bus klaar"),
                Stage.Drive=>GameLocalization.Text("Drive to the checkpoint", "Rijd naar de controlepost"),
                Stage.Checkpoint=>GameLocalization.Text("Hold the checkpoint", "Verdedig de controlepost"),
                _=>GameLocalization.Text("Last run: everyone aboard", "Laatste rit: iedereen aan boord")};
            string hint=CurrentStage switch {
                Stage.Prepare=>GameLocalization.Text("Take supplies, board windows, then F at the departure radio.", "Pak voorraden, timmer ramen dicht en druk F bij de vertrekpost."),
                Stage.Drive=>GameLocalization.Text("F at the wheel to drive. Park by the barrier; F at the roadside control.", "F bij het stuur om te rijden. Parkeer bij de slagboom; F bij de bediening."),
                Stage.Checkpoint=>WaitingForBus.Value?GameLocalization.Text("Bring the bus back to the checkpoint", "Breng de bus terug naar de controlepost"):GameLocalization.Text("Protect the bus while the barrier opens", "Bescherm de bus terwijl de doorgang wordt geopend"),
                _=>GameLocalization.Text("Park in the evacuation bay with every survivor inside the bus.", "Parkeer in het evacuatievak met alle overlevenden in de bus.")};
            if(CurrentStage==Stage.FinalRun && !routeDone){
                int metres=Mathf.RoundToInt(Vector3.Distance(bus.position,routeCheckpoints[RouteProgress.Value].position));
                hint=GameLocalization.Text("Follow the evacuation loop", "Volg de evacuatie-route")+" — "+(RouteProgress.Value+1)+"/"+routeCheckpoints.Length+" — "+metres+"m";
            }
            objective.UpdateObjective(title,CurrentStage==Stage.Checkpoint?SecondsRemaining.Value+"s | "+GateReleaseStep.Value*20+"%":"",hint);
        }
    }
}
