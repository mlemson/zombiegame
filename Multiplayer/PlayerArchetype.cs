using System;
using UnityEngine;

namespace ZombieTown.Multiplayer
{
    public enum PlayerArchetype : byte { Guardian, Gunslinger, Striker, Medic }

    [Serializable]
    public struct ArchetypeTuning
    {
        public PlayerArchetype Type;
        public string DisplayName;
        [TextArea] public string Benefit;
        public GameObject CharacterPrefab;
        public Unity.FPS.Game.WeaponController StartingWeapon;
        public GameObject HandPropPrefab;
        public Vector3 HandPropPosition;
        public Vector3 HandPropRotation;
        public Vector3 HandPropScale;
        public float MaxArmor;
        public float ArmorRechargePerSecond;
        public float ArmorRechargeDelay;
        public float MeleeDamage;
        public float HealAmount;
    }
}
