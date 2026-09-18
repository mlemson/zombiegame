# Repareerbare verdedigingspoort

Prefab: `Assets/ZombieGame/Prefabs/Gameplay/Barricades/RepairableGate.prefab`.

1. Sleep de hele prefab in een opening van circa 3,4 meter. Zet de root op vloerniveau; houd schaal `(1,1,1)` en roteer alleen om Y.
2. De groene **SAFE SIDE (+Z)** wijst naar de speler; de rode **ATTACK SIDE (-Z)** naar de zombies. De zijmuren moeten aansluiten, zodat zombies niet om de poort kunnen lopen.
3. Bak de NavMesh over de vloer aan beide kanten. De prefab sluit zichzelf uit van de bake: zijn carving-obstacle blokkeert alleen zolang de poort intact is. Plaats het attack point op bereikbare NavMesh.
4. Laat de groene reparatieplek en de `Rebuild Clearance` vrij. De speler kan staand over de 1,15 m hoge verdediging schieten. Houd **E** aan de veilige kant vast om te repareren; schieten wordt tijdens reparatie geblokkeerd.
5. Stel op de root gezondheid, reparatie per seconde, zombieschade-multiplier, waarschuwingsdrempel en minimale waarschuwingstijd in. `Allow Rebuild After Breach` bepaalt of herstel mogelijk is; een bezette opening verhindert herstel. Optionele geluiden staan op dezelfde component.

Dit is een doorbreekbare verdediging, geen betaalde terugtrekdeur. Er zijn geen mapobjecten of ramen vervangen. In een netwerksessie beheert de server schade, reparatie en de doorbraakstatus; de prefab heeft een NetworkObject voor plaatsing in de scene.

Testmenu: **Tools > Zombie Town > Gameplay > Test Repairable Gate (isolated scene)**. De fixture gebruikt tijdelijk een lege startscène en herstelt daarna de vorige Play Mode-startscene. Resultaten: `RepairableGate-AutomatedResults.txt`.

Kleine eerste proef: plaats één poort bij de zichtbare hoofdingang van zone 1 en laat drie gewone zombies naderen; vergelijk hoe lang de poort standhoudt met alleen schieten versus afwisselend schieten en repareren, met de terugtrekdeur al open.
