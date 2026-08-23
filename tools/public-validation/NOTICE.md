# Dataset attribution

`data/fdm-doe-grid.csv` is a normalized subset of:

> Aktepe, Elif; Ergün, Uçman (2026), “FDM 3D Printing Dataset: Printer Type, Materials, Process Parameters, and Tensile, Hardness and Roughness Test Results”, Mendeley Data, V2, doi: 10.17632/zd6td6svd6.2.

Source: https://data.mendeley.com/datasets/zd6td6svd6/2

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

Changes made by the Ingot project: selected the six complete closed-printer PLA+/PETG × grid/triangles/zigzag 3×3×3 DOE grids; renamed columns; normalized categorical labels; retained only the controls and outcomes used by the public benchmark. No outcome values were synthesized or imputed.

`data/crossed-barrel.csv` is a normalized copy of the Crossed Barrel dataset in:

> Liang, Q.; Gongora, A. E.; Ren, Z. et al. (2021), “Benchmarking the performance of Bayesian optimization across multiple experimental materials science domains”, *npj Computational Materials* 7, 188, doi: 10.1038/s41524-021-00656-9.

The underlying experiments are described in:

> Gongora, A. E.; Xu, B.; Perry, W. et al. (2020), “A Bayesian experimental autonomous researcher for mechanical design”, *Science Advances* 6(15), eaaz1708, doi: 10.1126/sciadv.aaz1708.

Source: https://github.com/PV-Lab/Benchmarking/blob/7585c517ad88e676c42c6bf24a8ad278e01ddb21/datasets/Crossed%20barrel_dataset.csv

