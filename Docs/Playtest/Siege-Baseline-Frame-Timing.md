# Editor playtest frame timing

Measured frames after first 5 seconds: 4183; mean: 14.71 ms; worst: 453.06 ms.

Spikes above 50 ms (maximum 256 retained). GC collection count changes are correlation, not proof of cause. Editor timings include Editor overhead.

Frame | ms | allocated bytes | gen-0 collections since previous frame
--- | --- | --- | ---
693 | 58.78 | 135977 | 0
1083 | 453.06 | 153129 | 0
1364 | 119.41 | 145798 | 0
1402 | 94.89 | 161582 | 0
1512 | 426.34 | 317415 | 0
1534 | 52.75 | 146745 | 0
