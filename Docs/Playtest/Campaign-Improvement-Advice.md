# Campagne: uitgevoerde verbeteringen

Deze status vervangt het eerdere advies met nog niet uitgevoerde werkzaamheden. De wijzigingen zijn geïntegreerd in de zes gameplay-scènes; technische testresultaten staan in de gekoppelde rapporten.

## Bediening en speler

- F: gebruiken, oppakken, neerleggen, planken plaatsen, ladders en turret. E: volgend wapen. G: squad-opdracht. C: hurken; Shift: sprinten.
- Standaard staan op schaal 1,2: capsule 2,16 m, hurkend 1,08 m. Een later toegewezen hurkprofiel verkleint de speler niet. Te weinig hoofdruimte schakelt niet automatisch hurken in.
- Ladders tonen weer **Press F to climb** bij Engels. Wereldlabels richten zich per camera naar de kijker; materialen en Pack-a-Punch zijn groter en de relevante Nederlandse labels worden vertaald.
- Shops gebruiken één wapendisplay zonder draagarmen. De sniperarmen zijn per wapen passend gemaakt in `Assets/ZombieGame/Prefabs/Weapons/FittedHands`; SVD/Noreen behouden hun eigen armen.

## Groter werk: uitgevoerd

| Level | In de scène geïntegreerd |
|---|---|
| 1 | SMG-punt van $60 langs de eerste route, één verdedigbaar raam en losse planken. Bestaande startloadout en kill-objective behouden. Pack-a-Punch toegevoegd. |
| 2 | Raam en losse planken bij de radio; beschikbaar vanaf de verdedigingsfase. Pack-a-Punch toegevoegd. |
| 3 | Begrensde mitrailleur bij de kade, verplaatsbare kratten en Pack-a-Punch. Operator- en uitstapplek vrijgemaakt van bestaande objecten. |
| 4 | Zijplatform-spawns per ontgrendelde zone, verplaatsbare planken/kratten, gekoppelde raamopeningen, stevige ingangspoort, turret en acht gespreide spelerspawns. Na gate-unlock 60 seconden geen nieuwe aanvoer; bestaande zombies blijven aanwezig. |
| 5 | Twee verkeerd geplaatste appartementswapenpunten naar de daadwerkelijke verdedigingszone verplaatst; Pack-a-Punch toegevoegd. |
| 6 | Reactor- en Airlock-wapenpunten langs de actuele sectieroute geplaatst en hernoemd. Pack-a-Punch toegevoegd. |

De optionele extra turrets in 2, 5 en 6 zijn niet toegevoegd. De geplaatste verdedigingsmiddelen blokkeren de veroveringsfase van Level 2 niet vooraf.

## Verdediging en upgrades

Alle aangesloten zombie-ramen beginnen met vier volle planken (ook maximaal vier na reparatie). De plankposities en breedte worden in de echte wandopeningen gemeten en liggen aan de zichtbare binnenzijde van de muur. Elke plank heeft 45 levenspunten; doorklimmen duurt 2,4 seconden. Na afbreken trekken zombies zich op tot vensterbankhoogte, kruipen door de opening en landen binnen. Planken zijn echte draagbare netwerkobjecten. F op een leeg raam herkent ook de opening zelf; je hoeft geen bestaande plank te raken. De grote bruikbare Level 4-wandramen zijn aangesloten op barricade- en doorbraaklogica. Verstevig de ingangspoort met planken. De oude kleine vaste bouwhekjes zijn uitgeschakeld.

Kratten zijn 1,65 × 1,30 × 1,20 m en hebben 600 levenspunten. Hun collider en NavMesh-obstakel volgen neerzetten/oppakken; zombies die de krat op hun route treffen stoppen om aan te vallen. Een losse krat sluit niet automatisch een brede straat af: plaats meerdere naast elkaar of gebruik een smalle doorgang.

