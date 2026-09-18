using System;
using System.Linq;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

internal static class BusZombieAccessValidation
{
    const string ScenePath = "Assets/Scenes/ZombieTownScene.unity";
    const string PendingKey = "ZombieTown.BusAccessTest.Pending";
    const string ExitKey = "ZombieTown.BusAccessTest.Exit";
    const string ResultKey = "ZombieTown.BusAccessTest.Result";

    static ZombieAI testZombie;
    static PlayerCharacterController testPlayer;
    static VehicleZombieAccessZone testZone;
    static Vector3 outsidePoint;
    static float deadline;
    static bool initialized;
    static bool enteredVehicle;

    [InitializeOnLoadMethod]
    static void ResumeAfterAssemblyReload()
    {
        if (SessionState.GetBool(PendingKey, false) ||
            SessionState.GetBool(ExitKey, false))
        {
            HookPlayModeEvents();
            EditorApplication.delayCall += ResumeUpdateIfAlreadyPlaying;
        }
    }

    static void ResumeUpdateIfAlreadyPlaying()
    {
        if (!EditorApplication.isPlaying ||
            !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        deadline = Time.realtimeSinceStartup + 30f;
        EditorApplication.update -= UpdateTest;
        EditorApplication.update += UpdateTest;
    }

    [MenuItem("Tools/Zombie Town/Level 1/Play Mode Test Stationary Bus Entry And Exit")]
    public static void RunStationaryBusEntryTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before starting the bus entry test.");

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(ExitKey, false);
        SessionState.SetString(ResultKey, "RUNNING");
        initialized = false;
        enteredVehicle = false;
        testZombie = null;
        testPlayer = null;
        testZone = null;
        deadline = (float)EditorApplication.timeSinceStartup + 30f;
        HookPlayModeEvents();
        EditorApplication.EnterPlaymode();
    }

