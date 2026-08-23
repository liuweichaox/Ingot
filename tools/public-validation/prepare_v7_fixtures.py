#!/usr/bin/env python3
"""Validate and snapshot the preregistered Olympus OER plate data for v7.

This converter performs only source-integrity and control-space checks. It does
not calculate thresholds, inspect outcome distributions, fit models, or run an
optimization policy.
"""
from __future__ import annotations

import argparse
import csv
from dataclasses import dataclass
import hashlib
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parent


@dataclass(frozen=True)
class Plate:
    name: str
    source_directory: str
    controls: tuple[str, ...]
    rows: int
    source_sha256: str


PLATES = (
    Plate(
        name="oer-plate-3496",
        source_directory="dataset_oer_plate_3496",
        controls=(
            "ni_load",
            "fe_load",
            "co_load",
            "mn_load",
            "ce_load",
            "la_load",
        ),
        rows=2121,
        source_sha256=(
            "3c70049ccfdd11bc05d1777421fc4c724d2b2d4a86c12b8759079609912cfade"
        ),
    ),
    Plate(
        name="oer-plate-3851",
        source_directory="dataset_oer_plate_3851",
        controls=(
            "ni_load",
            "fe_load",
            "co_load",
            "ta_load",
            "mn_load",
            "cu_load",
        ),
        rows=2119,
        source_sha256=(
            "e2212be9cc5c866fa98dcb9513fca63946003f317688cb025bd0d648d8c3caab"
        ),
    ),
    Plate(
        name="oer-plate-3860",
        source_directory="dataset_oer_plate_3860",
        controls=(
            "sn_load",
            "fe_load",
            "co_load",
            "ta_load",
            "mn_load",
            "cu_load",
        ),
        rows=2120,
        source_sha256=(
            "834e2832818900e5cefa9de3b433e2246424faa1b2c3c460a1daf0707710fc90"
        ),
    ),
    Plate(
        name="oer-plate-4098",
        source_directory="dataset_oer_plate_4098",
        controls=(
            "sn_load",
            "sb_load",
            "co_load",
            "ca_load",
            "ni_load",
            "mn_load",
        ),
        rows=2121,
        source_sha256=(
            "a3e4b4b781e3a04f861d062e773ce64d543118aa8ee9ccfc1aa4612502070b12"
        ),
    ),
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_source(path: Path, plate: Plate) -> list[list[float]]:
    actual_sha256 = sha256(path)
    if actual_sha256 != plate.source_sha256:
        raise ValueError(
            f"{plate.name} source checksum mismatch: expected "
            f"{plate.source_sha256}, got {actual_sha256}"
        )
    with path.open(encoding="utf-8", newline="") as stream:
        records = list(csv.reader(stream))
    if len(records) != plate.rows:
        raise ValueError(
            f"{plate.name} row count changed: expected {plate.rows}, "
            f"got {len(records)}"
        )
    if any(len(record) != 7 for record in records):
        raise ValueError(f"{plate.name} must contain six controls and one outcome")

    numeric = [[float(value) for value in record] for record in records]
    if any(not all(math.isfinite(value) for value in record) for record in numeric):
        raise ValueError(f"{plate.name} contains a non-finite value")

    identities: set[tuple[float, ...]] = set()
    for row_index, record in enumerate(numeric, start=1):
        controls = tuple(record[:6])
        if any(value < 0.0 or value > 1.0 for value in controls):
            raise ValueError(f"{plate.name} control outside [0, 1] at row {row_index}")
        if not math.isclose(sum(controls), 1.0, rel_tol=0.0, abs_tol=1e-8):
            raise ValueError(f"{plate.name} composition does not sum to one")
        if any(
            not math.isclose(value * 10.0, round(value * 10.0), abs_tol=1e-8)
            for value in controls
        ):
            raise ValueError(f"{plate.name} composition is outside the 10 at% grid")
        if sum(value > 1e-8 for value in controls) > 4:
            raise ValueError(f"{plate.name} contains a quinary or senary composition")
        if controls in identities:
            raise ValueError(f"{plate.name} contains a duplicate composition")
        identities.add(controls)
    return numeric


def render(value: float) -> str:
    return format(value, ".15g")


def write_fixture(plate: Plate, records: list[list[float]]) -> Path:
    output = ROOT / "data" / f"{plate.name}.csv"
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(("setting_id", *plate.controls, "overpotential"))
        for index, record in enumerate(records, start=1):
            writer.writerow(
                (
                    f"{plate.name}-{index:04d}",
                    *(render(value) for value in record),
                )
            )
    return output


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--source-root",
        type=Path,
        required=True,
        help="Path to the pinned Olympus src/olympus/datasets directory.",
    )
    args = parser.parse_args()

    for plate in PLATES:
        source = args.source_root / plate.source_directory / "data.csv"
        records = validate_source(source, plate)
        output = write_fixture(plate, records)
        print(f"{plate.name}: {output} ({sha256(output)})")


if __name__ == "__main__":
    main()
