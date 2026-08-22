#!/usr/bin/env python3
"""Create deterministic v3 evaluation fixtures from official UCI archives.

The converter verifies the downloaded archives before reading them. It does
not calculate targets, fit models, or run the evaluation protocol.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import math
from pathlib import Path
import zipfile


ROOT = Path(__file__).resolve().parent
ARCHIVES = {
    "airfoil": {
        "sha256": "5c7767ba53ad827d3f48ba1eb9434117f4892df8f10bc4c99e118a9e8a7ae07c",
        "member": "airfoil_self_noise.dat",
        "columns": (
            "frequency_hz",
            "attack_angle_deg",
            "chord_length_m",
            "free_stream_velocity_m_s",
            "displacement_thickness_m",
            "sound_pressure_db",
        ),
        "output": "airfoil-self-noise.csv",
        "expected_rows": 1503,
    },
    "yacht": {
        "sha256": "aa52b68f88c4bb552187a53ef4c5753fa178f6a36035a3771c5bc04e078487ac",
        "member": "yacht_hydrodynamics.data",
        "columns": (
            "longitudinal_buoyancy_position",
            "prismatic_coefficient",
            "length_displacement_ratio",
            "beam_draught_ratio",
            "length_beam_ratio",
            "froude_number",
            "residuary_resistance",
        ),
        "output": "yacht-hydrodynamics.csv",
        "expected_rows": 308,
    },
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def convert(name: str, archive: Path) -> Path:
    specification = ARCHIVES[name]
    actual_hash = sha256(archive)
    if actual_hash != specification["sha256"]:
        raise ValueError(
            f"{name} archive checksum mismatch: expected "
            f"{specification['sha256']}, got {actual_hash}"
        )
    with zipfile.ZipFile(archive) as bundle:
        raw_lines = bundle.read(str(specification["member"])).decode("utf-8").splitlines()
    rows = [line.split() for line in raw_lines if line.strip()]
    columns = tuple(str(value) for value in specification["columns"])
    if len(rows) != int(specification["expected_rows"]):
        raise ValueError(f"unexpected {name} row count: {len(rows)}")
    if any(len(row) != len(columns) for row in rows):
        raise ValueError(f"{name} contains a row with an unexpected column count")
    numeric_rows = [[float(value) for value in row] for row in rows]
    if any(not math.isfinite(value) for row in numeric_rows for value in row):
        raise ValueError(f"{name} contains a non-finite value")
    control_keys = [tuple(row[:-1]) for row in numeric_rows]
    if len(control_keys) != len(set(control_keys)):
        raise ValueError(f"{name} contains duplicate control settings")

    output = ROOT / "data" / str(specification["output"])
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(("setting_id", *columns))
        for index, row in enumerate(rows, start=1):
            writer.writerow((f"{name}-{index:04d}", *row))
    return output


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--airfoil-archive", type=Path, required=True)
    parser.add_argument("--yacht-archive", type=Path, required=True)
    args = parser.parse_args()
    for name, archive in (
        ("airfoil", args.airfoil_archive),
        ("yacht", args.yacht_archive),
    ):
        output = convert(name, archive)
        print(f"{name}: {output} ({sha256(output)})")


if __name__ == "__main__":
    main()