    static void HookPlayModeEvents()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode &&
            SessionState.GetBool(PendingKey, false))
        {
            deadline = Time.realtimeSinceStartup + 30f;
            EditorApplication.update -= UpdateTest;
            EditorApplication.update += UpdateTest;
            return;
        }

        if (state == PlayModeStateChange.EnteredEditMode &&
            SessionState.GetBool(ExitKey, false))
        {
            SessionState.SetBool(ExitKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
    }

    static void UpdateTest()
    {
        if (!EditorApplication.isPlaying)
            return;

        try
        {
            if (!initialized)
            {
                PlayerCharacterController player =
                    UnityEngine.Object.FindObjectsByType<PlayerCharacterController>(FindObjectsSortMode.None)
                        .FirstOrDefault(candidate => candidate != null && candidate.enabled);
                if (player == null &&
                    (Unity.Netcode.NetworkManager.Singleton == null ||
                     !Unity.Netcode.NetworkManager.Singleton.IsListening))
                {
                    player = CreateOfflineTestPlayer();
                }
                if (player == null)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                        throw new TimeoutException("No active player was spawned within 30 seconds.");
                    return;
                }
                testPlayer = player;

                testZone = UnityEngine.Object
                    .FindObjectsByType<VehicleZombieAccessZone>(FindObjectsSortMode.None)
                    .FirstOrDefault(candidate =>
                        candidate.name.IndexOf("DoorMain", StringComparison.OrdinalIgnoreCase) >= 0);
                if (testZone == null)
                    throw new InvalidOperationException("ZombieAccessDoorMain is missing from the playable bus.");

                Rigidbody busBody = testZone.VehicleRoot != null
                    ? testZone.VehicleRoot.GetComponent<Rigidbody>()
                    : null;
                if (busBody != null && !busBody.isKinematic)
                {
                    busBody.linearVelocity = Vector3.zero;
                    busBody.angularVelocity = Vector3.zero;
                }

                testZone.GetTraversalCandidates(out outsidePoint, out Vector3 insidePoint);
                MovePlayerTo(player, insidePoint);

                Vector3 spawnGuess = outsidePoint + testZone.GetOutsideDirection() * 4f;
                if (!NavMesh.SamplePosition(spawnGuess, out NavMeshHit spawnHit, 4f, NavMesh.AllAreas))
                    throw new InvalidOperationException("No outside NavMesh position exists near the main bus door.");

                GameObject prefab = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Zombies" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .First(candidate => candidate != null &&
                        candidate.GetComponent<ZombieAI>() != null &&
                        candidate.name.IndexOf("Heavy", StringComparison.OrdinalIgnoreCase) < 0);

                GameObject instance = UnityEngine.Object.Instantiate(
                    prefab,
                    spawnHit.position,
                    Quaternion.LookRotation(-testZone.GetOutsideDirection()));
                instance.name = "Stationary Bus Entry Test Zombie";
                Actor actor = instance.GetComponent<Actor>();
                if (actor != null)
                    actor.enabled = false;

                testZombie = instance.GetComponent<ZombieAI>();
                testZombie.MoveSpeed = 4f;
                testZombie.VehicleAccessTargetTimeout = 8f;
                NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
                if (agent == null || !agent.Warp(spawnHit.position))
                    throw new InvalidOperationException("The test zombie could not be placed on the NavMesh.");

                initialized = true;
                deadline = Time.realtimeSinceStartup + 18f;
                Debug.Log($"[BusZombieAccessTest] Started at {spawnHit.position}; target inside {insidePoint}.");
                return;
            }

            if (testZombie == null || testZone == null)
                throw new InvalidOperationException("The runtime test objects were unexpectedly destroyed.");

            if (!enteredVehicle &&
                testZone.SignedSide(testZombie.transform.position) < -.08f)
            {
                Vector3 exitGuess =
                    outsidePoint + testZone.GetOutsideDirection() * 2f;
                if (!NavMesh.SamplePosition(
                        exitGuess,
                        out NavMeshHit playerExitHit,
                        2f,
                        NavMesh.AllAreas))
                {
                    throw new InvalidOperationException(
                        "No street NavMesh point exists outside the main bus door.");
                }

                enteredVehicle = true;
                MovePlayerTo(testPlayer, playerExitHit.position);
                deadline = Time.realtimeSinceStartup + 18f;
                Debug.Log(
                    $"[BusZombieAccessTest] Entry confirmed; player moved outside to " +
                    $"{playerExitHit.position} for exit validation.");
                return;
            }

            if (enteredVehicle)
            {
                NavMeshAgent agent = testZombie.GetComponent<NavMeshAgent>();
                if (agent != null && agent.enabled &&
                    !testZombie.IsBoardingOrOnVehicle(testZone.VehicleRoot) &&
                    NavMesh.SamplePosition(
                        testZombie.transform.position - Vector3.up * testZombie.GroundOffset,
                        out NavMeshHit streetHit,
                        .25f,
                        agent.areaMask))
                {
                    float heightError = Mathf.Abs(
                        testZombie.transform.position.y -
                        (streetHit.position.y + testZombie.GroundOffset));
                    if (heightError <= .06f)
                    {
                        string message =
                            $"PASS: zombie entered and exited the stationary bus; " +
                            $"street height error {heightError:F3} m; " +
                            $"position {testZombie.transform.position}; " +
                            $"{testZombie.GetNavigationDebugState()}.";
                        SessionState.SetString(ResultKey, message);
                        Debug.Log($"[BusZombieAccessTest] {message}");
                        Finish();
                        return;
                    }
                }
            }

            if (Time.realtimeSinceStartup >= deadline)
            {
                throw new TimeoutException(
                    $"Zombie did not complete the stationary bus " +
                    $"{(enteredVehicle ? "exit" : "entry")} within 18 seconds. " +
                    $"Position {testZombie.transform.position}; {testZombie.GetNavigationDebugState()}.");
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(ResultKey, $"FAIL: {exception}");
            Debug.LogError($"[BusZombieAccessTest] FAIL: {exception}");
            Finish();
        }
    }

    static void MovePlayerTo(PlayerCharacterController player, Vector3 feetPosition)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controller != null)
            controller.enabled = false;
        player.transform.position = feetPosition;
        if (controller != null)
            controller.enabled = controllerWasEnabled;
    }

    static PlayerCharacterController CreateOfflineTestPlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Multiplayer/Player_Network.prefab");
        if (prefab == null)
            throw new InvalidOperationException("Player_Network.prefab is missing.");

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "Stationary Bus Entry Test Player";
        PlayerCharacterController player = instance.GetComponent<PlayerCharacterController>();
        if (player == null)
            throw new InvalidOperationException("Player_Network has no PlayerCharacterController.");

        player.enabled = false;
        Health health = instance.GetComponent<Health>();
        if (health == null)
            throw new InvalidOperationException("Player_Network has no Health component.");
        health.ReviveAndSetHealth(health.MaxHealth);
        return player;
    }

    static void Finish()
    {
        EditorApplication.update -= UpdateTest;
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(ExitKey, true);
        EditorApplication.ExitPlaymode();
    }
}
