# Last Exit — first playable finale

Update: de bus begint met volle, halfvolle en open ramen. Geplaatste en door zombies gebroken planken hebben korte, ruimtelijke geluiden. De controlepost toont vijf sloten: elke 20% voortgang verdwijnt een slot en klinkt een metalen kraak. Dit is ontgrendelvoortgang; de finale-gate blijft tot 100% gesloten. De timer pauzeert nog steeds wanneer de bus weg is.

Gewone `RepairableGate`-verdedigingsgates klinken bij 20/40/60/80/100% verloren gezondheid. Kleine treffers binnen dezelfde stap spelen geen extra breekgeluid; een grote treffer over meerdere stappen geeft één kraak. Repareren maakt opnieuw beschadigen hoorbaar. Planken plaatsen op deze gates heeft ook plaatsingsgeluid.

Geluiden en volume zijn vervangbaar in `Assets/ZombieGame/Resources/DefenseAudioSettings.asset`. De drie korte, gegenereerde WAV-effecten staan in `Assets/ZombieGame/Audio/Defense`. Ze gebruiken de bestaande Impact-mixergroep. Beginsituaties en laat aansluiten spelen geen historische plankgeluiden af.

De twee lege animatievelden staan in `Assets/ZombieGame/Animations/Bus/YourBusAnimations.asset`; zie Bus-Animation-Advice.md voor de drie stappen.

Scene: Assets/Scenes/BusEscapeFinaleScene.unity. Select LEVEL 7 in the game creation menu. Campaign progression continues here after Dead Orbit; completion uses the existing Win scene.

An approximately 220 m evacuation road uses existing POLYGON shops, abandoned trucks, destroyed walls and rubble. Dusty daylight and quarantine barriers establish the atmosphere. This is an initial playable layout, with space for a richer ruined skyline and environmental storytelling.

1. Take supplies, buy weapons and board the bus windows with the wood inside.
2. Press F at the departure post, then drive the bus using the existing vehicle controls.
3. Park before the checkpoint barrier. Press F at the roadside control, then defend for 35 seconds. The timer pauses if the bus leaves.
4. Drive through the opened checkpoint to the evacuation bay. Every participating survivor must be inside the bus for four seconds to finish.

Roadside zombie pressure is capped and scales with player count. Existing jumping, hanging and window entry are reused. The new scene is saved separately; it was not merged into Retreat or Zombie Town.

Validation: actual-scene isolated NGO host passed departure, invalid remote interaction, missing-bus rejection, checkpoint pause/resume, real zombie spawning, opening the gate, waiting for an outside survivor and completing the shared game flow. See BusEscapeFinale-AutomatedResults.txt. No remote client, fresh standalone build or full manual drive-through has been validated. Verified preview images are in LastExit; sign text has been fitted to its boards.

Next iteration: manual solo/co-op drive-through, route and spawn tuning, then denser ruins and escape audio. Bus recovery, bus destruction and an NPC driver are not implemented in this first slice.

