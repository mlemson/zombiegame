using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class FeedbackFlashHUD : MonoBehaviour
    {
        [Header("References")] [Tooltip("Image component of the flash")]
        public Image FlashImage;

        [Tooltip("CanvasGroup to fade the damage flash, used when recieving damage end healing")]
        public CanvasGroup FlashCanvasGroup;

        [Tooltip("CanvasGroup to fade the critical health vignette")]
        public CanvasGroup VignetteCanvasGroup;

        [Header("Damage")] [Tooltip("Color of the damage flash")]
        public Color DamageFlashColor;

        [Tooltip("Duration of the damage flash")]
        public float DamageFlashDuration;

        [Tooltip("Max alpha of the damage flash")]
        public float DamageFlashMaxAlpha = 1f;

        [Header("Critical health")] [Tooltip("Max alpha of the critical vignette")]
        public float CriticaHealthVignetteMaxAlpha = .8f;

        [Tooltip("Frequency at which the vignette will pulse when at critical health")]
        public float PulsatingVignetteFrequency = 4f;

        [Tooltip("Looped heartbeat that becomes louder and faster as health approaches zero")]
        public AudioClip CriticalHeartbeatSfx;

        [Range(0f, 1f)] public float CriticalHeartbeatMaxVolume = .72f;

        [Header("Heal")] [Tooltip("Color of the heal flash")]
        public Color HealFlashColor;

        [Tooltip("Duration of the heal flash")]
        public float HealFlashDuration;

        [Tooltip("Max alpha of the heal flash")]
        public float HealFlashMaxAlpha = 1f;

        bool m_FlashActive;
        float m_LastTimeFlashStarted = Mathf.NegativeInfinity;
        Health m_PlayerHealth;
        GameFlowManager m_GameFlowManager;
        bool m_Initialized;
        AudioSource m_HeartbeatSource;

        void OnEnable()
        {
            TryInitialize();
        }

        void Start()
        {
            TryInitialize();
        }

        void OnDisable()
        {
            UnsubscribeFromPlayer();
        }

        void OnDestroy()
        {
            UnsubscribeFromPlayer();
        }

        void Update()
        {
            // A player/GameFlowManager can disappear and be recreated during a scene switch.
            // Drop stale references and reconnect to the current scene objects.
            if (!m_Initialized || m_PlayerHealth == null || m_GameFlowManager == null)
            {
                UnsubscribeFromPlayer();
                TryInitialize();

                if (!m_Initialized)
                    return;
            }

            // Serialized HUD references can already have been destroyed while an old
            // callback is still finishing during scene teardown. Never touch them then.
            if (FlashCanvasGroup == null || VignetteCanvasGroup == null)
                return;

            if (m_PlayerHealth.IsCritical())
            {
                VignetteCanvasGroup.gameObject.SetActive(true);
                float criticalSeverity = Mathf.Clamp01(1f - (m_PlayerHealth.CurrentHealth /
                    Mathf.Max(1f, m_PlayerHealth.MaxHealth) /
                    Mathf.Max(.01f, m_PlayerHealth.CriticalHealthRatio)));
                float vignetteAlpha = Mathf.Lerp(.2f, CriticaHealthVignetteMaxAlpha,
                    criticalSeverity);

                UpdateHeartbeat(criticalSeverity);

                if (m_GameFlowManager.GameIsEnding)
                    VignetteCanvasGroup.alpha = vignetteAlpha;
                else
                    VignetteCanvasGroup.alpha =
                        ((Mathf.Sin(Time.time * PulsatingVignetteFrequency) / 2) + 0.5f) * vignetteAlpha;
            }
            else
            {
                VignetteCanvasGroup.gameObject.SetActive(false);
                StopHeartbeat();
            }

            if (m_FlashActive)
            {
                float normalizedTimeSinceDamage = (Time.time - m_LastTimeFlashStarted) / DamageFlashDuration;

                if (normalizedTimeSinceDamage < 1f)
                {
                    float flashAmount = DamageFlashMaxAlpha * (1f - normalizedTimeSinceDamage);
                    FlashCanvasGroup.alpha = flashAmount;
                }
                else
                {
                    FlashCanvasGroup.gameObject.SetActive(false);
                    m_FlashActive = false;
                }
            }
        }

        bool ResetFlash()
        {
            if (!isActiveAndEnabled || FlashCanvasGroup == null)
                return false;

            m_LastTimeFlashStarted = Time.time;
            m_FlashActive = true;
            FlashCanvasGroup.alpha = 0f;
            FlashCanvasGroup.gameObject.SetActive(true);
            return true;
        }

        void TryInitialize()
        {
            if (m_Initialized)
                return;

            if (FlashImage == null || FlashCanvasGroup == null || VignetteCanvasGroup == null)
                return;

            PlayerCharacterController playerCharacterController = FindAnyObjectByType<PlayerCharacterController>();
            if (playerCharacterController == null)
                return;

            Health playerHealth = playerCharacterController.GetComponent<Health>();
            if (playerHealth == null)
                return;

            GameFlowManager gameFlowManager = FindAnyObjectByType<GameFlowManager>();
            if (gameFlowManager == null)
                return;

            m_PlayerHealth = playerHealth;
            m_GameFlowManager = gameFlowManager;

            m_PlayerHealth.OnDamaged += OnTakeDamage;
            m_PlayerHealth.OnHealed += OnHealed;
            m_Initialized = true;
        }

        void UnsubscribeFromPlayer()
        {
            if (m_PlayerHealth != null)
            {
                m_PlayerHealth.OnDamaged -= OnTakeDamage;
                m_PlayerHealth.OnHealed -= OnHealed;
            }

            m_PlayerHealth = null;
            m_GameFlowManager = null;
            m_Initialized = false;
            m_FlashActive = false;
            StopHeartbeat();
        }

        void UpdateHeartbeat(float severity)
        {
            if (CriticalHeartbeatSfx == null) return;
            if (m_HeartbeatSource == null)
            {
                m_HeartbeatSource = gameObject.AddComponent<AudioSource>();
                m_HeartbeatSource.playOnAwake = false;
                m_HeartbeatSource.loop = true;
                m_HeartbeatSource.spatialBlend = 0f;
                m_HeartbeatSource.clip = CriticalHeartbeatSfx;
            }

            m_HeartbeatSource.volume = Mathf.Lerp(.16f, CriticalHeartbeatMaxVolume,
                Mathf.Clamp01(severity));
            m_HeartbeatSource.pitch = Mathf.Lerp(.92f, 1.22f, Mathf.Clamp01(severity));
            if (!m_HeartbeatSource.isPlaying) m_HeartbeatSource.Play();
        }

        void StopHeartbeat()
        {
            if (m_HeartbeatSource != null && m_HeartbeatSource.isPlaying)
                m_HeartbeatSource.Stop();
        }

        void OnTakeDamage(float dmg, GameObject damageSource)
        {
            if (!ResetFlash() || FlashImage == null)
                return;

            FlashImage.color = DamageFlashColor;
        }

        void OnHealed(float amount)
        {
            if (!ResetFlash() || FlashImage == null)
                return;

            FlashImage.color = HealFlashColor;
        }
    }
}
