#!/usr/bin/env python3
"""Build deterministic unseen-acceptance fixtures from pinned Olympus snapshots."""
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
    "fullerenes": {
        "path": DATA / "fullerenes-source.csv",
        "sha256": "87aa0927f0180a0f7d46dffb0b707df5caccc879492dbc0688ac3252414d4441",
        "columns": ("reaction_time", "sultine", "temperature", "product"),
        "expected_rows": 246,
        "expected_unique": 216,
        "fixture": DATA / "fullerenes.csv",
    },
    "suzuki": {
        "path": DATA / "suzuki-source.csv",
        "sha256": "88e3c2613ee6238300f3b326c34d14dc3f76f0335a3e193cf423750146c819b6",
        "columns": ("temperature", "pd_mol", "arbpin", "k3po4", "yield"),
        "expected_rows": 247,
        "expected_unique": 247,
        "fixture": DATA / "suzuki.csv",
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
    """Read one headerless pinned source and enforce its immutable contract."""
    settings = SOURCES[name]
    path = settings["path"]
    if sha256(path) != settings["sha256"]:
        raise ValueError(f"{name} source checksum mismatch")
    rows = []
    with path.open(encoding="utf-8", newline="") as stream:
        for index, row in enumerate(csv.reader(stream), start=1):
            if len(row) != len(settings["columns"]):
                raise ValueError(f"{name} row {index} has unexpected width")
            values = tuple(float(value) for value in row)
            if not all(math.isfinite(value) for value in values):
                raise ValueError(f"{name} row {index} contains a non-finite value")
            rows.append(values)
    if len(rows) != settings["expected_rows"]:
        raise ValueError(f"{name} source row count changed")
    return rows


def build_fixture(name: str) -> list[dict[str, object]]:
    """Aggregate exact control replicates and retain their count and spread."""
    settings = SOURCES[name]
    columns = settings["columns"]
    controls = columns[:-1]
    outcome = columns[-1]
    grouped: dict[tuple[float, ...], list[float]] = defaultdict(list)
    for row in read_source(name):
        grouped[row[:-1]].append(row[-1])
    if len(grouped) != settings["expected_unique"] or len(grouped) < 150:
        raise ValueError(f"{name} unique-setting count failed the preregistered gate")

    records = []
    for index, (params, outcomes) in enumerate(sorted(grouped.items()), start=1):
        record: dict[str, object] = {
            "setting_id": f"{name}-{index:03d}",
            **dict(zip(controls, params)),
            outcome: statistics.fmean(outcomes),
            "replicate_count": len(outcomes),
            "replicate_sample_std": (
                statistics.stdev(outcomes) if len(outcomes) > 1 else 0.0
            ),
        }
        records.append(record)
    return records


def write_fixture(name: str) -> None:
    """Write one normalized fixture with stable ordering and column names."""
    settings = SOURCES[name]
    records = build_fixture(name)
    fields = (
        "setting_id",
        *settings["columns"],
        "replicate_count",
        "replicate_sample_std",
    )
    with settings["fixture"].open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, lineterminator="\n")
        writer.writeheader()
        writer.writerows(records)


def main() -> None:
    """Build and report both preregistered fixtures."""
    for name in SOURCES:
        write_fixture(name)
        path = SOURCES[name]["fixture"]
        print(f"{name}: {path.relative_to(ROOT)} sha256={sha256(path)}")


if __name__ == "__main__":
    main()
