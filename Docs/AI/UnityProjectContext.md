# Unity Project Context

Last reviewed: 2026-09-09

- New first playable Level 7: `Assets/Scenes/BusEscapeFinaleScene.unity`, separate evacuation road after Dead Orbit. Prep/departure, checkpoint defense, final all-aboard extraction. See `Docs/Playtest/Last-Exit-Prototype.md` and its automated result file; host flow tested, remote client and complete manual driving still pending.
- Bus physics: `ZombieBody` and `BusAttachment` layers distinguish zombies and moving cargo/barricades from suspension ground. BusDriver uses the active zombie registry rather than a 128-collider overlap search. See `Docs/Playtest/Bus-Physics-Fix.md`; preserve these exclusions when changing bus colliders.

- Unity: 6000.5.7f1; URP 17.5; Input System 1.20; uGUI.
- Runtime: FPS Microgame-style MonoBehaviours under `Assets/FPS/Scripts`, extended by zombie gameplay under `Assets/Scripts`.
- Startup: `Assets/FPS/Scenes/IntroMenu.unity`; enabled gameplay scenes are ZombieTown, RadioOutpost, HarborEvacuation, RetreatDefense, HarborViewCity, and DeadOrbit, with Win/Lose scenes shared by the flow.
- Gameplay: `PlayerCharacterController`, `PlayerWeaponsManager`, `WeaponController`, and `Health`; zombies use NavMesh, `ZombieAI`, humanoid Animator, and server migration is in progress.
- Networking: Netcode for GameObjects 2.7 provides the eight-player client/server foundation. Combat hit validation is server-authoritative; the 2026-09-08 standalone defense smoke passed on host and client (19 assertions, including remote turret tracers). This does not cover full campaign transitions.
- Level 3: `HarborEvacuationScene` restores three power switches, activates the ferry beacon, runs a timed defense, and requires all active survivors inside the ferry extraction trigger. Added cover uses carved `NavMeshObstacle` components.
- Level 5: `HarborViewCityScene` is a 1.2-scale coastal city holdout with four sequential defense sites, progressive wall buys, eight player spawns, and a baked NavMesh.
- Level 6: `DeadOrbitScene` is a 1.2-scale Polygon Sci-Fi station with five sequential defense sectors, low-gravity movement, solo-scaled distributed zombie spawns, escalating demons, progressive wall buys, eight player spawns, and a baked NavMesh.
- Generated shops: Level 4, 5, and 6 weapon terminals use `SM_Prop_Crate_02`; normal rebuild menu items update in place, while explicitly named Force Rebuild items delete generated roots.
- Pickups: reusable full-ammo and full-health crate prefabs live under `Assets/ZombieGame/Prefabs/Pickups`.
- Art: POLYGON City Characters, Zombies, Explorers, Pirates, Spy, low-poly pistol/weapon packs, and Kevin Iglesias humanoid animations.
- Tests: `Assets/Editor/CarryableDefenseValidation.cs` runs an isolated NGO host fixture (carry, blockers, turret, upgrades, real window climb); `Assets/ZombieGame/Tests/DefenseNetworkSmoke.cs` runs a standalone host/client smoke fixture. Results live in `Docs/Playtest`.
- Tooling: Unity AI Assistant MCP is connected and supports live scene inspection, safe Editor commands, Console reads, Play Mode control, and visual captures.
- Constraints: preserve imported packages and GUIDs; prefer Editor wiring over manual YAML edits.

Important next validation: start through IntroMenu and run a host plus one remote client through Level 3's switch, beacon, defense, and extraction phases; then produce a fresh Windows player build.

## Current defense implementation (2026-09-08)

Use this map for targeted reads; do not rerun broad setup/rebuild tools to make a small fix.

