#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.FPS.AI;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace ZombieTown.EditorTools
{
    /// <summary>
    /// Validates the network-critical prefab setup that cannot be proven from C# alone.
    /// It also offers a safe editor-side fix for zombie loot prefabs: Unity itself adds
    /// the NetworkObject component so the correct GlobalObjectIdHash is generated, and
    /// the prefab is registered in the available NetworkPrefabsList assets.
    /// </summary>
    public static class NetworkPrefabValidation
    {
        const string MissingHealthLootGuid = "d40ae884302b6684a8b3b6f3d55a71ba";

        [MenuItem("Tools/Zombie Town/Networking/Validate critical prefabs")]
        public static void ValidateCriticalPrefabs()
        {
            int errors = 0;
            int warnings = 0;
            List<string> networkLists = FindNetworkPrefabListPaths();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                ZombieAI zombie = prefab.GetComponent<ZombieAI>();
                if (zombie == null)
                    continue;

                if (prefab.GetComponent<NetworkObject>() == null)
                {
                    Debug.LogError($"[Networking] Zombie prefab '{path}' has no NetworkObject.", prefab);
                    errors++;
                }

                if (prefab.GetComponent<NetworkTransform>() == null)
                {
                    Debug.LogError($"[Networking] Zombie prefab '{path}' has no NetworkTransform.", prefab);
                    errors++;
                }

                if (!IsRegisteredInAnyList(prefab, networkLists))
                {
                    Debug.LogError($"[Networking] Zombie prefab '{path}' is not registered in a NetworkPrefabsList.", prefab);
                    errors++;
                }

                ValidateLootReference(zombie, zombie.AmmoLootPrefab, zombie.AmmoDropChance,
                    "AmmoLootPrefab", path, networkLists, ref errors, ref warnings);
                ValidateLootReference(zombie, zombie.HealthLootPrefab,
                    Mathf.Max(zombie.HealthDropChance, zombie.StrikerHealthDropChance),
                    "HealthLootPrefab", path, networkLists, ref errors, ref warnings);
            }

            string healthPath = AssetDatabase.GUIDToAssetPath(MissingHealthLootGuid);
            if (string.IsNullOrEmpty(healthPath))
            {
                Debug.LogWarning(
                    "[Networking] The HealthLootPrefab GUID referenced by the zombie prefabs is not present in the project. " +
                    "Restore/upload the intended health-loot prefab and assign it to the zombie prefabs.");
                warnings++;
            }

            Debug.Log($"[Networking] Critical prefab validation finished: {errors} error(s), {warnings} warning(s).");
        }

        [MenuItem("Tools/Zombie Town/Networking/Fix referenced zombie loot prefabs")]
        public static void FixReferencedZombieLootPrefabs()
        {
            HashSet<string> lootPaths = new(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                ZombieAI zombie = prefab != null ? prefab.GetComponent<ZombieAI>() : null;
                if (zombie == null)
                    continue;

                AddPrefabPath(zombie.AmmoLootPrefab, lootPaths);
                AddPrefabPath(zombie.HealthLootPrefab, lootPaths);
            }

            if (lootPaths.Count == 0)
            {
                Debug.LogWarning("[Networking] No referenced zombie loot prefabs were found.");
                ValidateCriticalPrefabs();
                return;
            }

            List<string> networkLists = FindNetworkPrefabListPaths();
            int changed = 0;

            foreach (string path in lootPaths)
            {
                if (EnsureNetworkObject(path))
                    changed++;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                foreach (string listPath in networkLists)
                    if (EnsureRegistered(prefab, listPath))
                        changed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Networking] Loot prefab fix finished. {changed} asset change(s) applied.");
            ValidateCriticalPrefabs();
        }

        static void ValidateLootReference(ZombieAI zombie, GameObject lootPrefab, float chance,
            string fieldName, string zombiePath, IReadOnlyList<string> networkLists,
            ref int errors, ref int warnings)
        {
            if (chance <= 0f)
                return;

            if (lootPrefab == null)
            {
                Debug.LogWarning(
                    $"[Networking] '{zombiePath}' has {fieldName} drop chance > 0, but {fieldName} is missing.",
                    zombie);
                warnings++;
                return;
            }

            if (lootPrefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError(
                    $"[Networking] Loot prefab '{AssetDatabase.GetAssetPath(lootPrefab)}' referenced by '{zombiePath}' " +
                    "has no NetworkObject. Networked zombie drops cannot spawn for clients.",
                    lootPrefab);
                errors++;
            }

            if (!IsRegisteredInAnyList(lootPrefab, networkLists))
            {
                Debug.LogError(
                    $"[Networking] Loot prefab '{AssetDatabase.GetAssetPath(lootPrefab)}' is not registered in any " +
                    "NetworkPrefabsList.",
                    lootPrefab);
                errors++;
            }
        }

        static void AddPrefabPath(GameObject prefab, ISet<string> paths)
        {
            if (prefab == null)
                return;
            string path = AssetDatabase.GetAssetPath(prefab);
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        static bool EnsureNetworkObject(string path)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset == null || prefabAsset.GetComponent<NetworkObject>() != null)
                return false;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<NetworkObject>() == null)
                    root.AddComponent<NetworkObject>();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[Networking] Added NetworkObject to '{path}'.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static List<string> FindNetworkPrefabListPaths()
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in AssetDatabase.FindAssets("t:NetworkPrefabsList"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    result.Add(path);
            }

            // Fallback for Unity versions where type-search does not expose NetworkPrefabsList.
            foreach (string name in new[] { "DefaultNetworkPrefabs", "ZombieTownNetworkPrefabs" })
            {
                foreach (string guid in AssetDatabase.FindAssets(name))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset == null)
                        continue;

                    SerializedObject serialized = new(asset);
                    if (serialized.FindProperty("List") != null)
                        result.Add(path);
                }
            }

            if (result.Count == 0)
                Debug.LogWarning("[Networking] No NetworkPrefabsList assets were found.");

            return new List<string>(result);
        }

        static bool IsRegisteredInAnyList(GameObject prefab, IReadOnlyList<string> listPaths)
        {
            foreach (string listPath in listPaths)
                if (IsRegistered(prefab, listPath))
                    return true;
            return false;
        }

        static bool IsRegistered(GameObject prefab, string listPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(listPath);
            if (asset == null)
                return false;

            SerializedObject serialized = new(asset);
            SerializedProperty list = serialized.FindProperty("List");
            if (list == null || !list.isArray)
                return false;

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                SerializedProperty prefabProperty = entry.FindPropertyRelative("Prefab");
                if (prefabProperty != null && prefabProperty.objectReferenceValue == prefab)
                    return true;
            }

            return false;
        }

        static bool EnsureRegistered(GameObject prefab, string listPath)
        {
            if (prefab == null || IsRegistered(prefab, listPath))
                return false;

            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(listPath);
            if (asset == null)
                return false;

            SerializedObject serialized = new(asset);
            SerializedProperty list = serialized.FindProperty("List");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning($"[Networking] '{listPath}' does not expose a serialized prefab List.", asset);
                return false;
            }

            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty entry = list.GetArrayElementAtIndex(index);

            SetInt(entry, "Override", 0);
            SetObject(entry, "Prefab", prefab);
            SetObject(entry, "SourcePrefabToOverride", null);
            SetLong(entry, "SourceHashToOverride", 0);
            SetObject(entry, "OverridingTargetPrefab", null);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            Debug.Log($"[Networking] Registered '{AssetDatabase.GetAssetPath(prefab)}' in '{listPath}'.", asset);
            return true;
        }

        static void SetObject(SerializedProperty parent, string name, UnityEngine.Object value)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property != null)
                property.objectReferenceValue = value;
        }

        static void SetInt(SerializedProperty parent, string name, int value)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property != null)
                property.intValue = value;
        }

        static void SetLong(SerializedProperty parent, string name, long value)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property != null)
                property.longValue = value;
        }
    }
}
#endif
