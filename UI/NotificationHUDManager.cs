using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;

namespace Unity.FPS.UI
{
    public class NotificationHUDManager : MonoBehaviour
    {
        [Tooltip("UI panel containing the layoutGroup for displaying notifications")]
        public RectTransform NotificationPanel;

        [Tooltip("Prefab for the notifications")]
        public GameObject NotificationPrefab;

        [Tooltip("Minimum time (in seconds) a notification stays fully visible before fading out")]
        public float MinVisibleDuration = 5f;
        PlayerWeaponsManager m_PlayerWeaponsManager;
        Jetpack m_Jetpack;

        // Tracks the currently displayed toast per message, so repeated identical notifications refresh instead of stacking
        readonly System.Collections.Generic.Dictionary<string, NotificationToast> m_ActiveNotificationsByText = new();

        void Awake()
        {
            EventManager.AddListener<ObjectiveUpdateEvent>(OnObjectiveUpdateEvent);
            EdgeWarningIndicator edgeIndicator = GetComponent<EdgeWarningIndicator>();
            if (edgeIndicator == null)
                edgeIndicator = gameObject.AddComponent<EdgeWarningIndicator>();
            edgeIndicator.Configure(NotificationPanel);
            TryBindPlayerSystems();
        }

        void Update()
        {
            if (m_PlayerWeaponsManager == null || m_Jetpack == null)
                TryBindPlayerSystems();
        }

        void TryBindPlayerSystems()
        {
            if (m_PlayerWeaponsManager == null)
            {
                m_PlayerWeaponsManager = FindAnyObjectByType<PlayerWeaponsManager>();
                if (m_PlayerWeaponsManager != null)
                    m_PlayerWeaponsManager.OnAddedWeapon += OnPickupWeapon;
            }

            if (m_Jetpack == null)
            {
                m_Jetpack = FindAnyObjectByType<Jetpack>();
                if (m_Jetpack != null)
                    m_Jetpack.OnUnlockJetpack += OnUnlockJetpack;
            }
        }

        void OnObjectiveUpdateEvent(ObjectiveUpdateEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.NotificationText))
                CreateNotification(evt.NotificationText);
        }

        void OnPickupWeapon(WeaponController weaponController, int index)
        {
            if (index != 0)
                CreateNotification("Picked up weapon : " + weaponController.WeaponName);
        }

        void OnUnlockJetpack(bool unlock)
        {
            CreateNotification("Jetpack unlocked");
        }

        public void CreateNotification(string text)
        {
            // if this exact message is already being displayed, just restart its timer instead of spawning a duplicate
            if (m_ActiveNotificationsByText.TryGetValue(text, out NotificationToast activeToast) && activeToast != null)
            {
                activeToast.Initialize(text);
                return;
            }

            GameObject notificationInstance = Instantiate(NotificationPrefab, NotificationPanel);
            notificationInstance.transform.SetSiblingIndex(0);

            NotificationToast toast = notificationInstance.GetComponent<NotificationToast>();
            if (toast)
            {
                toast.VisibleDuration = Mathf.Max(toast.VisibleDuration, MinVisibleDuration);
                toast.Initialize(text);
                m_ActiveNotificationsByText[text] = toast;
            }
        }

        void OnDestroy()
        {
            if (m_PlayerWeaponsManager != null) m_PlayerWeaponsManager.OnAddedWeapon -= OnPickupWeapon;
            if (m_Jetpack != null) m_Jetpack.OnUnlockJetpack -= OnUnlockJetpack;
            EventManager.RemoveListener<ObjectiveUpdateEvent>(OnObjectiveUpdateEvent);
        }
    }
}
