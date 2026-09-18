using Unity.FPS.Game;
using UnityEngine;

namespace ZombieTown.LevelTwo
{
    [DisallowMultipleComponent]
    public sealed class OutpostGuardHeadHitbox : MonoBehaviour
    {
        OutpostGuardAI guard;
        Damageable damageable;

        public void Initialize(OutpostGuardAI targetGuard, float damageMultiplier, float radius)
        {
            guard = targetGuard;
            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere == null) sphere = gameObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.center = Vector3.zero;
            sphere.radius = radius;
            damageable = GetComponent<Damageable>();
            if (damageable == null) damageable = gameObject.AddComponent<Damageable>();
            damageable.DamageMultiplier = damageMultiplier;
        }

        public void RegisterHit(float baseDamage) => guard?.PrepareHeadshot();

        // ProjectileStandard sends impact information to every supported head hitbox.
        public void SetImpact(Ray impact) { }
    }
}
