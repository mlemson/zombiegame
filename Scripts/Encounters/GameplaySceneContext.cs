using UnityEngine;
using Unity.Netcode;
using ZombieTown.LevelFour;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    public sealed class GameplaySceneContext : MonoBehaviour
    {
        public GameBalanceDatabase balance;
        public LevelEncounterProfile encounter;
        public RetreatDefenseMissionController mission;
        public static GameplaySceneContext Active { get; private set; }
        GUIStyle materialsStyle;
        int cachedPlayerCount = 1;
        float nextPlayerCountRefresh;
        public int Stage => mission != null ? mission.CurrentLine.Value+1 : 1;
        public int Round => Stage;
        public int PlayerCount {
            get {
                if(Time.unscaledTime < nextPlayerCountRefresh)return cachedPlayerCount;
                var manager=NetworkManager.Singleton; int count=0;
                if(manager != null && manager.IsListening){
                    if(manager.IsServer)foreach(var client in manager.ConnectedClientsList){var p=client.PlayerObject!=null?client.PlayerObject.GetComponent<PlayerClassController>():null;if(p!=null && p.IsReady.Value)count++;}
                    else foreach(var obj in manager.SpawnManager.SpawnedObjectsList){var p=obj.GetComponent<PlayerClassController>();if(p!=null && p.IsReady.Value)count++;}
                }
                cachedPlayerCount=Mathf.Clamp(count,1,8);
                nextPlayerCountRefresh=Time.unscaledTime+.1f;
                return cachedPlayerCount;
            }
        }
        void Update() {
            var manager=NetworkManager.Singleton;
            if(manager==null || !manager.IsServer || balance==null || balance.defenseSupplies==null)return;
            foreach(var client in manager.ConnectedClientsList) {
                var p=client.PlayerObject!=null?client.PlayerObject.GetComponent<PlayerClassController>():null;if(p!=null && p.IsReady.Value)p.InitializeDefenseSupplies(balance.defenseSupplies);
            }
        }
        void OnGUI() {
            var p=NetworkManager.Singleton?.LocalClient?.PlayerObject?.GetComponent<PlayerClassController>();
            if(p!=null && p.IsReady.Value && balance!=null && balance.defenseSupplies!=null) {
                float uiScale=Mathf.Max(.75f,Screen.height/1080f);
                materialsStyle ??= new GUIStyle(GUI.skin.box) { fontStyle=FontStyle.Bold, alignment=TextAnchor.MiddleCenter };
                materialsStyle.fontSize=Mathf.RoundToInt(20*uiScale);
                GUI.Box(new Rect(20*uiScale,65*uiScale,230*uiScale,34*uiScale),Unity.FPS.Game.GameLocalization.Text("MATERIALS: ","MATERIALEN: ")+p.Materials.Value,materialsStyle);
            }
        }
        void OnEnable() => Active=this;
        void OnDisable() { if(Active==this) Active=null; }
    }
}
