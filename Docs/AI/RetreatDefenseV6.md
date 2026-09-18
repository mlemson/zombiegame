# Retreat Defense V6 — omgeving en klimladders

Datum: 2026-09-05. Unity 6000.5.7f1 / URP.
Scene: `Assets/Scenes/RetreatDefenseScene.unity`.

## Uitgevoerd

- Vier zones met een gezamenlijke industriële stijl: grijze wand- en
  vloerpanelen, rode servicepanelen/poorten, gele veiligheidsranden,
  borstweringen, leidingen, installaties en duidelijke bewegwijzering.
- Entry Yard: verhoogde uitkijkpost, trap, bevoorrading en gegroepeerde dekking.
- Workshop/Barracks: echte ruimtes met doorgangen, twee flankroutes,
  beloopbaar werkplaatsdak, toegangstrap, ladder, ventilatie en werkplaatsprops.
- Fuel Services: vaste industriële tank met fundering, zadels en leidingen;
  servicedek en twee routes naar het eindplatform. De tank staat vrij van de
  voet van de tweede ramp.
- Signal Control: compacte achthoekige toren met ingang, observatieband,
  beloopbaar dak, antenne, schotel, console, generator en bevoorrading.
- Drie prefab-ladders van 2,75 / 3,40 / 3,85 meter. Ze gebruiken het bestaande
  klimsysteem en hebben expliciete onderste/bovenste uitstappunten.
- Twee tijdens Play Mode aangetoonde klimproblemen verholpen: afdalen aan de
  binnenzijde van een platform en het missen van de ladder bij een volle
  colliderzoekbuffer (21 colliders waar slechts 16 werden gelezen).
- Wereldteksten testen nu tegen diepte, zodat tekst niet door muren zichtbaar
  is. Het fontatlasmateriaal wordt na laden/vernieuwen opnieuw gekoppeld.
- Handmatige oude blokken/ladder die niet in de NavMesh-hiërarchie zaten zijn
  bewaard onder de uitgeschakelde `V6 Archived Manual Blockout`.
- Geïmporteerde Synty/Town/Office-prefabassets zijn niet overschreven.

## Bestanden en hergebruik

- `Assets/Editor/RetreatDefenseEnvironmentPass.cs`: reproduceerbaar V6-recept.
- `Assets/Editor/RetreatDefenseV5Setup.cs`: bestaande structuur-/gameplayopbouw,
  nu gekoppeld aan de industriële pass. Ook gameplayherstel behoudt de poorten
  en stationafwerking.
- `Assets/Editor/RetreatDefenseLayout.cs`: aangepaste route- en stationposities.
- `Assets/Scripts/Traversal/ClimbableLadder.cs`: optionele expliciete uitgangen;
  bestaande ladders zonder deze verwijzingen behouden hun oude berekening.
- `Assets/Scripts/Traversal/PlayerLadderClimber.cs`: meegroeiende zoekbuffers.
- `Assets/Scripts/Traversal/WorldSignText.cs` en
  `Assets/ZombieGame/RetreatDefense/WorldSignText.shader`: occludeerbare borden.
- `Assets/Editor/RetreatDefenseLadderValidation.cs`: optionele Play Mode-probe.
- `Assets/ZombieGame/Prefabs/Traversal/README.md`: ladderplaatsing en bediening.

Opbouwen: `Tools > Zombie Town > Level 4 > Build Industrial Environment V6`.
Dit vervangt gegenereerde Geometry/Art; plaats handmatige decoratie daar niet
zonder haar ook in het recept te verwerken. Bestaande ladderprefabassets worden
hergebruikt, niet automatisch overschreven.

Volledige scene-backup vóór de V6-pass:
`Assets/_Recovery/RetreatDefense_BeforeEnvironmentV6.unity`.

## Validatie — uitgevoerd

De laatste drie volledige opbouwruns eindigden met:

```
LEVEL4_V5_VALIDATE|COLLISION|structuralChecked=104|failures=0
LEVEL4_V5_VALIDATE|GAMEPLAY|zones=4|shops=10|pickups=4|guards=3
LEVEL4_V5_VALIDATE|ART|artRoots=4|fuel=True|tower=True|interiorTrees=False|routeOverlaps=0
LEVEL4_V6_VALIDATE|failures=0
LEVEL4_V5_VALIDATE|result=PASS|failures=0
LEVEL4_V5_REFINE|complete=True|stages=4|gates=3
```

De validator controleert tevens vloer-/rampaansluitingen, spelerspawns,
zombie-breachroutes, gesloten/open poorten, werkplaats-/barracksdoorgangen,
beide flanken, toegangstrap, toreningang en beide ladderuiteinden op de NavMesh.
Alle drie geplaatste ladders zijn verbonden prefabinstances.

Twee opeenvolgende volledige Play Mode-probes zijn geslaagd, elk met negen
traversalcontroles op de echte FPS-spelercontroller:

| Ladder | Omhoog / boven uitstappen | Omlaag / onder uitstappen | Losspringen |
|---|---|---|---|
| Service Deck | PASS | PASS | PASS |
| Signal Tower Roof | PASS | PASS | PASS |
| Workshop Roof | PASS | PASS | PASS |

Elke test controleert ook herstel van normale locomotion en
`CharacterController.detectCollisions`. De tijdelijke speler, ActorsManager,
gamepad en InputSettings-kopie blijven niet achter in de scene. De probe gebruikt
virtuele beweging en roept de begin-klimactie rechtstreeks aan; fysieke E-input
is niet afzonderlijk geautomatiseerd.

Visuele inspectie uitgevoerd voor de werkplaats op spelerhoogte en het
eindplatform vanuit overzicht. Geen nieuwe Console-errors/warnings na de
laatste opbouw en herhaalde Play Mode-test. De scene is opgeslagen in Edit Mode.

## Grenzen van deze validatie

- Geen nieuwe Windows-playerbuild of volledige host/client-sessie uitgevoerd.
- Bij het hervatten stond reeds `Local client build failed: Unknown (0 errors)`
  in de Console. Dat was geen resultaat van de bovenstaande geslaagde tests;
  de oorzaak van die eerdere buildfout is hier niet vastgesteld.
- NavMesh-padcontroles zijn geen volledige zombie-/guard-gevechtstest.
- Geen frameratebudget of volledige balans-/survivalduur gemeten.
- Een conceptillustratie is de stijldoelstelling, geen pixel-identieke belofte.

Status: gereed voor verdere Editor-playtests; geen release-/multiplayercertificatie.
