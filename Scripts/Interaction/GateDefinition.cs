using UnityEngine;
namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Gate Definition")]
    public sealed class GateDefinition : ScriptableObject
    {
        public int baseCost=120, requiredStage=1, nextStage=2;
        public float interactionRadius=4, openHeight=6.5f;
        public string interactionPrompt="F  UNLOCK GATE";
    }
}
