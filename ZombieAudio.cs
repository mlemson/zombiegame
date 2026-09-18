using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace Unity.FPS.AI
{
    /// <summary>
    /// Handles all zombie voice sounds:
    /// - occasional individual growls
    /// - one group/horde sound from a nearby zombie
    /// - attack, successful-hit, hurt and death variations
    ///
    /// Ambient/group sounds are intentionally local to each client.
    /// Important gameplay sounds are triggered by the server and sent to clients/host.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class ZombieAudio : NetworkBehaviour
    {
        [Header("Sound variations")]
        public AudioClip[] IdleClips;
        public AudioClip[] GroupClips;
        public AudioClip[] AttackClips;
        public AudioClip[] PlayerHitClips;
        public AudioClip[] HurtClips;
        public AudioClip[] DeathClips;

        [Header("Individual zombie sounds")]
        [Tooltip("Minimum and maximum delay between chances for a random growl.")]
        public Vector2 IdleInterval = new Vector2(4f, 9f);
        [Range(0f, 1f)]
        public float IdleChance = 0.55f;

        [Header("Group / horde sounds")]
        [Min(2)]
        public int GroupMinimumSize = 4;
        [Min(1f)]
        public float GroupRadius = 7f;
        [Tooltip("How often each zombie checks whether it belongs to a group.")]
        public Vector2 GroupCheckInterval = new Vector2(1.5f, 2.5f);
        [Tooltip("Global cooldown between group sounds on this client.")]
        public Vector2 GroupSoundCooldown = new Vector2(8f, 14f);

        [Header("Volume")]
        [Range(0f, 1f)] public float IdleVolume = 0.45f;
        [Range(0f, 1f)] public float GroupVolume = 0.65f;
        [Range(0f, 1f)] public float AttackVolume = 0.75f;
        [Range(0f, 1f)] public float PlayerHitVolume = 0.70f;
        [Range(0f, 1f)] public float HurtVolume = 0.65f;
        [Range(0f, 1f)] public float DeathVolume = 0.75f;

        [Header("3D sound")]
        [Min(0.1f)] public float MinDistance = 1.5f;
        [Min(1f)] public float MaxDistance = 26f;
        [Range(0f, 0.2f)] public float PitchVariation = 0.06f;
        [Range(0, 256)] public int Priority = 160;

        [Header("Spam protection")]
        [Min(0f)] public float HurtSoundCooldown = 0.22f;
        [Min(0f)] public float PlayerHitSoundCooldown = 0.12f;

        private enum ZombieSoundEvent
        {
            Attack = 0,
            PlayerHit = 1,
            Hurt = 2,
            Death = 3
        }

        private static readonly List<ZombieAudio> ActiveZombies = new List<ZombieAudio>();
        private static float s_NextGroupSoundTime = float.NegativeInfinity;

        private AudioSource m_AmbientSource;
        private AudioSource m_ActionSource;

        private float m_NextIdleTime;
        private float m_NextGroupCheckTime;
        private float m_NextHurtSoundTime = float.NegativeInfinity;
        private float m_NextPlayerHitSoundTime = float.NegativeInfinity;
        private bool m_IsDead;

        private void Awake()
        {
            AudioSource[] sources = GetComponents<AudioSource>();

            if (sources.Length == 0)
            {
                m_AmbientSource = gameObject.AddComponent<AudioSource>();
            }
            else
            {
                m_AmbientSource = sources[0];
            }

            if (sources.Length >= 2)
            {
                m_ActionSource = sources[1];
            }
            else
            {
                m_ActionSource = gameObject.AddComponent<AudioSource>();
            }

            ConfigureSource(m_AmbientSource);
            ConfigureSource(m_ActionSource);
        }

        private void Start()
        {
            if (ShouldPlayLocalAudio())
                foreach (var clips in new[] { IdleClips, GroupClips, AttackClips, PlayerHitClips, HurtClips, DeathClips })
                    if (clips != null)
                        foreach (var clip in clips)
                            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                                clip.LoadAudioData();
            ScheduleNextIdle();
            ScheduleNextGroupCheck();
        }

        private void OnEnable()
        {
            if (!ActiveZombies.Contains(this))
                ActiveZombies.Add(this);
        }

        private void OnDisable()
        {
            ActiveZombies.Remove(this);
        }

        private void Update()
        {
            if (m_IsDead || !ShouldPlayLocalAudio())
                return;

            if (Time.time >= m_NextGroupCheckTime)
            {
                ScheduleNextGroupCheck();
                TryPlayGroupSound();
            }

            if (Time.time < m_NextIdleTime)
                return;

            ScheduleNextIdle();

            // If this zombie is already part of a crowd, keep individual growls quieter
            // by leaving the soundscape to the dedicated group/horde sound.
            int nearbyCount = CountNearbyZombies(out _);
            if (nearbyCount >= GroupMinimumSize)
                return;

            if (Random.value <= IdleChance && !m_AmbientSource.isPlaying)
            {
                PlayLocalRandom(m_AmbientSource, IdleClips, IdleVolume);
            }
        }

        public void PlayAttack()
        {
            PlayNetworkedEvent(ZombieSoundEvent.Attack, AttackClips, AttackVolume);
        }

        public void PlayPlayerHit()
        {
            if (Time.time < m_NextPlayerHitSoundTime)
                return;

            m_NextPlayerHitSoundTime = Time.time + PlayerHitSoundCooldown;
            PlayNetworkedEvent(ZombieSoundEvent.PlayerHit, PlayerHitClips, PlayerHitVolume);
        }

        public void PlayHurt()
        {
            if (Time.time < m_NextHurtSoundTime)
                return;

            m_NextHurtSoundTime = Time.time + HurtSoundCooldown;
            PlayNetworkedEvent(ZombieSoundEvent.Hurt, HurtClips, HurtVolume);
        }

        public void PlayDeath()
        {
            MarkDeadLocal();
            PlayNetworkedEvent(ZombieSoundEvent.Death, DeathClips, DeathVolume, true);
        }

        private void TryPlayGroupSound()
        {
            if (Time.time < s_NextGroupSoundTime ||
                GroupClips == null ||
                GroupClips.Length == 0 ||
                m_AmbientSource.isPlaying)
            {
                return;
            }

            int nearbyCount = CountNearbyZombies(out ZombieAudio leader);

            // Only one zombie in the local cluster may emit the group sound.
            if (nearbyCount < GroupMinimumSize || leader != this)
                return;

            PlayLocalRandom(m_AmbientSource, GroupClips, GroupVolume);

            float min = Mathf.Min(GroupSoundCooldown.x, GroupSoundCooldown.y);
            float max = Mathf.Max(GroupSoundCooldown.x, GroupSoundCooldown.y);
            s_NextGroupSoundTime = Time.time + Random.Range(min, max);
        }

        private int CountNearbyZombies(out ZombieAudio leader)
        {
            leader = null;
            int count = 0;
            float radiusSqr = GroupRadius * GroupRadius;

            for (int i = ActiveZombies.Count - 1; i >= 0; i--)
            {
                ZombieAudio other = ActiveZombies[i];

                if (other == null)
                {
                    ActiveZombies.RemoveAt(i);
                    continue;
                }

                if (!other.isActiveAndEnabled || other.m_IsDead)
                    continue;

                if ((other.transform.position - transform.position).sqrMagnitude > radiusSqr)
                    continue;

                count++;

                if (leader == null || ComesBefore(other, leader))
                    leader = other;
            }

            return count;
        }

        private static bool ComesBefore(ZombieAudio a, ZombieAudio b)
        {
            // In multiplayer, NetworkObjectId gives every client the same leader choice.
            if (a.IsSpawned && b.IsSpawned)
                return a.NetworkObjectId < b.NetworkObjectId;

            // Fallback for editor/single-player objects that are not network-spawned.
            return a.GetEntityId() < b.GetEntityId();
        }

        private void PlayNetworkedEvent(
            ZombieSoundEvent soundEvent,
            AudioClip[] clips,
            float volume,
            bool allowEmpty = false)
        {
            int clipIndex = PickRandomIndex(clips);

            if (clipIndex < 0 && !allowEmpty)
                return;

            float pitch = 1f + Random.Range(-PitchVariation, PitchVariation);

            NetworkManager manager = NetworkManager.Singleton;
            bool networkActive = manager != null && manager.IsListening && IsSpawned;

            if (networkActive)
            {
                // ZombieAI is server-authoritative, so only the server should originate this RPC.
                if (!IsServer)
                    return;

                PlayEventRpc((int)soundEvent, clipIndex, pitch, volume);
                return;
            }

            if (soundEvent == ZombieSoundEvent.Death)
                MarkDeadLocal();

            PlayLocalEvent(soundEvent, clipIndex, pitch, volume);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayEventRpc(int soundEventValue, int clipIndex, float pitch, float volume)
        {
            ZombieSoundEvent soundEvent = (ZombieSoundEvent)soundEventValue;

            if (soundEvent == ZombieSoundEvent.Death)
                MarkDeadLocal();

            PlayLocalEvent(soundEvent, clipIndex, pitch, volume);
        }

        private void PlayLocalEvent(
            ZombieSoundEvent soundEvent,
            int clipIndex,
            float pitch,
            float volume)
        {
            AudioClip[] clips = GetClipsForEvent(soundEvent);

            if (clips == null ||
                clipIndex < 0 ||
                clipIndex >= clips.Length ||
                clips[clipIndex] == null)
            {
                return;
            }

            if (!AudioReady(clips[clipIndex])) return;
            m_ActionSource.pitch = pitch;
            m_ActionSource.PlayOneShot(clips[clipIndex], volume);
        }

        private AudioClip[] GetClipsForEvent(ZombieSoundEvent soundEvent)
        {
            switch (soundEvent)
            {
                case ZombieSoundEvent.Attack:
                    return AttackClips;
                case ZombieSoundEvent.PlayerHit:
                    return PlayerHitClips;
                case ZombieSoundEvent.Hurt:
                    return HurtClips;
                case ZombieSoundEvent.Death:
                    return DeathClips;
                default:
                    return null;
            }
        }

        private void PlayLocalRandom(AudioSource source, AudioClip[] clips, float volume)
        {
            int index = PickRandomIndex(clips);
            if (index < 0 || !AudioReady(clips[index]))
                return;

            source.pitch = 1f + Random.Range(-PitchVariation, PitchVariation);
            source.PlayOneShot(clips[index], volume);
        }

        // These clips import with background loading. Never make a combat frame
        // wait for a cold voice clip; a later ambient/event sound can use it.
        private static bool AudioReady(AudioClip clip)
        {
            if (clip.loadState == AudioDataLoadState.Loaded) return true;
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            return false;
        }

        private static int PickRandomIndex(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
                return -1;

            // Try several times to avoid a null slot in an inspector array.
            for (int i = 0; i < 8; i++)
            {
                int index = Random.Range(0, clips.Length);
                if (clips[index] != null)
                    return index;
            }

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                    return i;
            }

            return -1;
        }

        private void MarkDeadLocal()
        {
            m_IsDead = true;

            if (m_AmbientSource != null)
                m_AmbientSource.Stop();
        }

        private bool ShouldPlayLocalAudio()
        {
            NetworkManager manager = NetworkManager.Singleton;

            // In single player/editor there is no listening NetworkManager.
            // In multiplayer, dedicated server has no reason to render local audio.
            return manager == null || !manager.IsListening || manager.IsClient;
        }

        private void ConfigureSource(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            source.playOnAwake = false;
            source.loop = false;

            source.spatialBlend = 1f; // fully 3D
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinDistance;
            source.maxDistance = Mathf.Max(MinDistance + 0.1f, MaxDistance);
            source.dopplerLevel = 0f;
            source.priority = Priority;
            source.volume = 1f;
        }

        private void ScheduleNextIdle()
        {
            float min = Mathf.Min(IdleInterval.x, IdleInterval.y);
            float max = Mathf.Max(IdleInterval.x, IdleInterval.y);
            m_NextIdleTime = Time.time + Random.Range(min, max);
        }

        private void ScheduleNextGroupCheck()
        {
            float min = Mathf.Min(GroupCheckInterval.x, GroupCheckInterval.y);
            float max = Mathf.Max(GroupCheckInterval.x, GroupCheckInterval.y);
            m_NextGroupCheckTime = Time.time + Random.Range(min, max);
        }
    }
}
