using UnityEngine;
using Unity.Netcode;
using Unity.FPS.Game;
using ZombieTown.Foundation;
using ZombieTown.Multiplayer;

namespace ZombieTown.Finale
{
    public sealed class FinaleSupplyTerminal : NetworkBehaviour
    {
        public enum Product { Armor, Turret, Salvage }
        public Product product;
        public int price=150;
        public float armorCapacity=100;
        public GameObject turretKitPrefab;
        public Transform interactionPoint;
        public readonly NetworkVariable<bool> Collected=new();
        public Vector3 Position=>interactionPoint!=null?interactionPoint.position:transform.position+Vector3.up;
        void Update(){if(IsSpawned && !Collected.Value && FoundationInteraction.Local(Position,2.8f)!=null && GameplayInteraction.Pressed){GameplayInteraction.Consume();BuyRpc();}}
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void BuyRpc(RpcParams rpc=default)
        {
            if(Collected.Value || !FoundationInteraction.TryPlayer(rpc.Receive.SenderClientId,Position,3,out var p) || p.IsUsingMountedGun || p.IsRepairingDefense)return;
            if(Physics.Linecast(p.transform.position+Vector3.up*1.7f,Position,out var hit,~0,QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(transform))return;
            if(product==Product.Armor){p.TryBuyArmor(price,armorCapacity);return;}
            if(product==Product.Salvage){Collected.Value=true;p.Points.Value+=Mathf.Max(0,price);return;}
            if(turretKitPrefab==null || p.Points.Value<price || CarryableDefenseItem.IsCarrying(p.OwnerClientId))return;
            var kit=Instantiate(turretKitPrefab,Position,Quaternion.identity).GetComponent<CarryableDefenseItem>();
            kit.NetworkObject.Spawn();kit.Carrier.Value=p.OwnerClientId;p.Points.Value-=Mathf.Max(0,price);
        }
        void OnGUI(){
            if(!IsSpawned || FoundationInteraction.Local(Position,2.8f)==null)return;
            if(Collected.Value){FoundationInteraction.Prompt(GameLocalization.Text("SUPPLIES COLLECTED","VOORRADEN MEEGENOMEN"));return;}
            string text=product==Product.Armor?GameLocalization.Text("ARMOR / REFILL","PANTSER / AANVULLEN"):product==Product.Turret?GameLocalization.Text("PORTABLE TURRET - 60s after placing","DRAAGBARE TURRET - 60s na plaatsen"):GameLocalization.Text("SALVAGE","BUIT");
            FoundationInteraction.Prompt("F  "+text+"\n"+(product==Product.Salvage?"+":"$")+price);
        }
    }
}
