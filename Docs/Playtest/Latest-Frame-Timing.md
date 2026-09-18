# Editor playtest frame timing

Measured frames after first 5 seconds: 840; mean: 10.42 ms; worst: 90.55 ms.

Largest available EditorLoop sample: 0.00 ms.

Spikes above 50 ms (maximum 256 retained). GC collection count changes are correlation, not proof of cause. Editor timings include Editor overhead.

Frame | ms | allocated bytes | gen-0 collections since previous frame
--- | --- | --- | ---
969 | 90.55 | 826229 | 0

Spike profiler sample durations (ms; -1 means unavailable; samples may overlap):

Frame | PlayerLoop | render | physics | authored spawn
--- | --- | --- | --- | ---
969 | 12.49 | -1.00 | 1.32 | 0.00
