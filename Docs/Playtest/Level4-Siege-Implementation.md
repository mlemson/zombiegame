# Level 4 — belegering, implementatie in uitvoering

## Status

De C#-compilatie van fps.Game, fps.Gameplay, Assembly-CSharp en Assembly-CSharp-Editor slaagt. Unity heeft de eerste import en prefabgenerator uitgevoerd: de bibliotheek, bouwplek, mitrailleur en ladder bestaan. De nieuwe runtimechecks en plaatsing zijn **nog niet gevalideerd**. De eerdere poorttestresultaten gelden voor de vorige versie, niet automatisch voor deze uitbreiding.

De Editorverbinding is niet beschikbaar. De gerichte inspectie is voltooid en staat in `Temp/SiegeSceneInspection.txt`. `Temp/SiegeLevelFour.install` wacht op de tweede Assets → Refresh buiten Play Mode. Die maakt een scèneback-up en voert de voorbereide plaatsing via `SiegeLevelFourSetup` uit; daarna start de geïsoleerde test. Controleer het Editor-log op `SIEGE_LEVEL4_INSTALLED` en het testresultaat; aanwezigheid van scripts is geen bewijs dat de plaatsing geslaagd is.

## Klaargezette code

- `DefenseSupplyProfile`: 30 startmaterialen, 10 per vaste bouwplek, 20 reparatie-HP per materiaal, 2 seconden bouwen. Reparatiekrediet blijft behouden tussen korte reparaties; opnieuw initialiseren vult niet gratis aan.
- Poorten en ramen kunnen dit profiel gebruiken. Zonder profiel blijft hun bestaande reparatie-economie behouden. Repareren blokkeert persoonlijke wapens; een bemande post verhindert repareren en bouwen.
- `BarricadeBuildSite`: vooraf geplaatste, servergestuurde bouwplek op dezelfde NetworkObject als de repareerbare verdediging; opening moet vrij zijn. Geen vrij bouwsysteem.
- `MountedMachineGun`: servergestuurde bezetting, vuursnelheid, schaderaycast en richtboog. E om te bedienen, muis om te richten/vuren, E/Esc om uit te stappen. Afzonderlijke blokkade voor persoonlijke wapens; beweging en ladder/zipline worden tijdelijk uitgeschakeld.
- Optionele `continuousSiege` in het encounterprofiel: 20 seconden voorbereiding, 15 seconden spawnpauze na terugtrekken, oplopende druk zonder eindig wavebudget. Betaalde deuren zijn vooraf te openen; de linie schuift pas door wanneer een speler de volgende zone betreedt. Bestaande zombies verdwijnen niet tijdens de ademruimte.

## Vindplaats na uitvoeren van de generator

`Assets/ZombieGame/Prefabs/Gameplay/GameplayLibrary.asset`

Menu: **Tools → Zombie Town → Gameplay → Open Gameplay Prefab Library**.

De bibliotheek verwijst naar de poort, bouwplek, mitrailleur, ladder, raam, Pack-a-Punch en wapenpunt. Nieuwe visuals komen uitsluitend uit reeds geïnstalleerde Synty-packs. De bestaande raam-prefab wordt door de generator niet overschreven.

Plaats roots op de vloer met schaal 1. Poort/bouwplek: +Z veilig. Mitrailleur: +Z vuurrichting, operator aan -Z; houd de zijwaartse EXIT vrij. Ladder: 3,6 meter; leg TOP op een beloopbare vloer en BOTTOM aan de toegankelijke kant. Bouwplekken moeten ver genoeg uit elkaar staan om niet dezelfde E-interactie te activeren, en buiten de terugtrekroute blijven.

## Plaatsing voorbereid, uitvoering nog afwachten

1. Level 4-profiel verbinden; terugtrekdeuren op 20/60/90 basispunten zetten. Solo blijven na het startwapen van 75 punten genoeg punten voor de eerste deur over.
2. Dichte voorwand vervangen door bestaande muurmodules, één raam en de poort. Oude zijopeningen in zone 1 sluiten; loopvloer, twee ladders, één mitrailleur en twee bouwplekken plaatsen.
3. Alle stages laten spawnen vanaf de voorzijde buiten het fort; NavMesh opnieuw bakken en opslaan.
4. Wapenpunten voorzien van hergebruikte wapenmeshes, blauwe verlichting en prijs; startwapen dichter bij de spawn zetten.
5. De plaatsing visueel controleren via `Docs/Playtest/Level4-Siege-Front.png` en fysiek in Unity. Controleer vooral vloeraansluitingen, ladderuitgangen, raamklimhoogte en bereikbaarheid van latere zones.
6. Menu **Test Siege Systems (isolated scene)** draaien. Dit breidt de bestaande poortfixture uit met materiaalbudget, herstelkosten, bouwen, exclusieve bediening en uitstappen. Daarna werkelijk schieten vanaf de post, muur-/raamroutes, voorbereiding en terugtrekken in Level 4 testen. Afzonderlijke remote client blijft apart te controleren.

Eerste kleine gameplayproef: één speler krijgt 20 seconden voorbereiding, kiest tussen één bouwplek of reparatiemateriaal bewaren, verdedigt de voorpoort en trekt door de al betaalbare deur terug zodra de waarschuwing begint.
