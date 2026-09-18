using Unity.Netcode;
using UnityEngine;

namespace ZombieTown.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleAudio : NetworkBehaviour
    {
        [Header("Engine start / stop")]
        public AudioClip[] EngineStartClips;
        public AudioClip[] EngineStopClips;

        [Header("Engine loops")]
        public AudioClip[] IdleLoopClips;
        public AudioClip[] DriveLoopClips;
        public AudioClip[] ReverseLoopClips;

        [Header("Driver actions")]
        public AudioClip[] AccelerationClips;
        public AudioClip[] BrakeClips;
        public AudioClip[] HandbrakeClips;
        public AudioClip[] HornClips;

        [Header("Vehicle interaction")]
        public AudioClip[] ImpactClips;
        public AudioClip[] AuxiliaryLoopClips;

        [Header("Volume")]
        [Range(0f, 1f)] public float IdleVolume = 0.42f;
        [Range(0f, 1f)] public float DriveVolume = 0.72f;
        [Range(0f, 1f)] public float ReverseVolume = 0.55f;
        [Range(0f, 1f)] public float ActionVolume = 0.72f;
        [Range(0f, 1f)] public float ImpactVolume = 0.82f;
        [Range(0f, 1f)] public float AuxiliaryVolume = 0.50f;

        [Header("Engine response")]
        [Tooltip("Speed in m/s where the driving loop is close to full volume.")]
        [Min(0.5f)] public float SpeedForFullDriveVolume = 12f;
        [Tooltip("How quickly loop volume and pitch react.")]
        [Min(0.5f)] public float ResponseSharpness = 5f;
        [Range(0.5f, 1.5f)] public float IdlePitchAtRest = 0.88f;
        [Range(0.5f, 2f)] public float DrivePitchAtFullSpeed = 1.22f;
        [Range(0f, 1f)] public float ThrottlePitchInfluence = 0.18f;

        [Header("Action thresholds")]
        [Range(0f, 1f)] public float AccelerationThreshold = 0.45f;
        [Min(0.1f)] public float AccelerationSoundCooldown = 1.3f;
        [Min(0.1f)] public float BrakeMinimumSpeed = 1.5f;
        [Min(0.5f)] public float ImpactMinimumSpeed = 2.5f;
        [Min(0f)] public float ImpactSoundCooldown = 0.12f;

        [Header("3D sound")]
        [Min(0.1f)] public float MinDistance = 2f;
        [Min(2f)] public float MaxDistance = 55f;
        [Range(0f, 0.2f)] public float OneShotPitchVariation = 0.035f;
        [Range(0, 256)] public int LoopPriority = 150;
        [Range(0, 256)] public int ActionPriority = 105;

        enum VehicleSoundEvent
        {
            Acceleration,
            Brake,
            Handbrake,
            Horn,
            Impact
        }

        Rigidbody m_Body;
        AudioSource m_IdleSource;
        AudioSource m_DriveSource;
        AudioSource m_ReverseSource;
        AudioSource m_AuxSource;
        AudioSource m_ActionSource;

        bool m_EngineOn;
        float m_Throttle;
        float m_Handbrake;
        float m_AuxiliaryInput;
        float m_NextAccelerationSoundTime = float.NegativeInfinity;
        bool m_WasThrottleHigh;
        bool m_WasHandbrakeHigh;
        float m_NextImpactSoundTime = float.NegativeInfinity;

        Vector3 m_LastPosition;
        bool m_HasLastPosition;
        float m_SmoothedSpeed;
        float m_SmoothedForwardSpeed;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();

            m_IdleSource = CreateSource("Vehicle Audio - Idle", true, LoopPriority);
            m_DriveSource = CreateSource("Vehicle Audio - Drive", true, LoopPriority);
            m_ReverseSource = CreateSource("Vehicle Audio - Reverse", true, LoopPriority);
            m_AuxSource = CreateSource("Vehicle Audio - Auxiliary", true, LoopPriority);
            m_ActionSource = CreateSource("Vehicle Audio - Actions", false, ActionPriority);

            m_LastPosition = transform.position;
            m_HasLastPosition = true;
        }

        void Update()
        {
            UpdateMeasuredSpeed();
            UpdateLoopMix();
        }

        public void SetEngineOn(bool engineOn)
        {
            if (m_EngineOn == engineOn) return;
            m_EngineOn = engineOn;

            if (engineOn)
            {
                PlayLocalRandom(EngineStartClips, ActionVolume);
                StartLoop(m_IdleSource, IdleLoopClips);
                StartLoop(m_DriveSource, DriveLoopClips);
                StartLoop(m_ReverseSource, ReverseLoopClips);
            }
            else
            {
                m_Throttle = 0f;
                m_Handbrake = 0f;
                m_AuxiliaryInput = 0f;
                m_WasThrottleHigh = false;
                m_WasHandbrakeHigh = false;

                StopLoop(m_IdleSource);
                StopLoop(m_DriveSource);
                StopLoop(m_ReverseSource);
                StopLoop(m_AuxSource);
                PlayLocalRandom(EngineStopClips, ActionVolume);
            }
        }

        /// <summary>
        /// Feed driver input. Use sendActionSounds=true only on the local driver's input path.
        /// A second server-physics pass should use false to avoid duplicate rev/brake sounds.
        /// </summary>
        public void SetInput(float throttle, float handbrake, bool sendActionSounds = true)
        {
            float previousThrottle = m_Throttle;

            m_Throttle = Mathf.Clamp(throttle, -1f, 1f);
            m_Handbrake = Mathf.Clamp01(handbrake);

            if (!sendActionSounds || !m_EngineOn)
            {
                m_WasThrottleHigh = Mathf.Abs(m_Throttle) >= AccelerationThreshold;
                m_WasHandbrakeHigh = m_Handbrake >= 0.5f;
                return;
            }

            bool throttleHigh = Mathf.Abs(m_Throttle) >= AccelerationThreshold;
            if (throttleHigh && !m_WasThrottleHigh && Time.time >= m_NextAccelerationSoundTime)
            {
                m_NextAccelerationSoundTime = Time.time + Mathf.Max(0.1f, AccelerationSoundCooldown);
                RequestActionSound(VehicleSoundEvent.Acceleration, AccelerationClips, ActionVolume);
            }

            bool handbrakeHigh = m_Handbrake >= 0.5f;
            if (handbrakeHigh && !m_WasHandbrakeHigh && m_SmoothedSpeed >= BrakeMinimumSpeed)
            {
                if (HasAny(HandbrakeClips))
                    RequestActionSound(VehicleSoundEvent.Handbrake, HandbrakeClips, ActionVolume);
                else
                    RequestActionSound(VehicleSoundEvent.Brake, BrakeClips, ActionVolume);
            }

            bool directionBrake =
                Mathf.Abs(m_SmoothedForwardSpeed) >= BrakeMinimumSpeed &&
                Mathf.Abs(m_Throttle) > 0.25f &&
                Mathf.Sign(m_Throttle) != Mathf.Sign(m_SmoothedForwardSpeed);

            bool wasDirectionBrake =
                Mathf.Abs(m_SmoothedForwardSpeed) >= BrakeMinimumSpeed &&
                Mathf.Abs(previousThrottle) > 0.25f &&
                Mathf.Sign(previousThrottle) != Mathf.Sign(m_SmoothedForwardSpeed);

            if (directionBrake && !wasDirectionBrake && !handbrakeHigh)
                RequestActionSound(VehicleSoundEvent.Brake, BrakeClips, ActionVolume);

            m_WasThrottleHigh = throttleHigh;
            m_WasHandbrakeHigh = handbrakeHigh;
        }

        public void SetAuxiliaryInput(float input)
        {
            m_AuxiliaryInput = Mathf.Clamp(input, -1f, 1f);

            if (m_EngineOn && Mathf.Abs(m_AuxiliaryInput) > 0.05f && !m_AuxSource.isPlaying)
                StartLoop(m_AuxSource, AuxiliaryLoopClips);
        }

        public void PlayHorn()
        {
            RequestActionSound(VehicleSoundEvent.Horn, HornClips, 1f);
        }

        /// <summary>
        /// Plays a speed-scaled impact sound. BusDriver/ForkliftDriver call this for
        /// zombie hits because those collider pairs are intentionally isolated before
        /// Unity resolves a normal Collision callback.
        /// </summary>
        public void PlayImpact(float impactSpeed)
        {
            if (impactSpeed < ImpactMinimumSpeed || Time.time < m_NextImpactSoundTime)
                return;

            if (IsNetworkActive() && !IsServer)
                return;

            m_NextImpactSoundTime = Time.time + Mathf.Max(0f, ImpactSoundCooldown);

            float severity = Mathf.InverseLerp(
                ImpactMinimumSpeed,
                Mathf.Max(ImpactMinimumSpeed + 0.1f, ImpactMinimumSpeed * 4f),
                impactSpeed);

            BroadcastAuthoritativeAction(
                VehicleSoundEvent.Impact,
                ImpactClips,
                Mathf.Lerp(ImpactVolume * 0.45f, ImpactVolume, severity));
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null) return;
            PlayImpact(collision.relativeVelocity.magnitude);
        }

        void UpdateMeasuredSpeed()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 current = transform.position;
            Vector3 delta = m_HasLastPosition ? current - m_LastPosition : Vector3.zero;
            m_LastPosition = current;
            m_HasLastPosition = true;

            float transformSpeed = Mathf.Clamp(delta.magnitude / dt, 0f, 60f);
            float transformForward = Mathf.Clamp(Vector3.Dot(delta / dt, transform.forward), -60f, 60f);

            float physicsSpeed = 0f;
            float physicsForward = 0f;
            if (m_Body != null && !m_Body.isKinematic)
            {
                physicsSpeed = Mathf.Clamp(m_Body.linearVelocity.magnitude, 0f, 60f);
                physicsForward = Mathf.Clamp(Vector3.Dot(m_Body.linearVelocity, transform.forward), -60f, 60f);
            }

            float targetSpeed = Mathf.Max(transformSpeed, physicsSpeed);
            float targetForward = Mathf.Abs(physicsForward) > Mathf.Abs(transformForward)
                ? physicsForward
                : transformForward;

            float blend = 1f - Mathf.Exp(-Mathf.Max(0.5f, ResponseSharpness) * dt);
            m_SmoothedSpeed = Mathf.Lerp(m_SmoothedSpeed, targetSpeed, blend);
            m_SmoothedForwardSpeed = Mathf.Lerp(m_SmoothedForwardSpeed, targetForward, blend);
        }

        void UpdateLoopMix()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.5f, ResponseSharpness) * dt);
            float speed01 = Mathf.Clamp01(m_SmoothedSpeed / Mathf.Max(0.5f, SpeedForFullDriveVolume));
            float throttle01 = Mathf.Abs(m_Throttle);

            float idleTarget = m_EngineOn
                ? IdleVolume * Mathf.Lerp(1f, 0.30f, Mathf.Max(speed01, throttle01))
                : 0f;

            float driveTarget = m_EngineOn
                ? DriveVolume * Mathf.Clamp01(speed01 * 0.82f + throttle01 * 0.42f)
                : 0f;

            bool reversing = m_EngineOn && (m_Throttle < -0.15f || m_SmoothedForwardSpeed < -0.65f);
            float reverseTarget = reversing ? ReverseVolume : 0f;
            float auxTarget = m_EngineOn && Mathf.Abs(m_AuxiliaryInput) > 0.05f
                ? AuxiliaryVolume * Mathf.Lerp(0.55f, 1f, Mathf.Abs(m_AuxiliaryInput))
                : 0f;

            FadeVolume(m_IdleSource, idleTarget, blend);
            FadeVolume(m_DriveSource, driveTarget, blend);
            FadeVolume(m_ReverseSource, reverseTarget, blend);
            FadeVolume(m_AuxSource, auxTarget, blend);

            if (m_IdleSource != null)
            {
                m_IdleSource.pitch = Mathf.Lerp(
                    IdlePitchAtRest,
                    1f + ThrottlePitchInfluence,
                    Mathf.Clamp01(throttle01 * 0.8f + speed01 * 0.2f));
            }

            if (m_DriveSource != null)
            {
                float pitch = Mathf.Lerp(0.82f, DrivePitchAtFullSpeed, speed01) +
                              throttle01 * ThrottlePitchInfluence;
                m_DriveSource.pitch = Mathf.Clamp(pitch, 0.6f, 2f);
            }

            if (m_ReverseSource != null) m_ReverseSource.pitch = 1f;
            if (m_AuxSource != null)
                m_AuxSource.pitch = Mathf.Lerp(0.92f, 1.08f, Mathf.Abs(m_AuxiliaryInput));
        }

        void RequestActionSound(VehicleSoundEvent evt, AudioClip[] clips, float volume)
        {
            int clipIndex = PickRandomIndex(clips);
            if (clipIndex < 0) return;
            float pitch = 1f + Random.Range(-OneShotPitchVariation, OneShotPitchVariation);

            if (!IsNetworkActive())
            {
                PlayActionLocal(evt, clipIndex, pitch, volume);
                return;
            }

            if (IsServer)
            {
                PlayActionRpc((int)evt, clipIndex, pitch, volume);
                return;
            }

            RequestActionRpc((int)evt, clipIndex, pitch, volume);
        }

        void BroadcastAuthoritativeAction(VehicleSoundEvent evt, AudioClip[] clips, float volume)
        {
            int clipIndex = PickRandomIndex(clips);
            if (clipIndex < 0) return;
            float pitch = 1f + Random.Range(-OneShotPitchVariation, OneShotPitchVariation);

            if (!IsNetworkActive())
            {
                PlayActionLocal(evt, clipIndex, pitch, volume);
                return;
            }

            if (IsServer) PlayActionRpc((int)evt, clipIndex, pitch, volume);
        }

        [Rpc(SendTo.Server)]
        void RequestActionRpc(int eventValue, int clipIndex, float pitch, float volume)
        {
            PlayActionRpc(eventValue, clipIndex, pitch, volume);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void PlayActionRpc(int eventValue, int clipIndex, float pitch, float volume)
        {
            PlayActionLocal((VehicleSoundEvent)eventValue, clipIndex, pitch, volume);
        }

        void PlayActionLocal(VehicleSoundEvent evt, int clipIndex, float pitch, float volume)
        {
            AudioClip[] clips = ClipsFor(evt);
            if (m_ActionSource == null || clips == null || clipIndex < 0 || clipIndex >= clips.Length || clips[clipIndex] == null)
                return;

            m_ActionSource.pitch = pitch;
            m_ActionSource.PlayOneShot(clips[clipIndex], Mathf.Clamp01(volume));
        }

        AudioClip[] ClipsFor(VehicleSoundEvent evt)
        {
            switch (evt)
            {
                case VehicleSoundEvent.Acceleration: return AccelerationClips;
                case VehicleSoundEvent.Brake: return BrakeClips;
                case VehicleSoundEvent.Handbrake: return HandbrakeClips;
                case VehicleSoundEvent.Horn: return HornClips;
                case VehicleSoundEvent.Impact: return ImpactClips;
                default: return null;
            }
        }

        void PlayLocalRandom(AudioClip[] clips, float volume)
        {
            int index = PickRandomIndex(clips);
            if (index < 0 || m_ActionSource == null) return;
            m_ActionSource.pitch = 1f + Random.Range(-OneShotPitchVariation, OneShotPitchVariation);
            m_ActionSource.PlayOneShot(clips[index], Mathf.Clamp01(volume));
        }

        void StartLoop(AudioSource source, AudioClip[] clips)
        {
            if (source == null || source.isPlaying) return;
            int index = PickRandomIndex(clips);
            if (index < 0) return;
            source.clip = clips[index];
            source.volume = 0f;
            source.pitch = 1f;
            source.Play();
        }

        static void StopLoop(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.clip = null;
            source.volume = 0f;
        }

        static void FadeVolume(AudioSource source, float target, float blend)
        {
            if (source == null) return;
            source.volume = Mathf.Lerp(source.volume, Mathf.Clamp01(target), blend);
        }

        AudioSource CreateSource(string objectName, bool loop, int priority)
        {
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinDistance;
            source.maxDistance = Mathf.Max(MinDistance + 0.1f, MaxDistance);
            source.dopplerLevel = 0.15f;
            source.priority = priority;
            source.volume = loop ? 0f : 1f;
            return source;
        }

        bool IsNetworkActive()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && IsSpawned;
        }

        static bool HasAny(AudioClip[] clips)
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return true;
            return false;
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
    }
}
