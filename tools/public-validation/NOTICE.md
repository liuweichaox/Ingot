# Dataset attribution

`data/fdm-doe-grid.csv` is a normalized subset of:

> Aktepe, Elif; Ergün, Uçman (2026), “FDM 3D Printing Dataset: Printer Type, Materials, Process Parameters, and Tensile, Hardness and Roughness Test Results”, Mendeley Data, V2, doi: 10.17632/zd6td6svd6.2.

Source: https://data.mendeley.com/datasets/zd6td6svd6/2

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

Changes made by the Ingot project: selected the six complete closed-printer PLA+/PETG × grid/triangles/zigzag 3×3×3 DOE grids; renamed columns; normalized categorical labels; retained only the controls and outcomes used by the public benchmark. No outcome values were synthesized or imputed.

`data/concrete-strength.csv` is a normalized copy of:

> Yeh, I-Cheng, “Concrete Compressive Strength”, UCI Machine Learning Repository, doi: 10.24432/C5PK67.

Source: https://archive.ics.uci.edu/dataset/165/concrete+compressive+strength

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

Changes made by the Ingot project: renamed columns, sorted records by curing age and mixture, and aggregated identical age-plus-mixture records to one candidate setting. The committed fixture retains the mean strength, replicate count, and sample standard deviation. Its 996 unique settings reconcile to all 1,030 source rows. No outcome was synthesized or imputed. The official ZIP used for normalization has SHA-256 `dad85d14de8aee4e07479daa774e6b569a313715b71a3b92c95a07cf91c2c9a7`; `prepare_concrete_fixture.py` documents the deterministic transformation.

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
