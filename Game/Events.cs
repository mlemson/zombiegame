using UnityEngine;

namespace Unity.FPS.Game
{
    // The Game Events used across the Game.
    // Anytime there is a need for a new event, it should be added here.

    public static class Events
    {
        public static ObjectiveUpdateEvent ObjectiveUpdateEvent = new ObjectiveUpdateEvent();
        public static AllObjectivesCompletedEvent AllObjectivesCompletedEvent = new AllObjectivesCompletedEvent();
        public static GameOverEvent GameOverEvent = new GameOverEvent();
        public static PlayerDeathEvent PlayerDeathEvent = new PlayerDeathEvent();
        public static EnemyKillEvent EnemyKillEvent = new EnemyKillEvent();
        public static PickupEvent PickupEvent = new PickupEvent();
        public static AmmoPickupEvent AmmoPickupEvent = new AmmoPickupEvent();
        public static DamageEvent DamageEvent = new DamageEvent();
        public static PenetrationHitEvent PenetrationHitEvent = new PenetrationHitEvent();
        public static MeleeDamageEvent MeleeDamageEvent = new MeleeDamageEvent();
        public static DisplayMessageEvent DisplayMessageEvent = new DisplayMessageEvent();
        public static EdgeWarningEvent EdgeWarningEvent = new EdgeWarningEvent();
    }

    public class ObjectiveUpdateEvent : GameEvent
    {
        public Objective Objective;
        public string DescriptionText;
        public string CounterText;
        public bool IsComplete;
        public string NotificationText;
    }

    public class AllObjectivesCompletedEvent : GameEvent { }

    public class GameOverEvent : GameEvent
    {
        public bool Win;
    }

    public class PlayerDeathEvent : GameEvent { }

    public class EnemyKillEvent : GameEvent
    {
        public GameObject Enemy;
        public int RemainingEnemyCount;
    }

    public class PickupEvent : GameEvent
    {
        public GameObject Pickup;
    }

    public class AmmoPickupEvent : GameEvent
    {
        public WeaponController Weapon;
    }

    public class DamageEvent : GameEvent
    {
        public GameObject Sender;
        public float DamageValue;
        public bool IsHeadshot;
        public bool IsLethal;
    }

    public class PenetrationHitEvent : GameEvent
    {
        public GameObject Sender;
        public int HitNumber;
        public bool IsLethal;
    }

    public class MeleeDamageEvent : GameEvent
    {
        public GameObject Sender;
        public float DamageValue;
        public bool IsLethal;
        public bool IsSpecial;
        public int KillCount;
    }

    public class DisplayMessageEvent : GameEvent
    {
        public string Message;
        public float DelayBeforeDisplay;
    }

    public class EdgeWarningEvent : GameEvent
    {
        public string Message;
        public Vector3 DangerDirection;
    }
}
