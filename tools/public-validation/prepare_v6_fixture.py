#!/usr/bin/env python3
"""Create the deterministic v6 LNP3 fixture from the preregistered source."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import csv
import hashlib
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parent
SOURCE_SHA256 = "69e8847e30f8b8b8720884676cd20d354152b7093309d278ee9910f9924b48ba"
CONTEXTS = ("Compritol_888", "Glyceryl_monostearate", "Stearic_acid")
LEVELS = (
    (6.0, 12.0, 24.0, 48.0),
    (72.0, 96.0, 108.0, 120.0),
    (0.0, 12.0, 24.0, 48.0),
    (0.0, 0.0025, 0.005, 0.01),
)
COLUMNS = (
    "drug_input",
    "solid_lipid",
    "solid_lipid_input",
    "liquid_lipid_input",
    "surfactant_input",
    "drug_loading",
    "encapsulation_efficiency",
    "particle_diameter",
)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_rows(path: Path) -> list[list[str]]:
    if sha256(path) != SOURCE_SHA256:
        raise ValueError("LNP3 source checksum mismatch")
    with path.open(encoding="utf-8", newline="") as stream:
        rows = list(csv.reader(stream))
    if len(rows) != 768 or Counter(map(len, rows)) != {8: 768}:
        raise ValueError("LNP3 must contain exactly 768 rows and eight columns")

    grouped: dict[str, list[tuple[float, ...]]] = defaultdict(list)
    identities = set()
    for index, row in enumerate(rows):
        context = row[1]
        if context not in CONTEXTS:
            raise ValueError(f"unexpected LNP3 context at row {index}")
        numeric = tuple(float(value) for value in (row[:1] + row[2:]))
        if not all(math.isfinite(value) for value in numeric):
            raise ValueError(f"non-finite LNP3 value at row {index}")
        controls = (numeric[0], numeric[1], numeric[2], numeric[3])
        identity = (context, *controls)
        if identity in identities:
            raise ValueError("duplicate LNP3 control-context setting")
        identities.add(identity)
        grouped[context].append(controls)

    for context in CONTEXTS:
        controls = grouped[context]
        if len(controls) != 256:
            raise ValueError(f"{context} must contain exactly 256 settings")
        for column, expected in enumerate(LEVELS):
            if tuple(sorted({row[column] for row in controls})) != expected:
                raise ValueError(f"{context} control levels changed at column {column}")
        expected_grid = 1
        for levels in LEVELS:
            expected_grid *= len(levels)
        if len(set(controls)) != expected_grid:
            raise ValueError(f"{context} is not a complete 4^4 factorial grid")
    return rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    args = parser.parse_args()
    rows = read_rows(args.source)
    output = ROOT / "data" / "lnp3-formulations.csv"
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(("setting_id", *COLUMNS))
        for index, row in enumerate(rows, start=1):
            writer.writerow((f"lnp3-{index:04d}", *row))
    print(f"lnp3: {output} ({sha256(output)})")


if __name__ == "__main__":
    main()
