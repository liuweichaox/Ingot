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