License: [MIT](https://github.com/PV-Lab/Benchmarking/blob/7585c517ad88e676c42c6bf24a8ad278e01ddb21/LICENSE)

Changes made by the Ingot project: renamed the four published design variables and toughness outcome with descriptive names and units, sorted designs, and averaged only exact design replicates. The committed fixture retains the three-replicate count and sample standard deviation. Its 600 unique settings reconcile to all 1,800 physical tests. No outcome was synthesized or imputed. The pinned source CSV has SHA-256 `2c01f875f3c210e986ca6142bf20f417884c2ad7d6f008c2fc574b44a3d5f606`; `prepare_crossed_barrel_fixture.py` documents the deterministic transformation.

The source repository's license notice is reproduced below as required:

> MIT License
>
> Copyright (c) 2020 MIT PVLab
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

`data/airfoil-self-noise.csv` is a normalized copy of:

> Brooks, Thomas; Pope, D.; Marcolini, Michael (1989), “Airfoil Self-Noise”, UCI Machine Learning Repository, doi: 10.24432/C5VW2C.

Source: https://archive.ics.uci.edu/dataset/291/airfoil+self+noise

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

Changes made by the Ingot project: added stable row identifiers, assigned descriptive column names from the repository metadata, and converted whitespace-delimited rows to CSV. The 1,503 unique experimental settings and measured outcomes are unchanged. The official ZIP has SHA-256 `5c7767ba53ad827d3f48ba1eb9434117f4892df8f10bc4c99e118a9e8a7ae07c`.

`data/yacht-hydrodynamics.csv` is a normalized copy of:

> Gerritsma, J.; Onnink, R.; Versluis, A. (1981), “Yacht Hydrodynamics”, UCI Machine Learning Repository, doi: 10.24432/C5XG7R.

Source: https://archive.ics.uci.edu/dataset/243/yacht+hydrodynamics

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

Changes made by the Ingot project: removed the archive's trailing blank line, added stable row identifiers, assigned descriptive column names from the repository metadata, and converted whitespace-delimited rows to CSV. The 308 unique physical-experiment settings and measured outcomes are unchanged. The official ZIP has SHA-256 `aa52b68f88c4bb552187a53ef4c5753fa178f6a36035a3771c5bc04e078487ac`.

`prepare_v3_fixtures.py` verifies both official archive hashes and documents these deterministic transformations.

`data/energy-efficiency.csv` is a normalized copy of:

> Tsanas, Athanasios; Xifara, Angeliki (2012), “Energy Efficiency”, UCI Machine Learning Repository, doi: 10.24432/C51307.

Source: https://archive.ics.uci.edu/dataset/242/energy+efficiency

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

`data/synchronous-machine.csv` is a normalized copy of:

> Synchronous Machine Data Set (2020), UCI Machine Learning Repository, doi: 10.24432/C5W32R.

Source: https://archive.ics.uci.edu/dataset/607/synchronous+machine+data+set

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

Changes made by the Ingot project for both v4 fixtures: assigned descriptive column names from the official metadata, added stable setting identifiers, converted the source formats to CSV, and retained all published controls and outcomes. No outcome was synthesized or imputed. `prepare_v4_fixtures.py` verifies the official archive hashes and the deterministic transformations.

`data/lnp3-formulations.csv` is a normalized copy of the LNP3 dataset in:

> The Matter Lab, “Olympus: a benchmarking framework for noisy optimization and experiment planning”, pinned revision `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`.

Source: https://github.com/the-matter-lab/olympus/tree/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/src/olympus/datasets/dataset_lnp3

License: [MIT](https://github.com/the-matter-lab/olympus/blob/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/LICENSE)

Changes made by the Ingot project: added stable setting identifiers and descriptive headers to the source's headerless rows. All 768 settings, the three solid-lipid identities, four formulation inputs, and three measured outcomes are unchanged. No outcome was synthesized, imputed, averaged, or removed. The pinned source CSV has SHA-256 `69e8847e30f8b8b8720884676cd20d354152b7093309d278ee9910f9924b48ba`; `prepare_v6_fixture.py` verifies the checksum and the complete 3 × 4⁴ factorial structure before writing the fixture.

`data/oer-plate-3496.csv`, `data/oer-plate-3851.csv`, `data/oer-plate-3860.csv`, and `data/oer-plate-4098.csv` are normalized copies of four OER composition-screen datasets in:

> The Matter Lab, “Olympus: a benchmarking framework for noisy optimization and experiment planning”, pinned revision `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`.

The physical screens and their Olympus benchmark representation are documented in the source repository and in *Olympus, enhanced: benchmarking mixed-parameter and multi-objective optimization in chemistry and materials science*.

Source: https://github.com/the-matter-lab/olympus/tree/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/src/olympus/datasets

License: [MIT](https://github.com/the-matter-lab/olympus/blob/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/LICENSE)

Changes made by the Ingot project: added stable setting identifiers and descriptive headers to the four headerless source files. The six elemental fractions and measured OER overpotential values are unchanged. No row or outcome was synthesized, imputed, averaged, removed, or replaced by emulator output. The pinned source CSV SHA-256 values are `3c70049ccfdd11bc05d1777421fc4c724d2b2d4a86c12b8759079609912cfade`, `e2212be9cc5c866fa98dcb9513fca63946003f317688cb025bd0d648d8c3caab`, `834e2832818900e5cefa9de3b433e2246424faa1b2c3c460a1daf0707710fc90`, and `a3e4b4b781e3a04f861d062e773ce64d543118aa8ee9ccfc1aa4612502070b12`, respectively. `prepare_v7_fixtures.py` verifies checksums, finite values, simplex sums, the 10 at% grid, support size, and unique compositions before writing the fixtures.

`data/fullerenes-source.csv` and `data/suzuki-source.csv` are pinned source snapshots of the Buckminsterfullerene flow-reaction and Suzuki coupling datasets in:

> The Matter Lab, “Olympus: a benchmarking framework for noisy optimization and experiment planning”, pinned revision `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`.

Sources: https://github.com/the-matter-lab/olympus/tree/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/src/olympus/datasets/dataset_fullerenes and https://github.com/the-matter-lab/olympus/tree/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/src/olympus/datasets/dataset_suzuki

License: [MIT](https://github.com/the-matter-lab/olympus/blob/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/LICENSE)

Changes made by the Ingot project: `data/fullerenes.csv` averages only exact repeated control settings, retaining replicate counts and sample standard deviations; its 216 unique settings reconcile to all 246 source rows. `data/suzuki.csv` adds stable setting identifiers and replicate metadata to the 247 unique source settings. Controls and measured outcomes are otherwise unchanged; no outcome was synthesized or imputed. Source SHA-256 values are `87aa0927f0180a0f7d46dffb0b707df5caccc879492dbc0688ac3252414d4441` and `88e3c2613ee6238300f3b326c34d14dc3f76f0335a3e193cf423750146c819b6`; normalized fixture hashes are `24ba7b2657913d20d268aa0a521edf3e6a1e6ea98b0f0990e681b55e2794b787` and `704e9303a40c9014078f1618a8a824a90d009da61cfb1cb946472310be2983f3`. `prepare_unseen_fixtures.py` documents and verifies the deterministic transformation.

`data/alkox-source.csv`, `data/p3ht-source.csv`, and `data/hplc-source.csv` are pinned source snapshots of the Alkox enzyme-catalysis, P3HT conductive-formulation, and HPLC injection-process datasets from the same Olympus revision and MIT license.

Sources: https://github.com/the-matter-lab/olympus/tree/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/src/olympus/datasets/dataset_alkox, https://github.com/the-matter-lab/olympus/tree/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/src/olympus/datasets/dataset_p3ht, and https://github.com/the-matter-lab/olympus/tree/440b6b58ebfcaa2391cff7e94b570fb4fda98d68/src/olympus/datasets/dataset_hplc

Changes made by the Ingot project: exact repeated control settings are averaged while replicate count and sample standard deviation are retained. The resulting fixtures contain 104, 178, and 1,007 unique settings and reconcile to all 208, 178, and 1,386 source rows. No measured outcome was synthesized, imputed, deleted, or replaced. Source SHA-256 values are `133ff07b39a05c21be3d22ad18d14eee73fe0a1a75f95814a68b4591a042be22`, `be832eb97e1e18f49766733ca5865718b57819fdaf4e6fcaa2f53873360838c8`, and `9c94222798229c1391f75445f44d9c0ed285e83c1b1e0608ab76b28bf05decef`; normalized fixture hashes are `66c8f068474646385cee5dfe95f0194df16ed354b882502bdf280dac4f379fa0`, `34637bf1bb504b71152542004b2f40aba53f0f6beaf821c8084736ea1ce6cb1a`, and `ae16a9939e05775a9d7df573a08cd380bb1b61bb675c186765e0cc9008dba17a`. `prepare_acceptance_fixtures.py` documents and verifies the transformation.
