using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    public static class FoundationInteraction
    {
        public static bool TryPlayer(ulong id,Vector3 position,float radius,out PlayerClassController player)
        {
            player=null; var manager=NetworkManager.Singleton;
            if(manager==null || !manager.IsServer || !manager.ConnectedClients.TryGetValue(id,out var client) || client.PlayerObject==null) return false;
            player=client.PlayerObject.GetComponent<PlayerClassController>();
            return player!=null && player.IsReady.Value && player.RoundStarted.Value && !player.IsDowned.Value && Vector3.Distance(player.transform.position,position)<=radius;
        }
        public static PlayerClassController Local(Vector3 position,float radius)
        {
            if(Unity.FPS.Game.GameplayInteraction.Carrying)return null;
            var manager=NetworkManager.Singleton;
            if(manager==null || !manager.IsListening || manager.LocalClient?.PlayerObject==null || Time.timeScale<=0) return null;
            var p=manager.LocalClient.PlayerObject.GetComponent<PlayerClassController>();
            return p!=null && p.IsReady.Value && p.RoundStarted.Value && !p.IsDowned.Value && Vector3.Distance(p.transform.position,position)<=radius ? p:null;
        }
        public static bool Pressed=>Keyboard.current!=null && Unity.FPS.Game.GameplayInteraction.Pressed;
        public static bool Held=>Keyboard.current!=null && Unity.FPS.Game.GameplayInteraction.Held;
        static GUIStyle promptStyle;
        public static void Prompt(string text) {
            float uiScale=Mathf.Max(.75f,Screen.height/1080f);
            promptStyle ??= new GUIStyle(GUI.skin.box) { alignment=TextAnchor.MiddleCenter, wordWrap=true };
            promptStyle.fontSize=Mathf.RoundToInt(22*uiScale);
            GUI.Box(new Rect(Screen.width*.2f,Screen.height*.72f,Screen.width*.6f,120*uiScale),text,promptStyle);
        }
    }
}
