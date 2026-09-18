using Unity.Netcode.Components;

namespace ZombieTown.Multiplayer
{
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
