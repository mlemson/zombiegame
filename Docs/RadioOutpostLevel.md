# Level 2: Radio Outpost

`RadioOutpostScene` is een los multiplayerlevel naast `ZombieTownScene`. De host kiest het level op de pagina **Maak spel**; clients volgen automatisch via Netcode scene synchronization.

## Missieverloop

1. Schakel de zes stilstaande, gewapende bewakers rondom en op het commandogebouw uit.
2. Ga door de open voordeur naar de radio en druk op `E`.
3. Verdedig de actieve radio 3 minuten. Zestien spawnpunten rondom de buitenpost blijven zombies leveren, met maximaal 30 levende zombies tegelijk.
4. Na de timer wordt de bestaande overwinning-flow gestart.

## Bewaker-AI

De AI is server-authoritative en gebruikt expliciete toestanden: `Idle`, `Suspicious`, `Alert`, `Engaging` en `Dead`.

- Een zichtbaar, gereed player-object brengt de bewaker naar `Engaging`.
- Een gewoon schot binnen het gehoorbereik brengt hem eerst naar `Suspicious`: hij kijkt naar de geluidsbron en gaat daarna tijdelijk naar `Alert`.
- De startpistoolvariant met silencer heeft `IsSilenced` aan en meldt geen schotgeluid.
- Bewakers blijven op hun post en schieten server-side hitscan; damage, health en death worden via Netcode verwerkt.

## Scene opnieuw genereren

Gebruik `Tools > Zombie Town > Create Radio Outpost Level`. Dit maakt de scene opnieuw aan, bakt de NavMesh en zet hem in Build Settings. Omdat dit de gegenereerde scene vervangt, moet handmatig scene-design daarna bewust worden teruggezet of in de generator worden verwerkt.

Gebruik `Tools > Zombie Town > Validate Radio Outpost Level` voor de structurele controle van missie, guards, radio, ingang, spawnpunten, NavMesh en Build Settings.

Belangrijk voor delen: verstuur de volledige zip/buildmap. Alleen `ZombieTownClient.exe` is niet voldoende; `ZombieTownClient_Data`, `MonoBleedingEdge` en `UnityPlayer.dll` horen erbij.