De turretcamera kijkt boven de behuizing uit. De oude 11-secondenopname had zijn knal pas op 7,37 seconden. Een eigen, vooraf geladen PCM-clip van 0,28 seconde start nu binnen circa 10 ms hoorbaar. Schoten gebruiken een zwaarder, lager geluid, mondingsdeeltjes, een korte lichtflits, een zichtbaar kogelspoor en inslagvonken. Pack-a-Punch geeft het wapen blauwe emissie bij upgrade 1 en sterkere paarse emissie bij upgrade 2, zonder de armen mee te kleuren. Upgrade 1 geeft een magazijn van 1,75× en een reserve van 2×; upgrade 2 respectievelijk 2,5× en 3× de basiswaarden. Beide worden bij aankoop gevuld.

## Moeilijkheid aanpassen

- **Loopsnelheid alle levels:** `Assets/ZombieGame/Data/Balance/GameBalanceDatabase.asset` → `zombieSpeedIncreasePerLevel`, standaard 0,06. Levels 1–6 gebruiken 100 / 106 / 112 / 118 / 124 / 130 procent; bestaande type- en golfmultipliers blijven van toepassing.

- **Level 1–3 solo:** `Tools > Zombie Town > Balance > Early solo zombies`, of `Assets/ZombieGame/Data/Balance/EarlySoloZombiePressure.asset`. `spawnRate` bepaalt aanvoer; `aliveLimit` het gelijktijdige aantal. De totale objective-budgetten blijven afzonderlijk ingesteld.
- **Level 4:** `Assets/ZombieGame/Data/Levels/Level4Encounter.asset`. Per stage: `baseMaxAliveOverride`, `spawnIntervalMin/Max`, `enemyPool`. Solo-limieten nu 32 / 48 / 64 / 80; coöp voegt per extra speler acht toe, tot maximaal 128. Ontgrendelde aanvalszijden blijven actief.
- **Drukopbouw:** `Assets/ZombieGame/Data/Balance/DefenseSupplies.asset`: `pressureRampSeconds` en `peakSpawnIntervalMultiplier`.
- **Gate-rust:** `gatePreparationSeconds` op `RetreatDefenseMissionController`, standaard 60. Doorlopen naar de volgende zone verlengt of verkort de resterende rust niet.

## Controle en prestatiemeting

Zie [interactietests](CarryableDefense-AutomatedResults.txt), [scèneplaatsing](Campaign-Placement-Checks.md), [host](DefenseNetwork-Host.txt) en [client](DefenseNetwork-Client.txt). De twee-processen netwerktest controleert oppakken, plaatsen, exclusief bezit en turretbediening; dit is geen volledige campagne met acht spelers.

De CPU-profiler vond een concrete hapering: de eerste afspeelpoging van een twee minuten lange zombie-groepsclip blokkeerde 472 ms door synchroon laden/decomprimeren. De zes zombieclips laden nu vooraf op de achtergrond; lange clips blijven gecomprimeerd. Zie [lagonderzoek en beperkingen](Level4-Lag-Investigation.md) en [oorspronkelijke CPU-capture](Siege-Audio-Hitch-Before.md).

De laatste echte Level 4-run duurde 65 seconden en bereikte 32 zombies. Met het profiler-venster gesloten: 3518 gemeten frames na de opstart, gemiddeld **17,49 ms**, hoogste **43,63 ms**, **geen pieken boven 50 ms**. Eerdere runs hadden pieken boven 500 ms. Dit is een gerichte Editor-controle, geen garantie voor alle pc's, latere zones of acht spelers. De minuut gate-rust is ook in de echte scène gecontroleerd.

Actuele controle: **39 lokale interactiechecks**, **19 host/client-checks** en zes opgeslagen scènes zonder ontbrekende scripts; alle acht Level 4-spawns en de gecontroleerde interactiepunten zijn vrij. De netwerktest ziet turretsporen op beide peers. Een volledige campagne met acht menselijke spelers is niet uitgevoerd.