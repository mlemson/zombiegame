# Bus physics fix — 2026-09-09

The new barricadable bus could lift and tilt. The physics test exposed both crowded contact filtering and collisions with the bus's own independently networked attachments.

- BusDriver now selects nearby zombies from the active registry rather than a fixed 128-collider overlap buffer, which can fill with cargo and scenery.
- Zombie colliders use the new ZombieBody layer. Bus wheel suspension excludes that layer; body contact pairs remain isolated while zombie entry and impact logic stay active.
- Bus-mounted windows and resting cargo use BusAttachment. The defense bus chassis and wheels exclude it. These remain non-trigger colliders for interaction and weapon queries. Dropped cargo returns to its original layer when it no longer rests on a vehicle.
- Imported bus assets and zombie prefab GUIDs were preserved. Both layers were added to previously unused slots in TagManager.

Before attachment isolation, the parked fixture tilted 10.07 degrees and only one wheel reported road contact. After the change it tilted 0.044 degrees and all four wheels reported the road. With 18 zombies and 180 extra nearby triggers, the measured upward movement was 0.000 m. Hanging, board breaking, crawling, doors, carry/place, shooting through open windows, and actual wheel acceleration passed. See BusDefense-AutomatedResults.txt and BusDefense/crowd-collision-regression.png.

This is an isolated local NGO host test, not a remote-client or full live-match verification. Restart Play Mode; existing standalone executables need rebuilding to include the changes.

Unity's collider layer exclusion API is documented at https://docs.unity3d.com/ScriptReference/Collider-excludeLayers.html .
