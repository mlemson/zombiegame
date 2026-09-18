using UnityEngine;
namespace Unity.FPS.Game
{
    [CreateAssetMenu(menuName="Zombie Town/Crouch Profile")]
    public sealed class CrouchProfile : ScriptableObject
    {
        public float standingHeight=1.8f, crouchingHeight=.9f, standingCameraY=1.62f, crouchingCameraY=.81f;
        [Range(.05f,.5f)] public float crouchTransitionTime=.16f;
    }
}
