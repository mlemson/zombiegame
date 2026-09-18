using UnityEngine;

namespace Unity.FPS.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class WeaponAudio : MonoBehaviour
    {
        [Header("Firing")]
        public AudioClip[] ShotClips;
        public AudioClip[] EmptyClips;

        [Header("Reload")]
        public AudioClip[] ReloadStartClips;
        public AudioClip[] ReloadEndClips;

        [Header("Equip")]
        public AudioClip[] EquipClips;
        public AudioClip[] UnequipClips;

        [Header("Melee / special")]
        public AudioClip[] MeleeSwingClips;
        public AudioClip[] MeleeHitClips;
        public AudioClip[] SpecialClips;

        [Header("Volumes")]
        [Range(0f, 1f)] public float ShotVolume = 0.9f;
        [Range(0f, 1f)] public float EmptyVolume = 0.55f;
        [Range(0f, 1f)] public float ReloadVolume = 0.65f;
        [Range(0f, 1f)] public float EquipVolume = 0.55f;
        [Range(0f, 1f)] public float MeleeVolume = 0.72f;
        [Range(0f, 1f)] public float SpecialVolume = 0.75f;

        [Header("3D sound")]
        [Min(0.1f)] public float MinDistance = 1.25f;
        [Min(1f)] public float MaxDistance = 42f;
        [Range(0f, 0.2f)] public float PitchVariation = 0.025f;
        [Range(0, 256)] public int Priority = 115;

        public float UpgradePitchOffset { get; set; }
        public float UpgradeVolumeOffset { get; set; }
        AudioSource m_Source;

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            ConfigureSource(m_Source);
        }

        public void PlayShot() => PlayRandom(ShotClips, ShotVolume);
        public void PlayEmpty() => PlayRandom(EmptyClips, EmptyVolume);
        public void PlayReloadStart() => PlayRandom(ReloadStartClips, ReloadVolume);
        public void PlayReloadEnd() => PlayRandom(ReloadEndClips, ReloadVolume);
        public void PlayEquip() => PlayRandom(EquipClips, EquipVolume);
        public void PlayUnequip() => PlayRandom(UnequipClips, EquipVolume);
        public void PlayMeleeSwing() => PlayRandom(MeleeSwingClips, MeleeVolume);
        public void PlayMeleeHit() => PlayRandom(MeleeHitClips, MeleeVolume);
        public void PlaySpecial() => PlayRandom(SpecialClips, SpecialVolume);

        public void PlayShotIndex(int index, float pitch = 1f)
        {
            PlayIndex(ShotClips, index, pitch, ShotVolume);
        }

        void PlayRandom(AudioClip[] clips, float volume)
        {
            int index = PickRandomIndex(clips);
            if (index < 0) return;
            PlayIndex(clips, index, 1f + Random.Range(-PitchVariation, PitchVariation), volume);
        }

        void PlayIndex(AudioClip[] clips, int index, float pitch, float volume)
        {
            if (m_Source == null || clips == null || index < 0 || index >= clips.Length || clips[index] == null)
                return;
            m_Source.pitch = pitch + UpgradePitchOffset;
            // No upper clamp: an upgraded weapon should audibly play louder, not just cap at the base volume.
            m_Source.PlayOneShot(clips[index], Mathf.Max(0f, volume + UpgradeVolumeOffset));
        }

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
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinDistance;
            source.maxDistance = Mathf.Max(MinDistance + 0.1f, MaxDistance);
            source.dopplerLevel = 0f;
            source.priority = Priority;
            source.volume = 1f;
        }
    }
}