- Current scope and tuning: `Docs/Playtest/Campaign-Improvement-Advice.md`. Larger campaign placement work has been applied to all six saved scenes; this is no longer only an advice list.
- Controls: F interaction/carry/place, E next weapon, G squad, C voluntary crouch, Shift sprint. Standing stance scale is 1.2 (2.16m); do not reset it to 1 to fix crouching.
- Windows: `BarricadeWindow` stores measured local `openingCenter`/`openingSize`, at most four boards, and sill lift. `ZombieBreachTraversal` claims the opening, breaks boards server-side, lifts to the sill, crosses crouched, and lands. `WindowClimbMotion` also serves ordinary window NavMesh links. `NetworkZombieAnimator.WindowPose` replicates climbing/crouching. Do not fit planks from the wall root's height alone: imported mesh pivots and window heights differ.
- Window authoring: `Assets/Editor/SiegeFinalPolish.cs` measures MeshCollider openings and fits actual plank mesh bounds. Last tuning: 45 HP/board, four initially full boards, 2.4s crossing. Level 4 has twelve actual wall openings plus the legacy standalone repair window.
- Turret: `MountedMachineGun` validates hits on the server, broadcasts muzzle flash/light/tracer/impacts with the shot. Muzzle belongs at the rendered barrel tip. `Assets/ZombieGame/Generated/TurretShot.wav` is a 0.28s mono PCM extract of the source recording; the original Remington file is 11s with the blast at 7.37s and MUST NOT be used whole for repeated shots.
- Weapon upgrades: `RuntimeWeaponUpgrade` applies cached base stats once, excludes arm materials from emission, and uses a white emission map. Tier 1 magazine/reserve 1.75x/2x; tier 2 2.5x/3x. Purchase RPC applies the tier before `WeaponController.RefillAfterUpgrade` to avoid replication ordering issues.
- Difficulty: `GameBalanceDatabase.zombieSpeedIncreasePerLevel` adds 6% per campaign level in `ZombieAI.Start`, once per zombie. Preserve additional type/wave modifiers. Level 4 alive caps 32/48/64/80 solo, +8 per extra player up to 128. Early solo pressure has its own balance asset and Tools menu.
- Gate preparation: Level 4 `gatePreparationSeconds=60` starts on purchase; moving into the next zone preserves remaining time. Existing zombies remain; new spawns pause.
- Performance: active zombie/guard registries and connected-player lists replaced repeated whole-scene scans. CPU evidence identified a 472ms synchronous FMOD load in ZombieAudio.Update: a 120s group recording used DecompressOnLoad with preload/background disabled. Six referenced clips now preload in the background; long voices use CompressedInMemory, and ZombieAudio skips clips until loaded. A later run with the Profiler window visible also exposed cold Animator instantiation and physics/Editor stalls. Final 65s pass with that window closed: 3518 measured frames, mean 17.49ms, max 43.63ms, no >50ms spikes; 32 zombies. See `Level4-Lag-Investigation.md`; this is not a full standalone/8-player guarantee. `SiegeCombatProbe` provides a bounded 65s real-scene host run; `PlaytestFrameTimingCapture` records Editor timings and available CPU samples. A failed test must not restart hosting indefinitely.
- Validation sequencing: refresh scripts, wait for fresh compiled assemblies, apply the targeted asset pass, then capture/verify/test. Do not queue apply/capture/test markers together: they can run in the wrong order or on the previous assembly. Never run a second Unity process on this open project.

## Boardable bus copy (2026-09-09)

- `BarricadableCityBus.prefab` under `Assets/ZombieGame/Prefabs/Vehicles` is a separate drivable copy, scale 1.4, placed beside the original in `ZombieTownScene` at (-14.99, 0, 7.04). The original prefab is preserved. RetreatDefenseScene does not contain this copy; do not load both campaign maps together for playtesting.
- `BusDefenseLayout` spawns 11 standalone network barricade windows and 44 carryable planks. `BusWindowAttachment` follows authored `VehicleZombieAccessZone` anchors. New openings opt into jump/grip/board-breaking/crawl traversal in `ZombieAI.BusBoarding.cs`; ordinary windows keep the existing traversal. `NetworkZombieAnimator` replicates bus poses and hand-IK grip targets. Wood resting positions follow the bus through networked local coordinates.
- `Assets/Editor/BusDefenseValidation.cs` provides an isolated host fixture; results and screenshots are in `Docs/Playtest/BusDefense*`. Read `Docs/Playtest/Bus-Animation-Advice.md` for clip inventory, replacement states, and scope (the small rear window remains fixed). Remote-client campaign driving remains unverified.

# UNITY PROJECT WORKFLOW

Je helpt mij met mijn Unity zombie FPS-project.

Werk token-efficiënt.

REGELS

1. Onderzoek alleen bestanden/objecten die relevant zijn voor mijn huidige vraag.
2. Scan niet automatisch het hele project.
3. Als ik een exacte script-, prefab- of GameObjectnaam geef, begin daar.
4. Lees bestaande code voordat je iets wijzigt.
5. Maak zo klein mogelijke wijzigingen.
6. Herschrijf geen compleet script als enkele regels aanpassen voldoende is.
7. Verander geen bestaande gameplay die niet relevant is voor mijn vraag.
8. Maak geen nieuwe systemen als het bestaande systeem uitgebreid kan worden.
9. Gebruik bestaande components, prefabs en scripts waar mogelijk.
10. Maak vóór grote wijzigingen kort een plan van maximaal 5 regels.

UNITY

- Controleer eerst bestaande componenten en references.
- Vermijd duplicate managers/systems.
- Behoud bestaande Inspector references.
- Verwijder geen serialized fields zonder reden.
- Houd wijzigingen backward compatible met bestaande prefabs.
- Bewerk originele asset-store assets niet destructief.
- Maak aangepaste varianten onder Assets/ZombieGame/.

ASSETS

Ik gebruik veel Synty POLYGON-assets.

Wanneer een bestaande asset moet worden aangepast:
1. gebruik indien mogelijk bestaande modulaire onderdelen;
2. anders maak een prefab variant;
3. voor eenvoudige meshwijzigingen mag ProBuilder worden gebruikt;
4. voor echte geometriewijzigingen mag een Blender/FBX workflow worden voorgesteld;
5. schaal ramen/deuren/props niet mee als alleen het gebouw groter moet worden.

WERKWIJZE

Bij iedere opdracht:

1. Begrijp wat ik daadwerkelijk wil bereiken.
2. Inspecteer alleen de benodigde onderdelen.
3. Vertel heel kort wat je gaat wijzigen.
4. Voer de minimale wijziging uit.
5. Controleer op compile errors / ontbrekende references.
6. Geef daarna alleen:

GEWIJZIGD:
- ...

TEST:
- ...

LET OP:
- ...

Geen lange uitleg tenzij ik daar expliciet om vraag.
