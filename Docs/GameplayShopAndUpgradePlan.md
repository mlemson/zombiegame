# Gameplay shop and upgrade plan

## Doel

Alle levels gebruiken dezelfde leesbare economy: shops zijn herkenbaar, wapens unlocken logisch per route en iedere speler ziet dezelfde koop- en upgrade-status. Statistieken en prijzen blijven bewerkbaar in Unity via data assets; level setup scripts mogen alleen plaatsen en koppelen.

## Vaste shopoplossing

Gebruik per level een `Shop Hub` per progression-zone. Een hub bestaat uit:

- een grote blauwe shop-crate of machine;
- een los maar gekoppeld naam- en prijslabel naar de speler gericht;
- een `WeaponShopTerminal` op hetzelfde parent-object;
- een vaste interactieradius;
- een zichtbare zonekleur per type: weapon, ammo, upgrade;
- geen losse wapens buiten een hub, behalve een expliciet authored pickup.

De crate, het label en het interactiepunt blijven in dezelfde lokale parent. Daardoor kunnen rotatie en schaal niet meer onafhankelijk ontsporen.

## Shopmatrix

| Level | Zone | Weapons | Richtprijzen solo / 2-3 / 4+ | Unlockmoment |
|---|---|---|---:|---|
| 1 | Start | Service pistol, basic SMG | 0-60 / 0-60 / 0-60 | Basisloadout en eerste aankoop |
| 1 | Mid route | shotgun, marksman rifle | 80-120 / 100-150 / 120-180 | Na eerste objective |
| 2 | Outpost | upgraded pistol, SMG, coach gun | 45-100 / 60-140 / 75-180 | Assault afgerond / radio actief |
| 2 | Radio defense | shotgun, launcher | 180-300 / 220-375 / 275-450 | Defensefase, niet direct bij spawn |
| 3 | Harbor | SMG, shotgun, marksman rifle | 120-300 / 150-375 / 180-450 | Power restored |
| 3 | Ferry route | sniper, launcher | 350-550 / 440-690 / 525-825 | Beacon actief |
| 4 | Entry Yard | blaster, upgraded pistol, sword | 38-75 / 75-150 / 150-300 | Startzone |
| 4 | Workshop | SMG, coach gun | 80-160 / 160-320 / 320-640 | Gate 1 open |
| 4 | Service Deck | marksman, Noreen | 175-325 / 350-650 / 700-1300 | Gate 2 open |
| 4 | Final Stand | SVD, launcher, chainsaw | 260-700 / 525-1400 / 1050-2800 | Gate 3 / final line |
| 5 | Harbor sectors | pistol, SMG, shotgun, rifle | 60-350 / 75-440 / 90-525 | Sector progression |
| 6 | Dead Orbit sectors | blaster, sword, SMG, shotgun, SVD, launcher | 75-700 / 90-875 / 105-1400 | Sector progression |

De bestaande basisprijzen blijven het technische fallback-systeem. De uiteindelijke matrix hoort in data assets te staan, zodat prijsaanpassingen niet in levelgenerators hoeven te worden gezocht.

## Economy-regels

- Level 1 start met `0` punten.
- Directe overgang van Level 1 naar Level 2 geeft `100` punten.
- Iedere latere levelovergang behoudt `floor(huidige punten * 0.5)`.
- Solo Level 4: deuren kosten 50% van de basisprijs.
- 2-3 spelers: basisprijs.
- 4 of meer spelers: deuren kosten 200% van de basisprijs.
- Solo Level 4 gebruikt minder levende enemies en minder orc-pressure.
- Weaponprijzen krijgen dezelfde schaal pas nadat de shopmatrix in data assets staat. Tot die tijd blijft weapon pricing centraal en voorspelbaar.

## Pack-a-Punch

Voeg een `WeaponUpgradeMachine` toe als herbruikbare prefab en een `WeaponUpgradeDefinition` ScriptableObject per wapenfamilie. De machine heeft twee levels per wapen:

| Upgrade | Damage | Fire rate | Magazine/reserve | Audio | Visual |
|---|---:|---:|---:|---|---|
| I | x1.35 | 15% sneller | x1.25 | pitch +0.03, volume +0.05 | emissive accent / kleurband |
| II | x1.80 totaal | 30% sneller | x1.60 totaal | pitch +0.06, volume +0.10 | sterker emissive accent / tweede kleur |

