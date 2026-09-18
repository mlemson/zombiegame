# Zombie Town multiplayer setup

## Implemented foundation

- Four replicated classes: Guardian, Gunslinger, Striker, Medic.
- Guardian armor absorbs damage and recharges after a configurable delay.
- Guardian armor is shown as a cyan shield/fuel bar in the existing bottom-left HUD stack.
- Gunslinger accepts a stronger starting `WeaponController` (use the revolver model under PolygonExplorers).
- The normal pistol and Heavy Revolver use different impact-style shot sounds from the existing project audio.
- Striker starts with a real, switchable Shortsword weapon slot. Left-click or `Q` attacks while it is equipped; successful hits use the normal hitmarker. The sword has no ammo or reload UI, while any collected firearm still shoots and reloads normally.
- Medic has a server-authoritative teammate heal ability on `H`.
- Class choice is replicated and late joiners receive it through `NetworkVariable` state.
- Gameplay input and zombie spawning remain paused until every connected player has confirmed a class.
- Player gravity, collision movement and weapon initialization stay disabled during selection. The server assigns each confirmed player a safe NavMesh spawn before releasing gameplay.
- Existing scene zombies also wait behind the same ready gate; they cannot walk or attack through the class menu.
- Both startup screens use scalable Canvas UI; class cards render a live 3D preview of their assigned POLYGON character.
- Character previews use a continuously looping humanoid idle animation and face the preview camera. Third-person avatars use humanoid hand IK; local ranged weapons retain the original tutorial FPS sockets.
- Eight-player connection approval and `UNITY_SERVER` automatic dedicated-server startup.
- Compact locomotion animator replication; transforms should use NGO `NetworkTransform` on the player prefab.

## Current automated setup

The Editor tool `Tools > Zombie Town > Apply Multiplayer Setup` now performs the previous wiring steps. It creates and configures:

- `Assets/Prefabs/Multiplayer/Player_Network.prefab`;
- the Intro scene `NetworkManager`, `UnityTransport`, approval callback and local launcher;
- the four assigned POLYGON characters and class weapons;
- owner-only camera, audio, input and FPS weapon behavior;
- an owner-authoritative player transform and replicated locomotion values;
- the network-prefab list containing the player and five zombie prefabs;
- server-only zombie spawning, navigation, damage, loot and despawning;
- `PlayerNetworkLocomotion.controller` using the imported humanoid animations.

The tool can be safely re-run and also runs once after a fresh Editor domain reload.

## Local test

1. Open `Assets/FPS/Scenes/IntroMenu.unity` and press Play.
2. Click **CREATE GAME**, then **HOST LOCALLY**.
3. Choose one of the four visible character cards after the gameplay scene loads. You cannot move or shoot until you confirm.
4. Start `Builds/WindowsLocal/ZombieTownClient.exe`.
5. In that client click **JOIN GAME**, keep `127.0.0.1` and port `7777`, then click **JOIN LAN**.
6. Choose a different class. Verify both characters move and animate; use `Q` for Striker melee and `H` for Medic healing.

## Class and weapon gameplay checks

- Guardian: the cyan `ARMOR` counter is a preconfigured child of `GameHUD/BottomLeftcorner`. It binds itself to the owning network player after spawn and starts at `30 / 30`.
- Striker: a zombie killed by Striker melee has a 30% health-drop chance instead of the normal 8%. The server derives this from the final damage source.
- Disc launcher: its physical magazine contains two charge units. Holding fire consumes the first to start charging and up to the second while building the charged area-damage projectile; release fire to shoot.
- Win/Lose scenes: `PLAY AGAIN` safely shuts down the old network session. A host immediately creates a fresh local session; clients return to the main menu instead of reloading underneath a live `NetworkManager`.

## Menu, vehicles and map arsenal

- The startup menu first shows only `CREATE GAME` and `JOIN GAME`. Create then offers local or Relay hosting; Join exposes a Relay code plus compact LAN address/port fields.
- In Unity, use `Tools > Zombie Town > Open Character Selection Settings`. In the opened `Player_Network` prefab, edit `PlayerClassController > Archetypes` for models, stats and loadouts, and `ClassSelectionMenu > Menu Presentation` for the title, subtitle and preview lighting.
- A complete four-entry archetype array is no longer overwritten when the setup tool runs, so manual Inspector tuning is preserved.
- The playable forklift uses URP/Lit materials, a 1.2 root scale and an automatically selected clear NavMesh parking location. Press `E` to enter/exit, use WASD to drive, Space for the handbrake, and PageUp/PageDown for the forks. Its multiplayer physics is currently host-authoritative; remote-client driving is a later networking step.
- Map loot includes Heavy Revolver, Compact SMG, Pump Shotgun, Coach Gun, Marksman Rifle, Shortsword and Disc Launcher. Their tradeoffs are respectively damage/reload, fire-rate/accuracy, steady close range, burst/reload, precision/fire-rate, unlimited short-range attacks and area damage/charge time.
- Generic ammo applies to the active ranged weapon and converts a fixed damage budget to weapon-specific rounds. Strong single shots and multi-pellet weapons therefore receive less ammo than a pistol or SMG.
- Melee mode is active only while a melee weapon slot is selected. Switching to a gun restores normal fire/reload/ammo behavior; switching back to the sword restores LMB/Q melee. The sword pickup can be collected and used by every class.
- Regular sword attacks use a short forward proximity arc in addition to the main cast, so zombies pressed against the player are not skipped. The charged 360-degree swing keeps its separate half-damage balance.
- Police and Riot Cop zombies receive 50% body damage and one third of normal headshot damage. Biker zombies move at 1.75x the standard speed.

## Combat animation hooks

