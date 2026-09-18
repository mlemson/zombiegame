using Unity.Netcode;
namespace ZombieTown.Multiplayer
{
    public partial class PlayerClassController
    {
        public readonly NetworkVariable<float> PurchasedArmorCapacity = new();
        public float ArmorCapacity => UnityEngine.Mathf.Max(GetArchetype(SelectedClass.Value).MaxArmor,PurchasedArmorCapacity.Value);
        public bool TryBuyArmor(int price,float capacity)
        {
            if(!IsServer || !IsReady.Value || !RoundStarted.Value || IsDowned.Value || Points.Value<price)return false;
            float maximum=UnityEngine.Mathf.Max(ArmorCapacity,capacity);
            if(Armor.Value>=maximum)return false;
            Points.Value-=UnityEngine.Mathf.Max(0,price);PurchasedArmorCapacity.Value=maximum;Armor.Value=maximum;return true;
        }
    }
}
