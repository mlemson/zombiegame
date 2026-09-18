using System;
using System.Reflection;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public class ObjectiveKillEnemies : Objective
    {
        [Tooltip("Choose whether you need to kill every enemy or only a minimum amount")]
        public bool MustKillAllEnemies = true;

        [Tooltip("Base kill target for one player.")]
        public int BaseKillsToCompleteObjective = 30;

        [Tooltip("Additional kills added for each player beyond the first.")]
        public int AdditionalKillsPerPlayer = 30;

        [Tooltip("If MustKillAllEnemies is false, this is the amount of enemy kills required")]
        public int KillsToCompleteObjective = 5;

        [Tooltip("Start sending notification about remaining enemies when this amount of enemies is left")]
        public int NotificationEnemiesRemainingThreshold = 3;

        int m_KillTotal;

        protected override void Start()
        {
            base.Start();

            EventManager.AddListener<EnemyKillEvent>(OnEnemyKilled);

            if (!MustKillAllEnemies)
                KillsToCompleteObjective = GetPlayerScaledKillTarget();

            // set a title and description specific for this type of objective, if it hasn't one
            if (string.IsNullOrEmpty(Title))
                Title = "Eliminate " + (MustKillAllEnemies ? "all the" : KillsToCompleteObjective.ToString()) +
                        " enemies";

            if (string.IsNullOrEmpty(Description))
                Description = GetUpdatedCounterAmount();
        }

        int GetPlayerScaledKillTarget()
        {
            if (BaseKillsToCompleteObjective <= 0)
                BaseKillsToCompleteObjective = 30;
            if (AdditionalKillsPerPlayer < 0)
                AdditionalKillsPerPlayer = 0;

            int playerCount = 1;
            Component[] components = UnityEngine.Object.FindObjectsByType<Component>();
            if (components != null && components.Length > 0)
            {
                int readyPlayers = 0;
                foreach (Component component in components)
                {
                    if (component == null) continue;
                    Type type = component.GetType();
                    if (!type.FullName?.Contains("PlayerClassController", System.StringComparison.OrdinalIgnoreCase) ?? true)
                        continue;

                    PropertyInfo readyProperty = type.GetProperty("IsReady");
                    if (readyProperty == null) continue;

                    object readyValue = readyProperty.GetValue(component);
                    if (readyValue == null) continue;

                    PropertyInfo valueProperty = readyValue.GetType().GetProperty("Value");
                    if (valueProperty == null) continue;

                    if (valueProperty.GetValue(readyValue) is bool ready && ready)
                        readyPlayers++;
                }

                if (readyPlayers > 0)
                    playerCount = readyPlayers;
            }

            int extraPlayers = Mathf.Max(0, playerCount - 1);
            return BaseKillsToCompleteObjective + extraPlayers * AdditionalKillsPerPlayer;
        }

        void OnEnemyKilled(EnemyKillEvent evt)
        {
            if (IsCompleted)
                return;

            m_KillTotal++;

            if (MustKillAllEnemies)
                KillsToCompleteObjective = evt.RemainingEnemyCount + m_KillTotal;
            else
                KillsToCompleteObjective = GetPlayerScaledKillTarget();

            int targetRemaining = MustKillAllEnemies ? evt.RemainingEnemyCount : KillsToCompleteObjective - m_KillTotal;

            // update the objective text according to how many enemies remain to kill
            if (targetRemaining == 0)
            {
                CompleteObjective(string.Empty, GetUpdatedCounterAmount(), "Objective complete : " + Title);
            }
            else if (targetRemaining == 1)
            {
                string notificationText = NotificationEnemiesRemainingThreshold >= targetRemaining
                    ? "One enemy left"
                    : string.Empty;
                UpdateObjective(string.Empty, GetUpdatedCounterAmount(), notificationText);
            }
            else
            {
                // create a notification text if needed, if it stays empty, the notification will not be created
                string notificationText = NotificationEnemiesRemainingThreshold >= targetRemaining
                    ? targetRemaining + " enemies to kill left"
                    : string.Empty;

                UpdateObjective(string.Empty, GetUpdatedCounterAmount(), notificationText);
            }
        }

        string GetUpdatedCounterAmount()
        {
            return m_KillTotal + " / " + KillsToCompleteObjective;
        }

        void OnDestroy()
        {
            EventManager.RemoveListener<EnemyKillEvent>(OnEnemyKilled);
        }
    }
}