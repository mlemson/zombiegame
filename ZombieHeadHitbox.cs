using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.AI
{
    public class ZombieHeadHitbox : MonoBehaviour
    {
        ZombieAI m_Zombie;
        Damageable m_Damageable;
        Vector3 m_HitDirection;
        Vector3 m_HitPoint;

        public void Initialize(ZombieAI zombie, float damageMultiplier, float radius)
        {
            m_Zombie = zombie;

            SphereCollider headCollider = GetComponent<SphereCollider>();
            if (headCollider == null)
            {
                headCollider = gameObject.AddComponent<SphereCollider>();
            }

            headCollider.isTrigger = true;
            headCollider.radius = radius;
            headCollider.center = Vector3.zero;

            m_Damageable = GetComponent<Damageable>();
            if (m_Damageable == null)
            {
                m_Damageable = gameObject.AddComponent<Damageable>();
            }

            m_Damageable.DamageMultiplier = damageMultiplier;
        }

        public void RegisterHit(float baseDamage)
        {
            if (m_Zombie != null && m_Damageable != null)
            {
                m_Zombie.PrepareHeadshot(baseDamage * m_Damageable.DamageMultiplier, transform,
                    m_HitDirection, m_HitPoint);
            }
        }

        public void SetImpact(Ray impact)
        {
            m_HitDirection = impact.direction;
            m_HitPoint = impact.origin;
        }
    }
}
