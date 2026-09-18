using System.Collections;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using ZombieTown.Multiplayer;
using ZombieTown.Progression;

namespace ZombieTown.Enemies
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class OrcCombatVariant : NetworkBehaviour
    {
        public enum VariantType : byte { Brute, Club, RockThrower }

        [SerializeField] GameObject clubPrefab;
        [SerializeField] GameObject rockPrefab;
        [SerializeField, Min(1f)] float clubDamage = 24f;
        [SerializeField, Min(1f)] float rockDamage = 16f;

        readonly NetworkVariable<byte> variant = new();
        VariantType configuredVariant;
        Health health;
        Animator animator;
        GameObject clubInstance;
        float nextAbilityAt;
        float nextTargetSearch;
        PlayerCharacterController cachedTarget;
        bool attacking;
        bool dead;

        public VariantType CurrentVariant => IsSpawned ? (VariantType)variant.Value : configuredVariant;

        public void Configure(VariantType value)
        {
            configuredVariant = value;
            if (value == VariantType.RockThrower) rockDamage = Mathf.Max(rockDamage, 30f);
            if (IsSpawned && IsServer) variant.Value = (byte)value;
            ApplyVisual(value);
        }

        void Awake()
        {
            health = GetComponent<Health>();
            animator = GetComponent<Animator>();
        }

        void Start()
        {
            EnemyPointReward.Ensure(gameObject, 30);
            if (health != null)
            {
                health.MaxHealth *= 2f;
                health.CurrentHealth = health.MaxHealth;
                health.OnDie += OnDeath;
            }
            ApplyVisual(CurrentVariant);
        }

        public override void OnNetworkSpawn()
        {
            variant.OnValueChanged += OnVariantChanged;
            if (IsServer) variant.Value = (byte)configuredVariant;
            ApplyVisual((VariantType)variant.Value);
        }

        public override void OnNetworkDespawn()
        {
            variant.OnValueChanged -= OnVariantChanged;
        }

        void OnVariantChanged(byte previous, byte current) => ApplyVisual((VariantType)current);

        void Update()
        {
            if (dead || attacking || Time.time < nextAbilityAt || !HasSimulationAuthority()) return;
            VariantType type = CurrentVariant;
            if (type == VariantType.Brute) return;

            if (!IsValidTarget(cachedTarget) || Time.time >= nextTargetSearch)
            {
                nextTargetSearch = Time.time + .4f;
                cachedTarget = FindClosestPlayer(out _);
            }

            PlayerCharacterController target = cachedTarget;
            if (target == null) return;
            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (type == VariantType.Club && distance <= 4.1f)
                StartCoroutine(ClubSwing(target));
            else if (type == VariantType.RockThrower && distance >= 5f && distance <= 20f)
                StartCoroutine(ThrowRock(target));
        }

        IEnumerator ClubSwing(PlayerCharacterController target)
        {
            attacking = true;
            nextAbilityAt = Time.time + 3.4f;
            Face(target.transform.position);
            TriggerAttack(true);
            yield return new WaitForSeconds(.72f);
            if (!dead && IsValidTarget(target) &&
                Vector3.Distance(transform.position, target.transform.position) <= 4.4f)
            {
                Health targetHealth = target.GetComponent<Health>();
                targetHealth?.TakeDamage(clubDamage, gameObject);
                Vector3 away = target.transform.position - transform.position;
                away.y = 0f;
                Vector3 impulse = away.normalized * 9f + Vector3.up * 4.5f;
                ApplyKnockback(target, impulse);
            }
            yield return new WaitForSeconds(.45f);
            attacking = false;
        }

        IEnumerator ThrowRock(PlayerCharacterController target)
        {
            attacking = true;
            nextAbilityAt = Time.time + 4.2f;
            Face(target.transform.position);
            TriggerAttack(false);
            yield return new WaitForSeconds(.7f);
            if (dead || !IsValidTarget(target))
            {
                attacking = false;
                yield break;
            }

            Vector3 start = transform.position + Vector3.up * 2.2f + transform.forward * .7f;
            Vector3 targetVelocity = Vector3.ProjectOnPlane(target.CharacterVelocity, Vector3.up);
            Vector3 targetPoint = target.transform.position + Vector3.up * .9f + targetVelocity * .28f;
            float travelTime = Mathf.Clamp(Vector3.Distance(start, targetPoint) / 8f, .75f, 1.8f);
            if (IsSpawned) AnimateRockRpc(start, targetPoint, travelTime);
            else StartCoroutine(AnimateProjectile(start, targetPoint, travelTime, rockPrefab, .42f));

            yield return ResolveRockHit(target, start, targetPoint, travelTime);
            attacking = false;
        }

        IEnumerator ResolveRockHit(PlayerCharacterController target, Vector3 start, Vector3 end, float duration)
        {
            float elapsed = 0f;
            while (!dead && IsValidTarget(target) && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 rockPosition = Vector3.Lerp(start, end, t) +
                                       Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.6f);
                if (Vector3.Distance(target.transform.position + Vector3.up * .9f, rockPosition) <= 1.05f)
                {
                    target.GetComponent<Health>()?.TakeDamage(rockDamage, gameObject);
                    yield break;
                }
                yield return null;
            }
        }

        [Rpc(SendTo.Everyone)]
        void AnimateRockRpc(Vector3 start, Vector3 end, float duration)
        {
            StartCoroutine(AnimateProjectile(start, end, duration, rockPrefab, .42f));
        }

        static IEnumerator AnimateProjectile(Vector3 start, Vector3 end, float duration,
            GameObject prefab, float fallbackScale)
        {
            GameObject projectile = prefab != null
                ? Instantiate(prefab, start, Quaternion.identity)
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "Orc Rock Projectile";
            foreach (Collider collider in projectile.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            if (prefab == null) projectile.transform.localScale = Vector3.one * fallbackScale;
            float elapsed = 0f;
            while (projectile != null && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.6f);
                projectile.transform.SetPositionAndRotation(position,
                    Quaternion.LookRotation((end - start).normalized) * Quaternion.Euler(t * 720f, 0f, 0f));
                yield return null;
            }
            if (projectile != null) Destroy(projectile);
        }

        void ApplyKnockback(PlayerCharacterController target, Vector3 impulse)
        {
            NetworkObject playerNetworkObject = target.GetComponent<NetworkObject>();
            if (IsSpawned && playerNetworkObject != null)
                ApplyPlayerKnockbackRpc(impulse,
                    RpcTarget.Single(playerNetworkObject.OwnerClientId, RpcTargetUse.Temp));
            else target.CharacterVelocity = impulse;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ApplyPlayerKnockbackRpc(Vector3 impulse, RpcParams rpcParams = default)
        {
            NetworkObject localPlayer = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
            PlayerCharacterController movement = localPlayer != null
                ? localPlayer.GetComponent<PlayerCharacterController>() : null;
            if (movement != null) movement.CharacterVelocity = impulse;
        }

        void ApplyVisual(VariantType type)
        {
            if (clubInstance != null) clubInstance.SetActive(type == VariantType.Club);
            if (type != VariantType.Club || clubInstance != null || clubPrefab == null || animator == null) return;
            Transform hand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : animator.transform;
            if (hand == null) hand = animator.transform;
            clubInstance = Instantiate(clubPrefab, hand);
            clubInstance.name = "Orc Club";
            clubInstance.transform.SetLocalPositionAndRotation(new Vector3(.04f, .08f, .02f),
                Quaternion.Euler(8f, -78f, -105f));
            FitLongestSide(clubInstance, 2.35f);
            foreach (Collider collider in clubInstance.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        }

        void TriggerAttack(bool special)
        {
            if (animator == null) return;
            int preferred = Animator.StringToHash(special ? "SpecialAttack" : "Attack");
            if (HasParameter(preferred)) animator.SetTrigger(preferred);
        }

        bool HasParameter(int hash)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.nameHash == hash) return true;
            return false;
        }

        void Face(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(direction);
        }

        PlayerCharacterController FindClosestPlayer(out float bestDistance)
        {
            PlayerCharacterController best = null;
            bestDistance = float.PositiveInfinity;
            foreach (PlayerCharacterController player in FindObjectsByType<PlayerCharacterController>())
            {
                if (!IsValidTarget(player)) continue;
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = player;
            }
            return best;
        }

        static bool IsValidTarget(PlayerCharacterController player)
        {
            if (player == null || player.GetComponent<Health>()?.CurrentHealth <= 0f) return false;
            PlayerClassController playerClass = player.GetComponent<PlayerClassController>();
            if (playerClass != null && playerClass.IsDowned.Value) return false;
            return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening ||
                   playerClass != null && playerClass.IsReady.Value;
        }

        bool HasSimulationAuthority()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager == null || !manager.IsListening || manager.IsServer;
        }

        void OnDeath()
        {
            dead = true;
            StopAllCoroutines();
        }

        public override void OnDestroy()
        {
            if (health != null) health.OnDie -= OnDeath;
            base.OnDestroy();
        }

        static void FitLongestSide(GameObject model, float target)
        {
            Renderer[] items = model.GetComponentsInChildren<Renderer>(true);
            if (items.Length == 0) return;
            Bounds bounds = items[0].bounds;
            for (int i = 1; i < items.Length; i++) bounds.Encapsulate(items[i].bounds);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > .001f) model.transform.localScale *= target / longest;
        }
    }
}
