using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class PlayerAudio : MonoBehaviour
    {
        [Header("Footsteps")]
        public AudioClip[] WalkFootstepClips;
        public AudioClip[] SprintFootstepClips;
        public AudioClip[] CrouchFootstepClips;

        [Header("Movement")]
        public AudioClip[] JumpClips;
        public AudioClip[] LandClips;
        public AudioClip[] HardLandClips;

        [Header("Damage & death")]
        public AudioClip[] HurtClips;
        public AudioClip[] FallHurtClips;
        public AudioClip[] DeathClips;

        [Header("Volumes")]
        [Range(0f, 1f)] public float WalkVolume = 0.48f;
        [Range(0f, 1f)] public float SprintVolume = 0.62f;
        [Range(0f, 1f)] public float CrouchVolume = 0.32f;
        [Range(0f, 1f)] public float JumpVolume = 0.55f;
        [Range(0f, 1f)] public float LandVolume = 0.55f;
        [Range(0f, 1f)] public float HardLandVolume = 0.78f;
        [Range(0f, 1f)] public float HurtVolume = 0.72f;
        [Range(0f, 1f)] public float DeathVolume = 0.85f;

        [Header("3D sound")]
        [Min(0.1f)] public float MinDistance = 1.25f;
        [Min(1f)] public float MaxDistance = 20f;
        [Range(0f, 0.2f)] public float PitchVariation = 0.045f;
        [Range(0, 256)] public int Priority = 145;

        [Header("Spam protection")]
        [Min(0f)] public float HurtCooldown = 0.18f;

        public enum PlayerSoundEvent
        {
            Walk, Sprint, Crouch, Jump, Land, HardLand, Hurt, FallHurt, Death
        }

        AudioSource m_Source;
        Health m_Health;
        float m_NextHurtTime = float.NegativeInfinity;
        bool m_Dead;
        bool m_Subscribed;

        public event System.Action<PlayerSoundEvent, int, float, float> SoundPlayed;

        void Awake()
        {
            // The player root AudioSource is also used by movement/jetpack code and can
            // be stopped or reconfigured by those systems. Keep vocals and footsteps on
            // their own source so the owning player always hears them reliably.
            Transform audioRoot = transform.Find("Player Voice And Footsteps");
            if (audioRoot == null)
            {
                GameObject audioObject = new("Player Voice And Footsteps");
                audioRoot = audioObject.transform;
                audioRoot.SetParent(transform, false);
            }
            m_Source = audioRoot.GetComponent<AudioSource>() ?? audioRoot.gameObject.AddComponent<AudioSource>();
            ConfigureSource(m_Source);
            m_Health = GetComponent<Health>();
        }

        void Start() => SubscribeHealth();

        void OnEnable()
        {
            if (m_Health == null) m_Health = GetComponent<Health>();
            SubscribeHealth();
        }

        void OnDisable() => UnsubscribeHealth();

        public void PlayFootstep(bool sprinting, bool crouching)
        {
            if (m_Dead) return;

            if (crouching && HasShortFootstep(CrouchFootstepClips))
            {
                PlayOwnerFootstep(PlayerSoundEvent.Crouch, CrouchFootstepClips, CrouchVolume);
                return;
            }

            if (sprinting && HasShortFootstep(SprintFootstepClips))
            {
                PlayOwnerFootstep(PlayerSoundEvent.Sprint, SprintFootstepClips, SprintVolume);
                return;
            }

            if (HasShortFootstep(WalkFootstepClips))
            {
                PlayOwnerFootstep(PlayerSoundEvent.Walk, WalkFootstepClips, WalkVolume);
            }
            else if (HasShortFootstep(SprintFootstepClips))
            {
                PlayOwnerFootstep(PlayerSoundEvent.Sprint, SprintFootstepClips, WalkVolume);
            }
        }

        public void PlayJump()
        {
            if (!m_Dead) PlayOwnerEvent(PlayerSoundEvent.Jump, JumpClips, JumpVolume);
        }

        public void PlayLand(bool hardLanding)
        {
            if (m_Dead) return;

            if (hardLanding && HasAny(HardLandClips))
            {
                PlayOwnerEvent(PlayerSoundEvent.HardLand, HardLandClips, HardLandVolume);
                return;
            }

            PlayOwnerEvent(PlayerSoundEvent.Land, LandClips, LandVolume);
        }

        void OnDamaged(float damage, GameObject source)
        {
            if (damage <= 0f) return;
            PlayDamage(source == null);
        }

        public void PlayDamage(bool fallDamage = false)
        {
            if (m_Dead || Time.time < m_NextHurtTime) return;
            m_NextHurtTime = Time.time + Mathf.Max(0f, HurtCooldown);
            fallDamage &= HasAny(FallHurtClips);
            PlayServerEvent(
                fallDamage ? PlayerSoundEvent.FallHurt : PlayerSoundEvent.Hurt,
                fallDamage ? FallHurtClips : HurtClips,
                HurtVolume);
        }

        public void SetLocalListener(bool local)
        {
            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            ConfigureSource(m_Source);
            if (m_Source != null) m_Source.spatialBlend = local ? 0f : 1f;
        }

        void OnDeath()
        {
            if (m_Dead) return;
            m_Dead = true;
            PlayServerEvent(PlayerSoundEvent.Death, DeathClips, DeathVolume);
        }

        void PlayOwnerEvent(PlayerSoundEvent evt, AudioClip[] clips, float volume)
        {
            int clipIndex = PickRandomIndex(clips);
            if (clipIndex < 0)
                return;

            float pitch = RandomPitch();
            PlayLocal(evt, clipIndex, pitch, volume);
            SoundPlayed?.Invoke(evt, clipIndex, pitch, volume);
        }

        void PlayOwnerFootstep(PlayerSoundEvent evt, AudioClip[] clips, float volume)
        {
            int clipIndex = PickRandomFootstepIndex(clips);
            if (clipIndex < 0) return;

            float pitch = RandomPitch();
            PlayLocal(evt, clipIndex, pitch, volume);
            SoundPlayed?.Invoke(evt, clipIndex, pitch, volume);
        }

        void PlayServerEvent(PlayerSoundEvent evt, AudioClip[] clips, float volume)
        {
            int clipIndex = PickRandomIndex(clips);
            if (clipIndex < 0)
                return;

            float pitch = RandomPitch();
            PlayLocal(evt, clipIndex, pitch, volume);
            SoundPlayed?.Invoke(evt, clipIndex, pitch, volume);
        }

        public void PlayReplicated(PlayerSoundEvent evt, int clipIndex, float pitch, float volume) =>
            PlayLocal(evt, clipIndex, Mathf.Clamp(pitch, .8f, 1.2f), Mathf.Clamp01(volume));

        void PlayLocal(PlayerSoundEvent evt, int clipIndex, float pitch, float volume)
        {
            AudioClip[] clips = ClipsFor(evt);
            if (m_Source == null || clips == null || clipIndex < 0 || clipIndex >= clips.Length || clips[clipIndex] == null)
                return;

            m_Source.pitch = pitch;
            m_Source.PlayOneShot(clips[clipIndex], Mathf.Clamp01(volume));
        }

        AudioClip[] ClipsFor(PlayerSoundEvent evt)
        {
            switch (evt)
            {
                case PlayerSoundEvent.Walk: return WalkFootstepClips;
                case PlayerSoundEvent.Sprint: return SprintFootstepClips;
                case PlayerSoundEvent.Crouch: return CrouchFootstepClips;
                case PlayerSoundEvent.Jump: return JumpClips;
                case PlayerSoundEvent.Land: return LandClips;
                case PlayerSoundEvent.HardLand: return HardLandClips;
                case PlayerSoundEvent.Hurt: return HurtClips;
                case PlayerSoundEvent.FallHurt: return FallHurtClips;
                case PlayerSoundEvent.Death: return DeathClips;
                default: return null;
            }
        }

        void SubscribeHealth()
        {
            if (m_Subscribed || m_Health == null) return;
            m_Health.OnDamaged += OnDamaged;
            m_Health.OnDie += OnDeath;
            m_Subscribed = true;
        }

        void UnsubscribeHealth()
        {
            if (!m_Subscribed || m_Health == null) return;
            m_Health.OnDamaged -= OnDamaged;
            m_Health.OnDie -= OnDeath;
            m_Subscribed = false;
        }

        float RandomPitch() => 1f + Random.Range(-PitchVariation, PitchVariation);

        static bool HasAny(AudioClip[] clips)
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return true;
            return false;
        }

        static bool HasShortFootstep(AudioClip[] clips) => PickFirstFootstepIndex(clips) >= 0;

        static int PickRandomFootstepIndex(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return -1;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int index = Random.Range(0, clips.Length);
                if (IsSingleFootstep(clips[index])) return index;
            }
            return PickFirstFootstepIndex(clips);
        }

        static int PickFirstFootstepIndex(AudioClip[] clips)
        {
            if (clips == null) return -1;
            for (int i = 0; i < clips.Length; i++)
                if (IsSingleFootstep(clips[i])) return i;
            return -1;
        }

        // Footstep events are distance-driven one-shots. Longer recordings contain
        // several footsteps and overlap every time the distance threshold is crossed.
        static bool IsSingleFootstep(AudioClip clip) => clip != null && clip.length <= 1.1f;

        static int PickRandomIndex(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return -1;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int index = Random.Range(0, clips.Length);
                if (clips[index] != null) return index;
            }
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return i;
            return -1;
        }

        void ConfigureSource(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.playOnAwake = false;
            source.loop = false;
            // Ownership switches this to fully 2D for the local player and fully 3D
            // for remote players. Default to 3D until ownership has been established.
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = Mathf.Max(1f, MinDistance);
            source.maxDistance = Mathf.Max(MinDistance + 0.1f, MaxDistance);
            source.dopplerLevel = 0f;
            source.priority = Priority;
            source.volume = 1f;
        }
    }
}