The code already supplies procedural sword-combo and reload motion, so combat remains readable before custom clips are assigned. Custom humanoid clips can replace or enhance it through these parameters on `PlayerNetworkLocomotion.controller`:

- `Melee` (Trigger): enter the melee attack state;
- `MeleeAttack` (Int): `0` diagonal slash, `1` reverse slash, `2` overhead chop;
- `Reload` (Trigger): enter a character reload state;
- `Speed`, `Grounded`, and `Crouched`: existing locomotion parameters.

For zombie hit reactions, add a Trigger parameter named `Hit` (preferred) or `OnDamaged` to each zombie Animator Controller and transition it to the desired reaction clip. Gameplay damage remains server-authoritative; the trigger is replicated cosmetically by `NetworkZombieAnimator`.

First-person weapon prefabs may also receive an Animator with a `Reload` Trigger assigned to `WeaponController.WeaponAnimator`. If it is absent, `WeaponController` uses its built-in procedural reload motion and still completes the magazine transfer at `ReloadDuration`.

## Weapon grip fine-tuning

The POLYGON pistol, revolver and SMG are authored with their barrels along local `+Z`. Their generated FPS model transforms therefore use rotation `(0, 0, 0)` and sit directly on the original tutorial `GunRoot` grip.

- First-person pistol: open `Assets/FPS/Prefabs/Weapons/Weapon_Blaster.prefab`, expand `GunRoot`, then adjust `StarterPistol_Model`.
- First-person revolver: open `Assets/FPS/Prefabs/Weapons/Weapon_UpgradedPistol.prefab`, expand `GunRoot`, then adjust `UpgradedPistol_Model`.
- First-person SMG: open `Assets/FPS/Prefabs/Weapons/Weapon_SMG.prefab`, expand `GunRoot`, then adjust `SMG_Model`.
- Third-person hand props: open `Assets/Prefabs/Multiplayer/Player_Network.prefab`, open `PlayerClassController > Archetypes`, then adjust `Hand Prop Position`, `Hand Prop Rotation`, and `Hand Prop Scale` for the class.

Use local position for grip placement and local rotation for the barrel direction. Small adjustments of roughly `0.01` to `0.03` units are usually enough. Do not run `Tools > Zombie Town > Apply Complete Setup` or `Apply Multiplayer Setup` after hand-tuning, because those explicit setup commands deliberately restore the generated defaults. Normal Editor restarts and Windows builds no longer regenerate these transforms.

If Unity was still in Play Mode while scripts were changed, stop Play Mode once and wait for compilation before testing again.

The setup tool also adds persistent `ActorsManager` and configured `AudioManager` services to the Intro `NetworkManager`; these are required before the FPS player and its weapons initialize.

Rebuild the client with `Tools > Zombie Town > Build Local Windows Client` after gameplay or networking code changes.

## Display and language settings

- The Pause / Options menu contains a real **Fullscreen** toggle for standalone clients.
- English is the default for the main menu, class selection, HUD labels, vehicle prompts and replay UI.
- Enable **Dutch language** in Pause / Options to switch to Dutch. The choice is stored in `PlayerPrefs` and is reused after restarting the game.

## Authority boundary

- Server: admission, class validation, health/armor, damage, melee hit result, zombie AI/spawn/death/loot, objectives.
- Owning client: raw input, camera, first-person arms/weapon, immediate cosmetic feedback.
- Replicated: player transform, selected class, armor/health presentation, compact animator state, equipped weapon/action events, zombie state.

## Dedicated server

Create a Dedicated Server build profile. Code compiled with `UNITY_SERVER` starts `StartServer()` automatically. For local testing, run one server build and up to eight client builds against the Unity Transport address/port. Do not expose the default port publicly until rate limits, authentication, timeout/reconnect handling, and deployment firewall rules are configured.

## Players buiten het lokale netwerk

Unity Relay is implemented through the embedded `com.unity.services.multiplayer` 1.2.0 package. The project is linked to Unity Cloud and uses anonymous authentication plus encrypted `wss` transport over the firewall-friendly HTTPS/WebSocket route.

1. Start any copy of the Windows client, click **CREATE GAME**, then **HOST ONLINE (FRIENDS)**. Every distributed client can become the host; no separate server executable is needed for this mode.
2. Share the displayed Relay code. It is copied to the Windows clipboard and remains visible in the upper-right corner after the game scene loads. Press `C` to copy it again.
3. A remote player starts the same build, clicks **JOIN GAME**, enters that code in the empty **RELAY CODE** field, and clicks **JOIN WITH CODE**.
4. The host allocation accepts seven joining clients, for eight players total.

Relay requires an internet connection and a valid linked Unity Cloud project. No router port-forward is required. A code is temporary and stops working after the host closes or ends the session; the next host session receives a new code.

The join field accepts only Unity Relay's valid six-to-twelve-character alphabet. Placeholder text is never submitted as a code, so malformed values are rejected locally before an HTTP request is made.

If joining fails, the client now stays on or returns to the Join page and shows the transport or approval reason. The standalone log is stored at `%USERPROFILE%\AppData\LocalLow\DefaultCompany\My project\Player.log`; ask both the host and joining player for this file when diagnosing an external-service or network-specific failure.

Direct self-hosting is possible but less friendly: bind the server to `0.0.0.0`, allow the game executable through Windows Firewall, forward UDP port `7777` from the router to the host PC, and let players connect to the public IPv4 address. This does not work normally behind carrier-grade NAT (CGNAT), and the host's public address is exposed.

For a persistent authoritative server, make a Linux Dedicated Server build and deploy it to a VPS/game-server provider with a public IP and an allowed UDP game port. The client must never be allowed to authoritatively decide damage, armor, zombie hits, loot, or class bonuses.
