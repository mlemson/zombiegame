# Bus en animaties — 8 september 2026

De kopie staat naast de bestaande bus in `Assets/Scenes/ZombieTownScene.unity`, op `(-14.99, 0, 7.04)`, schaal **1.4**. Prefab: `Assets/ZombieGame/Prefabs/Vehicles/BarricadableCityBus.prefab`.

De tien zijramen en voorruit zijn dichttimmerbaar (vier planken per opening). Twee deuren blijven open. De kleine hoge achterruit blijft onderdeel van de vaste carrosserie. Bij het starten verschijnen 44 echte oppakbare planken achterin. Gebruik **F** om een plank op te pakken en op een raam te plaatsen. Openingen en planken bewegen met de bus mee. Geplaatste planken blokkeren schoten; tussen de planken en door open ramen kun je schieten.

## Zelf je twee clips toevoegen

Selecteer `Assets/ZombieGame/Animations/Bus/YourBusAnimations.asset` in het Project-venster.

1. Sleep je **Humanoid** hang-aanval in **Zombie Hang Attack**.
2. Sleep je **Humanoid** raam/buskruip-clip in **Window Bus Crawl**.
3. Klik **Toepassen op zombie-animator**. Lege velden behouden de bestaande animatie.

Dit vervangt alleen de Motion van `BusHangAttack` en `BusCrawl`; de bestaande overgangen blijven behouden. Gebruik in-place clips: de gameplay beweegt de zombie met de bus mee. De crawl-slot is voor de busraam-kruipfase; gewone gebouwramen behouden hun bestaande klimbeweging. Beide velden staan alvast klaar en zijn nog leeg. De huidige tijdelijke animaties blijven dus actief.

De bus start nu met vier volle en vier halfvolle zijramen; twee zijramen en de voorruit blijven open. De 44 losse planken blijven beschikbaar. Startverdeling: `BusDefenseLayout.startingBoards` op de bus-prefab, in dezelfde volgorde als `openings`.

## Korte animatiecontrole

| Beweging | Aanwezig / advies |
|---|---|
| Lopen, rennen, aanvallen, geraakt worden en sterven | Bestaande zombieclips en Animator-states aanwezig; hiervoor is geen extra pakket nodig. |
| Springen naar de bus | Bestaande `HumanM@Jump01 - Begin` van Kevin Iglesias hergebruikt. Een agressieve zombie-aanloopsprong kan dit later verbeteren. |
| Vastklampen en planken lostrekken | Nieuwe `Zombie_BusHang` en `Zombie_BusHangAttack`, afgeleid van de bestaande klimclip. Hand-IK grijpt het bovenste raamkozijn. Dit zijn functionele tussenoplossingen, geen nieuwe mocap-opnames. |
| Door een raam kruipen | Bestaande klim- en crouchwalk-clips. Hoogste prioriteit voor verbetering: een echte **window mantle / low vault** met handen op de dorpel en benen door de opening. |
| Loslaten en vallen van de bus | Een aparte **hang release → fall → landing** ontbreekt nog als specifieke voertuiganimatieset. |
| Speler timmert / draagt hout | Een specifieke first-person **hameren**, **plank oppakken** en **plank vasthouden**-set zou de bestaande F-interactie duidelijker maken. |

Voeg zelf bij voorkeur Humanoid-clips toe voor **jump-to-grab, hang idle, hang attack, window vault en drop/land**. Vervang de Motion van `BusJump`, `BusHang`, `BusHangAttack` of `BusCrawl` in `Assets/Animations/Zombies/ZombieAnimator.controller`. De gameplay verplaatst de zombie; laat root motion tijdens het instappen door de code uitgeschakeld. De hangclips staan onder `Assets/ZombieGame/Animations/Bus/`.

[Mixamo](https://www.mixamo.com/) biedt animaties die op een eigen personage kunnen worden bekeken en overgezet. [MoCap Online Zombie](https://mocaponline.com/collections/animation-starter-packs/products/zombie) is een optie voor meer variatie in kruipen, bewegen en aanvallen. Controleer vóór aanschaf de cliplist: een passende raam-vault en bus-hang zijn hiermee niet gegarandeerd.

## Controle en grenzen

Zie `BusDefense-AutomatedResults.txt`: geïsoleerde NGO-hosttest voor netwerkspawns, raamdoorgang van schiet-rays, hout meenemen, planken plaatsen/slopen, springen/hangen bij verplaatsen en draaien, kruipen, deurinstap en terugkeer naar straatnavigatie, dood tijdens hangen en rijden met WheelColliders. Screenshots staan in `Docs/Playtest/BusDefense/`.

Geen volledige campagne-rit met een tweede netwerkclient uitgevoerd. De originele rijdbare-bus-prefab is behouden. Runtime-raamobjecten worden apart gespawned om geneste dynamische NetworkObjects te vermijden.
