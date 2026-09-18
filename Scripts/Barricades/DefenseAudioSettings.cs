using UnityEngine;
using Unity.FPS.Game;

namespace ZombieTown.Foundation
{
    [CreateAssetMenu(menuName="Zombie Town/Defense audio")]
    public sealed class DefenseAudioSettings : ScriptableObject
    {
        public AudioClip plankPlaced, plankBroken, gateBreak;
        [Range(0,1)] public float volume=.65f;
        static DefenseAudioSettings cached;
        public enum Cue { Placed, Broken, Gate }
        public static void Play(Cue cue, Vector3 position)
        {
            if(cached==null)cached=Resources.Load<DefenseAudioSettings>("DefenseAudioSettings");
            if(cached==null)return;
            var clip=cue==Cue.Placed?cached.plankPlaced:cue==Cue.Broken?cached.plankBroken:cached.gateBreak;
            if(clip!=null)AudioUtility.CreateSFX(clip,position,AudioUtility.AudioGroups.Impact,1,3,cached.volume);
        }
        // Healing rearms a threshold. One large hit is one audible crunch, not stacked voices.
        public static bool CrossedDamageStep(float before,float after,float maximum)
        {
            if(maximum<=0 || after>=before)return false;
            return Mathf.FloorToInt((1-Mathf.Clamp01(after/maximum))*5+.0001f)>
                Mathf.FloorToInt((1-Mathf.Clamp01(before/maximum))*5+.0001f);
        }
    }
}
