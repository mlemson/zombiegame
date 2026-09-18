using System;
using UnityEngine;

namespace ZombieTown.LevelTwo
{
    public static class CombatNoiseSystem
    {
        public const float UnsilencedGunshotRadius = 45f;

        public static event Action<Vector3, float, GameObject> NoiseReported;

        public static void Report(Vector3 position, float radius, GameObject source)
        {
            NoiseReported?.Invoke(position, Mathf.Max(0f, radius), source);
        }
    }
}
