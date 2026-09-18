using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.FPS.Game
{
    public static class GameplayInteraction
    {
        static int consumedFrame=-1;
        public static bool Carrying { get; set; }
        public static bool RawPressed=>Keyboard.current!=null && Keyboard.current.fKey.wasPressedThisFrame;
        public static bool Pressed=>!Carrying && consumedFrame!=Time.frameCount && RawPressed;
        public static bool Held=>!Carrying && consumedFrame!=Time.frameCount && Keyboard.current!=null && Keyboard.current.fKey.isPressed;
        public static void Consume()=>consumedFrame=Time.frameCount;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){consumedFrame=-1;Carrying=false;}
    }
}
