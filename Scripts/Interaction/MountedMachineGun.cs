using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.FPS.AI;
using ZombieTown.Multiplayer;
using ZombieTown.Traversal;

namespace ZombieTown.Foundation
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class MountedMachineGun : NetworkBehaviour
    {
        public Transform operatorPoint, exitPoint, sight, muzzle, swivel;
        public Vector3 cameraOffset = new(0f, .65f, -.15f);
        public float interactionRadius=2.5f, damage=24, shotsPerSecond=9, range=90;
        [Range(5,160)] public float yawLimit=65;
        public float minimumPitch=-25, maximumPitch=45;
        public AudioSource shotAudio;
        public ParticleSystem muzzleFlash;
        public Light muzzleLight;
        public LineRenderer shotTracer;
        public ParticleSystem impactSparks;
        readonly RaycastHit[] shotHits=new RaycastHit[64];
        float flashUntil;
        public readonly NetworkVariable<ulong> Operator=new(ulong.MaxValue);
        public readonly NetworkVariable<Vector2> Aim=new();
        [Tooltip("Cost to unlock this turret, same base price as hiring a guard (see RetreatDefenseMissionController.guardHirePrice).")]
        public int purchaseCost=400;
        public readonly NetworkVariable<bool> Purchased=new(false);
        public int Price=>MultiplayerPriceScaling.Scale(purchaseCost, MultiplayerPriceScaling.GetActivePlayerCount());
        PlayerClassController localPlayer;
        PlayerCharacterController movement;
        PlayerInputHandler input;
        Camera view;
        Vector3 savedCameraPosition;
        Quaternion savedCameraRotation;
        readonly Dictionary<Behaviour,bool> suspended=new();
        Vector2 localAim;
        float nextShot, nextAim;
        bool exiting;
        GameObject heldWeaponSocket;
        bool heldWeaponSocketWasActive;

        public override void OnNetworkDespawn() { ReleaseAuthority(); RestoreLocal(); }
        void OnDisable() { ReleaseAuthority(); RestoreLocal(); }
        void ReleaseAuthority() {
            if(!IsServer || NetworkManager==null)return;
            if(NetworkManager.ConnectedClients.TryGetValue(Operator.Value,out var c) && c.PlayerObject!=null) {
                var p=c.PlayerObject.GetComponent<PlayerClassController>(); if(p!=null && p.MountedGun==this)p.MountedGun=null;
            }
            if(IsSpawned)Operator.Value=ulong.MaxValue;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void UseRpc(bool enter,RpcParams rpc=default) {
            ulong id=rpc.Receive.SenderClientId;
            if(!enter) { if(Operator.Value==id)ReleaseAuthority();return; }
            if(operatorPoint==null || exitPoint==null || muzzle==null || sight==null || Operator.Value!=ulong.MaxValue ||
                !FoundationInteraction.TryPlayer(id,operatorPoint.position,interactionRadius,out var p) || p.IsRepairingDefense || p.IsUsingMountedGun)return;
            if(!Purchased.Value) {
                if(p.Points.Value<Price)return;
                p.Points.Value-=Price;Purchased.Value=true;
            }
            p.MountedGun=this;Operator.Value=id;Aim.Value=Vector2.zero;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone,Delivery=RpcDelivery.Unreliable)]
        public void AimRpc(Vector2 angles,bool fire,RpcParams rpc=default) {
            if(Operator.Value!=rpc.Receive.SenderClientId || !float.IsFinite(angles.x) || !float.IsFinite(angles.y) || operatorPoint==null || muzzle==null ||
                !FoundationInteraction.TryPlayer(Operator.Value,operatorPoint.position,interactionRadius+.5f,out var p) || p.MountedGun!=this)return;
            Aim.Value=ClampAim(angles);
            if(swivel!=null)swivel.localRotation=Quaternion.Euler(-Aim.Value.y,Aim.Value.x,0);
            if(!fire || Time.time<nextShot || !NetworkRoundGate.IsOpen)return;
            nextShot=Time.time+1/Mathf.Max(1,shotsPerSecond);
            Vector3 direction=transform.rotation*Quaternion.Euler(-Aim.Value.y,Aim.Value.x,0)*Vector3.forward;
            CameraPose(Aim.Value,out var cameraPosition,out var cameraRotation);
            // Resolve the crosshair target from the elevated view, then check the muzzle
            // independently so the gun cannot shoot through cover below the camera.
            Vector3 target=cameraPosition+cameraRotation*Vector3.forward*Mathf.Max(1,range);
            float closest=range;
            int count=Physics.RaycastNonAlloc(cameraPosition,cameraRotation*Vector3.forward,shotHits,range,~0,QueryTriggerInteraction.Ignore);
            if(count==shotHits.Length)return;
            for(int i=0;i<count;i++) {var hit=shotHits[i];
                if(hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(p.transform) || hit.distance>=closest)continue;
                closest=hit.distance;target=hit.point;
            }
            if((target-muzzle.position).sqrMagnitude>.0001f)direction=(target-muzzle.position).normalized;
            // Resolve the first physical occluder on the server, ignoring only this post and its operator.
            count=Physics.RaycastNonAlloc(muzzle.position,direction,shotHits,Mathf.Max(1,range),~0,QueryTriggerInteraction.Ignore);
            if(count==shotHits.Length)return;
            int first=-1;closest=range;
            for(int i=0;i<count;i++){var h=shotHits[i];if(!h.transform.IsChildOf(transform) && !h.transform.IsChildOf(p.transform) && h.distance<closest){first=i;closest=h.distance;}}
            Vector3 endpoint=muzzle.position+direction*range,normal=-direction;
            if(first>=0) {var hit=shotHits[first];endpoint=hit.point;normal=hit.normal;
                var zombie=hit.collider.GetComponentInParent<ZombieAI>();
                var zombieHealth=zombie!=null?zombie.GetComponent<Health>():null;
                if(zombieHealth!=null) {
                    float healthBefore=zombieHealth.CurrentHealth;
                    zombieHealth.TakeDamage(Mathf.Max(0,damage),p.gameObject);
                    float dealt=Mathf.Max(0,healthBefore-zombieHealth.CurrentHealth);
                    if(dealt>0)TurretHitFeedbackRpc(dealt,zombieHealth.CurrentHealth<=0,RpcTarget.Single(Operator.Value,RpcTargetUse.Temp));
                }
            }
            ShotRpc(muzzle.position,endpoint,normal,first>=0);
        }
        [Rpc(SendTo.ClientsAndHost)] void ShotRpc(Vector3 origin,Vector3 endpoint,Vector3 normal,bool impact) {
            if(shotAudio!=null && shotAudio.clip!=null)shotAudio.PlayOneShot(shotAudio.clip);
            if(muzzleFlash!=null){if(!muzzleFlash.isPlaying)muzzleFlash.Play();muzzleFlash.Emit(3);}
            flashUntil=Time.time+.065f;if(muzzleLight!=null)muzzleLight.enabled=true;
            if(shotTracer!=null){shotTracer.SetPosition(0,origin);shotTracer.SetPosition(1,endpoint);shotTracer.enabled=true;}
            if(impact && impactSparks!=null){if(!impactSparks.isPlaying)impactSparks.Play();var particle=new ParticleSystem.EmitParams{position=endpoint+normal*.02f,velocity=normal*2};impactSparks.Emit(particle,5);}
        }
        // Lets the operator's hitmarker/kill-feed react to turret damage, same as a held weapon.
        [Rpc(SendTo.SpecifiedInParams)] void TurretHitFeedbackRpc(float damageDealt,bool lethal,RpcParams rpcParams=default) {
            var local=NetworkManager.LocalClient?.PlayerObject;
            var evt=Events.DamageEvent;evt.Sender=local!=null?local.gameObject:gameObject;evt.DamageValue=damageDealt;evt.IsHeadshot=false;evt.IsLethal=lethal;
            EventManager.Broadcast(evt);
        }
        Vector2 ClampAim(Vector2 a)=>new(Mathf.Clamp(a.x,-yawLimit,yawLimit),Mathf.Clamp(a.y,minimumPitch,maximumPitch));
        void Update() {
            if(muzzleLight!=null && Time.time>=flashUntil)muzzleLight.enabled=false;
            if(shotTracer!=null && Time.time>=flashUntil)shotTracer.enabled=false;
            if(!IsSpawned)return;
            if(IsServer && Operator.Value!=ulong.MaxValue && (operatorPoint==null || !FoundationInteraction.TryPlayer(Operator.Value,operatorPoint.position,interactionRadius+.5f,out _)))ReleaseAuthority();
            bool ours=IsClient && Operator.Value==NetworkManager.LocalClientId;
            if(!ours) { exiting=false;RestoreLocal(); }
            if(ours && !exiting) {
                if(localPlayer==null)MountLocal();
                if(localPlayer==null)return;
                if(FoundationInteraction.Pressed || (Keyboard.current?.escapeKey.wasPressedThisFrame??false)) {
                    GameplayInteraction.Consume();exiting=true;UseRpc(false);RestoreLocal();return;
                }
                if(input==null || !input.CanProcessInput())return;
                Vector2 mouse=Mouse.current!=null?Mouse.current.delta.ReadValue():Vector2.zero;
                localAim=ClampAim(localAim+mouse*.08f);
                if(Time.unscaledTime>=nextAim) {nextAim=Time.unscaledTime+.05f;AimRpc(localAim,Mouse.current?.leftButton.isPressed??false);}
            } else if(!ours && Operator.Value==ulong.MaxValue && operatorPoint!=null) {
                var p=FoundationInteraction.Local(operatorPoint.position,interactionRadius);
                if(p!=null && !p.IsUsingMountedGun && !p.IsRepairingDefense && FoundationInteraction.Pressed){GameplayInteraction.Consume();UseRpc(true);}
            }
            if(swivel!=null)swivel.localRotation=Quaternion.Euler(-Aim.Value.y,Aim.Value.x,0);
        }
        void LateUpdate() {
            if(localPlayer==null || view==null)return;
            localPlayer.transform.position=operatorPoint.position;
            CameraPose(localAim,out var cameraPosition,out var cameraRotation);
            view.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
        }
        void CameraPose(Vector2 angles,out Vector3 position,out Quaternion rotation) {
            Quaternion aimRotation=transform.rotation*Quaternion.Euler(-angles.y,angles.x,0);
            position=sight.position+aimRotation*cameraOffset;
            Vector3 aimPoint=muzzle.position+aimRotation*Vector3.forward*Mathf.Max(1,range);
            rotation=Quaternion.LookRotation(aimPoint-position,aimRotation*Vector3.up);
        }
        void Suspend(Behaviour b) { if(b==null)return;suspended[b]=b.enabled;b.enabled=false; }
        void MountLocal() {
            localPlayer=NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerClassController>();if(localPlayer==null)return;
            localPlayer.MountedGun=this;movement=localPlayer.GetComponent<PlayerCharacterController>();input=localPlayer.GetComponent<PlayerInputHandler>();
            view=movement!=null?movement.PlayerCamera:null;
            if(view!=null){savedCameraPosition=view.transform.localPosition;savedCameraRotation=view.transform.localRotation;}
            Suspend(localPlayer.GetComponent<PlayerLadderClimber>());Suspend(localPlayer.GetComponent<PlayerZiplineRider>());Suspend(movement);
            var weapons=localPlayer.GetComponent<PlayerWeaponsManager>();
            Suspend(weapons);
            heldWeaponSocket=weapons!=null && weapons.WeaponParentSocket!=null?weapons.WeaponParentSocket.gameObject:null;
            if(heldWeaponSocket!=null){heldWeaponSocketWasActive=heldWeaponSocket.activeSelf;heldWeaponSocket.SetActive(false);}
            input?.SetMountedFireBlocked(true);localAim=Aim.Value;
        }
        void RestoreLocal() {
            if(localPlayer==null)return;
            if(view!=null){view.transform.localPosition=savedCameraPosition;view.transform.localRotation=savedCameraRotation;}
            if(exitPoint!=null) {
                var cc=localPlayer.GetComponent<CharacterController>();bool was=cc!=null && cc.enabled;if(cc!=null)cc.enabled=false;
                localPlayer.transform.position=exitPoint.position;if(cc!=null)cc.enabled=was;
            }
            foreach(var pair in suspended)if(pair.Key!=null)pair.Key.enabled=pair.Value;suspended.Clear();
            if(heldWeaponSocket!=null)heldWeaponSocket.SetActive(heldWeaponSocketWasActive);
            heldWeaponSocket=null;
            input?.SetMountedFireBlocked(false);if(localPlayer.MountedGun==this)localPlayer.MountedGun=null;
            localPlayer=null;view=null;movement=null;input=null;
        }
        void OnGUI() {
            if(!IsSpawned || operatorPoint==null)return;
            if(localPlayer!=null){FoundationInteraction.Prompt(GameLocalization.Text("MACHINE GUN — mouse: aim/fire — F or Esc: exit","MITRAILLEUR — muis: richten/schieten — F of Esc: uitstappen"));return;}
            var local=FoundationInteraction.Local(operatorPoint.position,interactionRadius);
            if(local==null)return;
            if(Operator.Value!=ulong.MaxValue){FoundationInteraction.Prompt(GameLocalization.Text("MACHINE GUN OCCUPIED","MITRAILLEUR BEZET"));return;}
            if(!Purchased.Value) {
                FoundationInteraction.Prompt(local.Points.Value>=Price
                    ?GameLocalization.Text($"F  BUY MACHINE GUN  -  ${Price}",$"F  MITRAILLEUR KOPEN  -  ${Price}")
                    :GameLocalization.Text($"MACHINE GUN  -  ${Price}  (NEED ${Price-local.Points.Value})",$"MITRAILLEUR  -  ${Price}  (NOG ${Price-local.Points.Value})"));
                return;
            }
            FoundationInteraction.Prompt(GameLocalization.Text("F — use machine gun","F — mitrailleur bedienen"));
        }
    }
}
