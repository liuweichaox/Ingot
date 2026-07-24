# Cross-industry scientific validation record

This directory stores versioned dataset manifests. Raw source data is not committed because it remains subject to external licensing and size constraints. A validation run must reacquire the source from the public location in its manifest and pass SHA-256 verification first.

## 2026-07-24 acceptance

| Dataset | Industry/process | Source scale | Source hash | Maximum stream/batch difference | Result |
|---|---|---:|---|---:|---|
| NASA Milling Wear | CNC milling tool wear | 167 runs, 16 cases, about 1.503 million raw samples per sensor | `71486a857939d1416c86c7cf0c469d5e69c7c30495e01ec8a0e13aafd2a313cb` | `1.7763568394002505E-15` | Passed |
| Mendeley Al-Ce | Aging heat treatment | 516 measured records | `aa7f00161cd3f8553ba632b61600ed7e5791b58958218fe8899ce7371280875d` | `3.979039320256561E-13` | Passed |

The NASA MAT source contains non-physical spikes in a small number of runs. Its accompanying documentation places amplified acoustic-emission and vibration acquisition signals in a ±5 V maximum-load range, which is recorded in the manifest. Current signals use a ±10 range that retains all normal observations. Across all six signals, 1,850 out-of-range samples were excluded—about 0.021% of 9.018 million signal samples. Reports retain the excluded count and basis for each signal; cleaning is not silent and no complete machining run is discarded.

A pass means that source information, structure, numeric coverage, chronology, and stream/batch computation meet the current gates. It does not prove a causal benefit from a parameter change. Such a conclusion still requires preregistration, controls, sample-size planning, safety constraints, and site review.

Dataset manifests:

- [`nasa-milling-wear.v1.json`](datasets/nasa-milling-wear.v1.json)
- [`mendeley-al-ce-heat-treatment.v1.json`](datasets/mendeley-al-ce-heat-treatment.v1.json)
<!--  -->
