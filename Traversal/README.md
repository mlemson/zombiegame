# Industriële klimladders

Herbruikbare prefabhoogtes: **2,75 m**, **3,40 m** en **3,85 m**.
Ze gebruiken de bestaande `ClimbableLadder` / `PlayerLadderClimber`-besturing:
E om te beginnen, W/S omhoog/omlaag en springen om los te laten.

## Plaatsen

1. Sleep een `Industrial_Climbable_Ladder_*.prefab` naar de scene.
2. Het draaipunt zit midden in de ladder. Zet de Y-positie op
   `vloerhoogte + halve ladderhoogte` en draai alleen om de Y-as.
3. De blauwe lokale Z-as wijst naar het bovenste platform. `Bottom Exit`
   staat vóór de ladder, `Top Exit` op de platformzijde.
4. Laat bij beide uitgangen ruimte voor de spelercapsule. Maak ook een echte
   opening in de reling. Een visuele opening met een doorlopende collider is
   onvoldoende.
5. Bak de NavMesh opnieuw wanneer zombies de ladder moeten gebruiken.
   De component maakt bij het starten een tweerichtings-NavMeshLink.

De trigger en kinematische Rigidbody zitten al in de prefab. De bestaande
spelercontroller krijgt de klimcomponent via het laddercontact. Er is geen
nieuw inputpakket of aparte klimmanager nodig.

Maak voor afwijkende hoogtes een prefabvariant en pas de geometrie én beide
uitgangen bewust aan. Houd de rootschaal bij voorkeur op (1,1,1). De V6-opbouw
hergebruikt bestaande prefabassets en overschrijft hun wijzigingen niet.

## Retreat Defense opnieuw opbouwen

`Tools > Zombie Town > Level 4 > Build Industrial Environment V6`

Dit vervangt de gegenereerde Geometry/Art in de vier zones, herstelt de
gameplayplaatsingen en bakt de NavMesh. Bewaar eigen handmatige decoratie
buiten deze gegenereerde groepen of verwerk haar in het editorrecept.

## Test

Sla `RetreatDefenseScene` op in Edit Mode en gebruik:
`Tools > Zombie Town > Level 4 > Validate V6 Ladders in Play Mode`.

De optionele test gebruikt een tijdelijke FPS-speler en virtuele gamepad.
Hij controleert vinden van de ladder, omhoog klimmen, boven uitstappen,
afdalen, onder uitstappen en losspringen. Het betreden van de klimstatus
gebeurt rechtstreeks; de fysieke E-toetsaanslag en multiplayer worden niet
door deze test afgedekt. De test keert automatisch terug naar Edit Mode.
