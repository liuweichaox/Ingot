#!/usr/bin/env python3
"""Normalize the preregistered fresh-data acceptance snapshots."""
from __future__ import annotations

from collections import defaultdict
import csv
import hashlib
import math
from pathlib import Path
import statistics


ROOT = Path(__file__).resolve().parent
DATA = ROOT / "data"
SOURCES = {
    "alkox": {
        "source": DATA / "alkox-source.csv",
        "fixture": DATA / "alkox.csv",
        "sha256": "133ff07b39a05c21be3d22ad18d14eee73fe0a1a75f95814a68b4591a042be22",
        "columns": ("catalase", "peroxidase", "alcohol_oxidase", "ph", "conversion"),
        "rows": 208,
        "unique": 104,
    },
    "p3ht": {
        "source": DATA / "p3ht-source.csv",
        "fixture": DATA / "p3ht.csv",
        "sha256": "be832eb97e1e18f49766733ca5865718b57819fdaf4e6fcaa2f53873360838c8",
        "columns": (
            "p3ht_content",
            "d1_content",
            "d2_content",
            "d6_content",
            "d8_content",
            "conductivity",
        ),
        "rows": 178,
        "unique": 178,
    },
    "hplc": {
        "source": DATA / "hplc-source.csv",
        "fixture": DATA / "hplc.csv",
        "sha256": "9c94222798229c1391f75445f44d9c0ed285e83c1b1e0608ab76b28bf05decef",
        "columns": (
            "sample_loop",
            "additional_volume",
            "tubing_volume",
            "sample_flow",
            "push_speed",
            "wait_time",
            "peak_area",
        ),
        "rows": 1386,
        "unique": 1007,
    },
}


def sha256(path: Path) -> str:
    """Return the hexadecimal SHA-256 digest of a file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_source(name: str) -> list[tuple[float, ...]]:
    """Read one pinned headerless source and enforce its immutable shape."""
    settings = SOURCES[name]
    if sha256(settings["source"]) != settings["sha256"]:
        raise ValueError(f"{name} source checksum mismatch")
    rows = []
    with settings["source"].open(encoding="utf-8", newline="") as stream:
        for index, row in enumerate(csv.reader(stream), start=1):
            if len(row) != len(settings["columns"]):
                raise ValueError(f"{name} row {index} has unexpected width")
            values = tuple(float(value) for value in row)
            if not all(math.isfinite(value) for value in values):
                raise ValueError(f"{name} row {index} contains a non-finite value")
            rows.append(values)
    if len(rows) != settings["rows"]:
        raise ValueError(f"{name} source row count changed")
    return rows


def build_fixture(name: str) -> list[dict[str, object]]:
    """Aggregate exact control replicates without dropping measured outcomes."""
    settings = SOURCES[name]
    columns = settings["columns"]
    controls = columns[:-1]
    outcome = columns[-1]
    grouped: dict[tuple[float, ...], list[float]] = defaultdict(list)
    for row in read_source(name):
        grouped[row[:-1]].append(row[-1])
    if len(grouped) != settings["unique"] or len(grouped) < 80:
        raise ValueError(f"{name} unique-setting gate failed")

    records = []
    for index, (params, outcomes) in enumerate(sorted(grouped.items()), start=1):
        records.append(
            {
                "setting_id": f"{name}-{index:04d}",
                **dict(zip(controls, params)),
                outcome: statistics.fmean(outcomes),
                "replicate_count": len(outcomes),
                "replicate_sample_std": (
                    statistics.stdev(outcomes) if len(outcomes) > 1 else 0.0
                ),
            }
        )
    return records


def write_fixture(name: str, records: list[dict[str, object]]) -> None:
    """Write one deterministic normalized fixture."""
    path = SOURCES[name]["fixture"]
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(records[0]))
        writer.writeheader()
        writer.writerows(records)


def main() -> None:
    for name in SOURCES:
        records = build_fixture(name)
        write_fixture(name, records)
        controls = SOURCES[name]["columns"][:-1]
        ranges = {
            control: [
                min(float(row[control]) for row in records),
                max(float(row[control]) for row in records),
            ]
            for control in controls
        }
        print(
            f"{name}: rows={len(records)} sha256={sha256(SOURCES[name]['fixture'])} "
            f"ranges={ranges}"
        )


if __name__ == "__main__":
    main()