Voor melee betekent fire rate een lagere attack delay. Voor charge weapons betekent het een lagere charge duration. Voor physical-bullet weapons moeten zowel `ClipSize` als reserve `AmmoCapacity` correct worden aangepast.

De machine moet:

- alleen het actieve wapen van de lokale speler upgraden;
- per wapen maximaal twee upgrades toestaan;
- server-authoritatively punten aftrekken en upgrade level synchroniseren;
- een upgrade blokkeren als de speler te weinig punten heeft;
- de upgrade behouden bij leveltransitie als het wapen wordt behouden, of expliciet resetten als de loadout wordt gewist;
- een duidelijke prompt tonen: weapon, upgrade level, prijs, resultaat;
- dezelfde weapon key gebruiken voor alle clients.

## Technische implementatievolgorde

1. Maak `WeaponUpgradeDefinition` met weapon key, twee kosten, damage multiplier, delay multiplier, clip multiplier, reserve multiplier, audio pitch/volume en visual preset.
2. Maak `WeaponUpgradeState` op de speler met weapon key en level 0-2. Repliceer dit met NetworkVariables of een server-owned serializable state.
3. Maak `WeaponUpgradeMachine` met dezelfde RPC-validatie als weapon purchases.
4. Maak `WeaponUpgradeRuntimeApplier` die alleen runtime waarden wijzigt en de originele waarden bewaart.
5. Pas crosshair, ammo HUD en weapon name aan wanneer het upgrade level verandert.
6. Voeg een eenvoudige visual presenter toe: emissive material override, accentkleur en optioneel een bestaande muzzle/energy VFX. Geen nieuw wapenmodel nodig.
7. Voeg upgrade state toe aan save/transition-regels.
8. Maak één testprefab en pas daarna alle levels aan.

Een nieuwe WAV is niet nodig. Gebruik dezelfde `ShootSfx` en pas `AudioSource.pitch` of een gecontroleerde upgrade pitch toe. Pitch mag beperkt blijven zodat het wapen herkenbaar blijft.

## Unity-bewerkbaarheid

De gewenste Inspector-workflow:

- `WeaponShopCatalog` asset: weapon key, prefab, display name, basisprijs.
- `WeaponUpgradeCatalog` asset: per key de twee upgrades.
- `LevelShopLayout` asset: level, zone, terminal pose, unlock condition.
- level setup scripts plaatsen alleen prefab + catalog reference.
- geen hardcoded damage/prijzen in `LevelFourRetreatDefenseSetup.cs`.

## Multiplayer-audit in theorie

Op basis van de huidige code:

- **Points/aankopen:** server trekt punten af via RPC; weapon-resultaat wordt naar iedereen gestuurd maar lokaal alleen op de owner toegepast. Dit is in theorie correct voor eigendom, maar moet worden getest op dubbele RPC's en late responses.
- **Health:** server schrijft `SyncedHealth`; clients spiegelen die waarde. Dit is in theorie goed voor zichtbare health, maar damage audio is lokaal/replicated afhankelijk van het eventpad.
- **Downed/revive:** server bezit de state; `IsDowned` en `ReviveProgress` zijn NetworkVariables. Dit ziet er consistent uit.
- **Enemy spawns:** mission controllers spawnen op de server en NetworkObjects repliceren. In theorie zien clients dezelfde enemies.
- **Guards:** guards worden server-side gespawned en NetworkObjects zijn aanwezig. Visuals, weapon IK en shot audio moeten in een echte host/client-test worden bevestigd.
- **Cursor/crosshair:** lokale input/UI wordt per owner opgebouwd. Dat is correct: spelers hoeven niet dezelfde cursorpositie te zien, maar moeten dezelfde weapon state en targetfeedback krijgen.
- **Audio:** wereld-audio is per client lokaal; dezelfde gebeurtenis moet dus op alle clients correct worden getriggerd. Dit is het grootste resterende verificatiepunt.
- **Scenes:** netwerk-scene loads lopen via de server; terug naar menu verbreekt de sessie. Dit is in theorie correct.

## Rebuild-notitie

Na wijzigingen aan Level 4 moeten de actieve redesign-instellingen opnieuw via de juiste V5 tuning/rebuild worden toegepast. Controleer daarna in de Inspector expliciet:

- `spawnFlyingDemons = false`;
- `smallOrcStartDelay = 360`;
- gates hebben de gewenste basisprijzen;
- spawnpoints staan alleen voor de Entry Yard;
- alle structurele colliders zijn aanwezig.
