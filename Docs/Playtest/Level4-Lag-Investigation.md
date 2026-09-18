# Level 4: gerichte haperingsdiagnose — 8 september 2026

## Bewezen oorzaak en wijziging

De CPU-profiler ving een frame van 519,61 ms: `ZombieAudio.Update` → `SoundManager.GetHandle` → `SoundManager.LoadFMODSound` nam 471,72 ms. Zie `Siege-Audio-Hitch-Before.md`. De groepsclip `111044__garyq__zombie-group-2-small.wav` duurt 120 seconden; de import stond op DecompressOnLoad, zonder preload en zonder achtergrondladen.

Alle zes door de zombieprefabs gebruikte stemclips laden nu vooraf op de achtergrond. Clips langer dan tien seconden blijven gecomprimeerd in het geheugen; korte effecten worden vooraf gedecomprimeerd. `ZombieAudio` start geen afspeelpoging zolang de clip nog laadt. Dit voorkomt dat een gevechtsframe op zo'n koude clip wacht. De oorspronkelijke audiobestanden en GUIDs blijven behouden.

De vertraagde turretknal had een aparte oorzaak: zijn 11,032s opname bevat de knal pas op 7,374s. `TurretShot.wav` is een eigen 0,28s PCM-fragment met een hoorbare aanzet binnen ongeveer 10ms bij de ingestelde pitch. Geen lange opname meer per kogel.

## Overige waarnemingen en begrenzing

De eerste vervolgmeting met het profiler-venster zichtbaar had nog pieken. Eén spawn kostte 66,78ms, waarvan Animator-initialisatie 40,08ms (`Siege-Spawn-Hitch.md`). Een later frame van 532,21ms bevatte Physics.Simulate 195,50ms en EditorLoop circa 318ms; de renderthread registreerde ook 97,27ms bij GPU-skinning. Dit is geen bewijs voor één gemeenschappelijke runtime-oorzaak: samples kunnen overlappen en CPU-wachttijd bevatten.

Geen GC-collectie op de vastgelegde grote piekframes. Het profiler-venster veroorzaakte aanzienlijke extra Editor-overhead en is na de inspectie gesloten. Er is geen nieuwe zombiepool toegevoegd op basis van deze beperkte meting: correct resetten en repliceren van AI/animatie/health/despawn vraagt een aparte bewezen aanpak.

De herhaling met gesloten profiler-venster staat in `Latest-Frame-Timing.md`; de echte scène draait daarin 65 seconden met een onkwetsbare stilstaande host en maximaal 32 zombies in de eerste zone. Dit is geen standalone GPU-benchmark of volledige achtspelercampagne. De audio-oorzaak is verholpen; volledige afwezigheid van haperingen wordt niet geclaimd.
## Laatste controle zonder profiler-venster

65s echte Level 4-scène, 32 zombies; 3518 frames na de eerste vijf seconden. Gemiddeld 17,49ms; maximum 43,63ms; geen frames boven 50ms. Grootste EditorLoop-sample 17,97ms. In deze run was de eerder waargenomen hapering niet aanwezig. Het verschil met de run met zichtbaar profiler-venster is geen zuivere audio-only A/B-test: de profilerweergave en warme caches verschillen ook.