using Unity.FPS.Game;
using UnityEngine;

namespace ZombieTown.Weapons
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WeaponController))]
    public sealed class ChainsawWeapon : MonoBehaviour
    {
        [SerializeField, Min(1f)] float secondsToOverheat = 5.5f;
        [SerializeField, Min(1f)] float secondsToCool = 4.25f;
        [SerializeField, Min(.5f)] float secondsToCoolBeforeOverheat = 1.65f;
        [SerializeField, Range(0f, .8f)] float restartHeat = .28f;
        [SerializeField, Min(.05f)] float damageInterval = .11f;
        [SerializeField, Min(1f)] float damagePerTick = 8f;
        [SerializeField, Range(1f, 3f)] float damageRange = 2.15f;
        [SerializeField] AudioClip engineLoop;
        [SerializeField, Range(0f, 1f)] float engineVolume = .15f;
        [SerializeField, Range(.5f, 3f)] float enginePitch = 2.2f;
        [SerializeField, Range(.5f, 3f)] float hotEnginePitch = 2.45f;
        [Header("First Person Pose")]
        [SerializeField] Vector3 holdingLocalPosition = new(.2f, .08f, .52f);
        [SerializeField] Vector3 holdingLocalEuler = new(8f, -8f, -10f);
        [SerializeField] Vector3 cuttingLocalPosition = new(.12f, .28f, .88f);
        [SerializeField] Vector3 cuttingLocalEuler = new(8f, -8f, -10f);
        [SerializeField, Min(.1f)] float modelScale = 1.2f;
        [SerializeField, Min(1f)] float poseSpeed = 9f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        WeaponController weapon;
        Renderer[] renderers;
        Color[] coldColors;
        MaterialPropertyBlock propertyBlock;
        AudioSource engineSource;
        Transform chainsawModel;
        Vector3 restingLocalPosition;
        Quaternion restingLocalRotation;
        Quaternion cuttingLocalRotation;
        AudioClip generatedEngineLoop;
        float heat;
        float cuttingPose;
        float nextDamageAt;
        float nextNoiseAt;
        int drivenFrame = -1;
        bool overheated;
        bool powered;

        public float CurrentHeat => heat;
        public bool IsOverheated => overheated;
        public bool IsPowered => powered;
        public AudioClip EngineLoopClip => engineLoop;
        public float EngineVolume => engineVolume;
        public float EnginePitch => enginePitch;

        void Awake()
        {
            weapon = GetComponent<WeaponController>();
            if (weapon.WeaponRoot != null)
            {
                Transform legacySword = weapon.WeaponRoot.transform.Find("MeleeSword_Model");
                if (legacySword != null) legacySword.gameObject.SetActive(false);
            }
            renderers = weapon.WeaponRoot != null
                ? weapon.WeaponRoot.GetComponentsInChildren<Renderer>(true)
                : GetComponentsInChildren<Renderer>(true);
            coldColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].sharedMaterial;
                coldColors[i] = material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : material != null && material.HasProperty(ColorId)
                        ? material.GetColor(ColorId) : Color.white;
            }
            propertyBlock = new MaterialPropertyBlock();

            chainsawModel = weapon.WeaponRoot != null
                ? weapon.WeaponRoot.transform.Find("Chainsaw Model") : null;
            if (chainsawModel != null)
            {
                // Keep the authored display prefab independent from the actual held
                // pose. A copied shop weapon must always face down the view, even if
                // its source model was rotated for presentation on a wall or table.
                chainsawModel.localPosition = holdingLocalPosition;
                chainsawModel.localRotation = Quaternion.Euler(holdingLocalEuler);
                chainsawModel.localScale = Vector3.one * modelScale;
                restingLocalPosition = holdingLocalPosition;
                restingLocalRotation = chainsawModel.localRotation;
                cuttingLocalRotation = Quaternion.Euler(cuttingLocalEuler);
            }

            engineSource = gameObject.AddComponent<AudioSource>();
            engineSource.playOnAwake = false;
            engineSource.loop = true;
            generatedEngineLoop = engineLoop == null ? CreateEngineLoop() : null;
            engineSource.clip = engineLoop != null ? engineLoop : generatedEngineLoop;
            engineSource.volume = engineVolume;
            engineSource.spatialBlend = 0f;
            ApplyHeatVisual();
        }

        public bool HandleInput(bool inputHeld, IContinuousMeleeAttackHandler attackHandler)
        {
            drivenFrame = Time.frameCount;
            powered = inputHeld && !overheated;
            if (!powered)
            {
                CoolDown();
                UpdateCuttingPose();
                return false;
            }

            heat = Mathf.MoveTowards(heat, 1f, Time.deltaTime / secondsToOverheat);
            if (heat >= .999f)
            {
                heat = 1f;
                overheated = true;
                powered = false;
                UpdateEngineAudio();
                ApplyHeatVisual();
                UpdateCuttingPose();
                return false;
            }

            bool dealtTick = false;
            if (attackHandler != null && Time.time >= nextDamageAt)
            {
                nextDamageAt = Time.time + damageInterval;
                dealtTick = attackHandler.TryContinuousMeleeAttack(damagePerTick, damageRange);
            }
            if (weapon.Owner != null && Time.time >= nextNoiseAt)
            {
                nextNoiseAt = Time.time + .5f;
                weapon.Owner.GetComponent<IWeaponNoiseReporter>()?.ReportWeaponNoise(false, transform.position);
            }
            UpdateEngineAudio();
            ApplyHeatVisual();
            UpdateCuttingPose();
            return dealtTick;
        }

        void LateUpdate()
        {
            if (drivenFrame == Time.frameCount) return;
            powered = false;
            CoolDown();
            UpdateCuttingPose();
        }

        void CoolDown()
        {
            float coolingSeconds = overheated ? secondsToCool : secondsToCoolBeforeOverheat;
            heat = Mathf.MoveTowards(heat, 0f, Time.deltaTime / Mathf.Max(.5f, coolingSeconds));
            if (overheated && heat <= restartHeat) overheated = false;
            UpdateEngineAudio();
            ApplyHeatVisual();
        }

        void UpdateEngineAudio()
        {
            if (engineSource == null) return;
            if (powered)
            {
                engineSource.pitch = Mathf.Lerp(enginePitch, hotEnginePitch, heat);
                if (!engineSource.isPlaying) engineSource.Play();
            }
            else if (engineSource.isPlaying) engineSource.Stop();
        }

        void ApplyHeatVisual()
        {
            float warmth = Mathf.SmoothStep(0f, 1f, heat);
            float tintStrength = warmth * .42f;
            Color hotTint = new(1f, .12f, .035f, 1f);
            Color emission = new Color(1f, .035f, .01f, 1f) * (warmth * warmth * .65f);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                Color tint = Color.Lerp(coldColors[i], hotTint, tintStrength);
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, tint);
                propertyBlock.SetColor(ColorId, tint);
                propertyBlock.SetColor(EmissionColorId, emission);
                renderer.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }

        void UpdateCuttingPose()
        {
            if (chainsawModel == null) return;
            cuttingPose = Mathf.MoveTowards(cuttingPose, powered ? 1f : 0f, poseSpeed * Time.deltaTime);
            float easedPose = Mathf.SmoothStep(0f, 1f, cuttingPose);
            chainsawModel.localPosition = Vector3.Lerp(restingLocalPosition, cuttingLocalPosition, easedPose);
            chainsawModel.localRotation = Quaternion.Slerp(restingLocalRotation, cuttingLocalRotation, easedPose);
        }

        static AudioClip CreateEngineLoop()
        {
            const int sampleRate = 22050;
            const int sampleCount = sampleRate / 2;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float throttle = .86f + .14f * Mathf.Sin(2f * Mathf.PI * 8f * time);
                float motor = .48f * Mathf.Sin(2f * Mathf.PI * 92f * time) +
                              .24f * Mathf.Sin(2f * Mathf.PI * 184f * time) +
                              .14f * Mathf.Sin(2f * Mathf.PI * 368f * time) +
                              .08f * Mathf.Sin(2f * Mathf.PI * 736f * time);
                samples[i] = motor * throttle * .52f;
            }

            AudioClip clip = AudioClip.Create("Generated Chainsaw Engine Loop", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void OnDisable()
        {
            powered = false;
            if (engineSource != null) engineSource.Stop();
            heat = 0f;
            overheated = false;
            cuttingPose = 0f;
            if (chainsawModel != null)
            {
                chainsawModel.localPosition = restingLocalPosition;
                chainsawModel.localRotation = restingLocalRotation;
            }
            ApplyHeatVisual();
        }

        void OnDestroy()
        {
            if (generatedEngineLoop != null) Destroy(generatedEngineLoop);
        }
    }
}
