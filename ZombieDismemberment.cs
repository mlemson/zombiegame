using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.AI
{
    /// <summary>
    /// Cosmetic zombie dismemberment. Supports authored separate meshes and a
    /// bone-weight based fallback for characters that use one SkinnedMeshRenderer.
    /// Gameplay damage and death remain owned by ZombieAI/Health.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZombieDismemberment : MonoBehaviour
    {
        [Header("Optional authored setup")]
        [SerializeField] Transform headBone;
        [SerializeField] Renderer[] separateHeadRenderers;
        [SerializeField] GameObject normalBodyRoot;
        [SerializeField] GameObject headlessBodyRoot;
        [SerializeField] GameObject detachedHeadPrefab;
        [SerializeField] GameObject headshotVfxPrefab;

        [Header("Automatic single-mesh fallback")]
        [SerializeField] bool cutHeadFromSkinnedMesh = true;
        [SerializeField, Range(.05f, .8f)] float minimumHeadBoneWeight = .28f;
        [SerializeField, Min(.05f)] float fallbackHeadRadius = .2f;

        [Header("Detached head physics")]
        [SerializeField, Min(0f)] float impulse = 6f;
        [SerializeField, Min(0f)] float upwardImpulse = 2.75f;
        [SerializeField, Min(0f)] float torque = 7f;
        [SerializeField, Min(.2f)] float lifetime = 4f;

        readonly List<Mesh> runtimeHeadlessMeshes = new();
        bool decapitated;
        bool bloodPlayed;

        public bool IsDecapitated => decapitated;
        public Transform HeadBone => headBone;
        public int RuntimeHeadlessMeshCount => runtimeHeadlessMeshes.Count;
        public GameObject LastDetachedHead { get; private set; }

        public void Configure(Transform newHeadBone, GameObject bloodVfx = null)
        {
            headBone = newHeadBone;
            if (bloodVfx != null) headshotVfxPrefab = bloodVfx;
        }

        public void Decapitate(Vector3 hitDirection, Vector3 hitPoint, GameObject fallbackDetachedHeadPrefab = null)
        {
            if (decapitated) return;
            decapitated = true;

            ResolveHeadBone();
            Vector3 effectPosition = hitPoint != Vector3.zero
                ? hitPoint
                : headBone != null ? headBone.position : transform.position + Vector3.up * 1.6f;
            Quaternion effectRotation = headBone != null ? headBone.rotation : transform.rotation;

            PlayImmediateBlood(effectPosition, effectRotation);

            bool authoredHeadHidden = HideAuthoredHead();
            GameObject detachedSource = detachedHeadPrefab != null ? detachedHeadPrefab : fallbackDetachedHeadPrefab;
            GameObject detachedHead = null;

            // If an authored separate head prefab is assigned, instantiate it.
            if (detachedHeadPrefab != null)
            {
                detachedHead = Instantiate(detachedHeadPrefab, effectPosition, effectRotation);
            }

            if (!authoredHeadHidden && cutHeadFromSkinnedMesh)
            {
                CutSkinnedHead(ref detachedHead);
            }

            // If no dynamic headless mesh or detached head was produced, use the fallback gib prefab
            if (detachedHead == null && detachedSource != null && !authoredHeadHidden && runtimeHeadlessMeshes.Count == 0)
            {
                detachedHead = Instantiate(detachedSource, effectPosition, effectRotation);
            }

            // Last-resort visual fallback. Scaling a bone is less exact than a
            // separated/headless mesh, but works without altering imported art.
            if (!authoredHeadHidden && runtimeHeadlessMeshes.Count == 0 && headBone != null)
                headBone.localScale = Vector3.one * .001f;

            if (detachedHead != null)
            {
                LastDetachedHead = detachedHead;
                Launch(detachedHead, hitDirection);
            }
        }

        public void PlayImmediateBlood(Vector3 hitPoint, Quaternion rotation)
        {
            if (bloodPlayed || headshotVfxPrefab == null) return;
            bloodPlayed = true;
            GameObject effect = Instantiate(headshotVfxPrefab, hitPoint, rotation);
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = particles.main;
                main.startDelay = 0f;
                particles.Play(true);
            }
            ScheduleDestroy(effect);
        }

        bool HideAuthoredHead()
        {
            bool hidden = false;
            if (separateHeadRenderers != null)
            {
                foreach (Renderer renderer in separateHeadRenderers)
                {
                    if (renderer == null) continue;
                    renderer.enabled = false;
                    hidden = true;
                }
            }

            if (normalBodyRoot != null && headlessBodyRoot != null)
            {
                normalBodyRoot.SetActive(false);
                headlessBodyRoot.SetActive(true);
                hidden = true;
            }
            return hidden;
        }

        void CutSkinnedHead(ref GameObject detachedRoot)
        {
            if (headBone == null) return;
            HashSet<Renderer> authoredHeads = separateHeadRenderers == null
                ? new HashSet<Renderer>()
                : new HashSet<Renderer>(separateHeadRenderers);

            foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null || authoredHeads.Contains(skin))
                    continue;
                HashSet<int> headBoneIndices = FindHeadBoneIndices(skin);
                if (headBoneIndices.Count == 0) continue;

                Mesh source = skin.sharedMesh;
                BoneWeight[] weights = source.boneWeights;
                if (weights == null || weights.Length != source.vertexCount) continue;

                List<int>[] kept = new List<int>[source.subMeshCount];
                List<int>[] removed = new List<int>[source.subMeshCount];
                bool removedAny = false;
                for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
                {
                    int[] triangles = source.GetTriangles(subMesh);
                    kept[subMesh] = new List<int>(triangles.Length);
                    removed[subMesh] = new List<int>();
                    for (int index = 0; index + 2 < triangles.Length; index += 3)
                    {
                        bool isHeadTriangle = IsHeadVertex(weights[triangles[index]], headBoneIndices) &&
                                              IsHeadVertex(weights[triangles[index + 1]], headBoneIndices) &&
                                              IsHeadVertex(weights[triangles[index + 2]], headBoneIndices);
                        List<int> destination = isHeadTriangle ? removed[subMesh] : kept[subMesh];
                        destination.Add(triangles[index]);
                        destination.Add(triangles[index + 1]);
                        destination.Add(triangles[index + 2]);
                        removedAny |= isHeadTriangle;
                    }
                }
                if (!removedAny) continue;

                Mesh headless = Instantiate(source);
                headless.name = source.name + " (Runtime Headless)";
                for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
                    headless.SetTriangles(kept[subMesh], subMesh, false);
                headless.RecalculateBounds();
                skin.sharedMesh = headless;
                runtimeHeadlessMeshes.Add(headless);

                if (detachedRoot == null)
                    detachedRoot = CreateDetachedRoot();
                AddBakedHeadPiece(detachedRoot.transform, skin, removed);
            }
        }

        HashSet<int> FindHeadBoneIndices(SkinnedMeshRenderer skin)
        {
            HashSet<int> result = new();
            Transform[] bones = skin.bones;
            for (int index = 0; index < bones.Length; index++)
            {
                Transform bone = bones[index];
                if (bone == headBone || bone != null && bone.IsChildOf(headBone)) result.Add(index);
            }
            return result;
        }

        bool IsHeadVertex(BoneWeight weight, HashSet<int> headBones)
        {
            float total = 0f;
            if (headBones.Contains(weight.boneIndex0)) total += weight.weight0;
            if (headBones.Contains(weight.boneIndex1)) total += weight.weight1;
            if (headBones.Contains(weight.boneIndex2)) total += weight.weight2;
            if (headBones.Contains(weight.boneIndex3)) total += weight.weight3;
            return total >= minimumHeadBoneWeight;
        }

        GameObject CreateDetachedRoot()
        {
            GameObject root = new(name + " Detached Head");
            root.transform.SetPositionAndRotation(headBone.position, headBone.rotation);
            SphereCollider collider = root.AddComponent<SphereCollider>();
            collider.radius = fallbackHeadRadius;
            root.AddComponent<Rigidbody>();
            ScheduleDestroy(root);
            return root;
        }

        void AddBakedHeadPiece(Transform detachedRoot, SkinnedMeshRenderer skin, IReadOnlyList<List<int>> headTriangles)
        {
            Mesh baked = new() { name = skin.sharedMesh.name + " Detached" };
            skin.BakeMesh(baked);
            for (int subMesh = 0; subMesh < baked.subMeshCount; subMesh++)
                baked.SetTriangles(headTriangles[subMesh], subMesh, false);
            baked.RecalculateBounds();

            GameObject piece = new(skin.name + " Head Mesh", typeof(MeshFilter), typeof(MeshRenderer), typeof(RuntimeMeshCleanup));
            piece.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            piece.transform.localScale = skin.transform.lossyScale;
            piece.transform.SetParent(detachedRoot, true);
            piece.GetComponent<MeshFilter>().sharedMesh = baked;
            piece.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            piece.GetComponent<RuntimeMeshCleanup>().Initialize(baked);
        }

        void Launch(GameObject detachedHead, Vector3 hitDirection)
        {
            Rigidbody body = detachedHead.GetComponent<Rigidbody>();
            if (body == null) body = detachedHead.AddComponent<Rigidbody>();
            Vector3 direction = hitDirection.sqrMagnitude > .001f ? hitDirection.normalized : -transform.forward;
            direction.y = Mathf.Clamp(direction.y, -.15f, .25f);
            direction.Normalize();
            body.linearDamping = .18f;
            body.angularDamping = 1.1f;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.AddForce(direction * Mathf.Min(impulse, 4.5f) + Vector3.up * Mathf.Min(upwardImpulse, 1.25f),
                ForceMode.Impulse);
            body.AddTorque(Random.insideUnitSphere * Mathf.Min(torque, 3.5f), ForceMode.Impulse);
            if (Application.isPlaying) Destroy(detachedHead, Mathf.Min(lifetime, 2.8f));
        }

        void ResolveHeadBone()
        {
            if (headBone != null) return;
            Animator animator = GetComponent<Animator>();
            if (animator != null && animator.isHuman)
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone != null) return;

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.Equals("Head", System.StringComparison.OrdinalIgnoreCase)) continue;
                headBone = child;
                break;
            }
        }

        void OnDestroy()
        {
            foreach (Mesh mesh in runtimeHeadlessMeshes)
                if (mesh != null) DestroyRuntimeObject(mesh);
            runtimeHeadlessMeshes.Clear();
        }

        void ScheduleDestroy(Object target)
        {
            if (target != null && Application.isPlaying) Destroy(target, lifetime);
        }

        static void DestroyRuntimeObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        sealed class RuntimeMeshCleanup : MonoBehaviour
        {
            Mesh mesh;
            public void Initialize(Mesh value) => mesh = value;
            void OnDestroy()
            {
                if (mesh != null) DestroyRuntimeObject(mesh);
            }
        }
    }
}
