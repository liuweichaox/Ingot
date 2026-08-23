#!/usr/bin/env python3
"""Create the deterministic crossed-barrel fixture from the published CSV.

The source contains 600 distinct designs tested in triplicate. This converter
verifies the source snapshot, aggregates only exact design replicates, and
retains the replicate count and sample standard deviation.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import math
import statistics
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parent
SOURCE_SHA256 = "2c01f875f3c210e986ca6142bf20f417884c2ad7d6f008c2fc574b44a3d5f606"
SOURCE_ROWS = 1800
EXPECTED_SETTINGS = 600


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def convert(source: Path) -> Path:
    actual_hash = sha256(source)
    if actual_hash != SOURCE_SHA256:
        raise ValueError(
            f"source checksum mismatch: expected {SOURCE_SHA256}, got {actual_hash}"
        )
    with source.open(encoding="utf-8", newline="") as stream:
        rows = list(csv.DictReader(stream))
    if len(rows) != SOURCE_ROWS:
        raise ValueError(f"unexpected source row count: {len(rows)}")
    if not rows or set(rows[0]) != {"n", "theta", "r", "t", "toughness"}:
        raise ValueError("unexpected source columns")

    replicates: dict[tuple[float, float, float, float], list[float]] = defaultdict(list)
    for row in rows:
        key = tuple(float(row[name]) for name in ("n", "theta", "r", "t"))
        toughness = float(row["toughness"])
        if any(not math.isfinite(value) for value in (*key, toughness)):
            raise ValueError("source contains a non-finite value")
        replicates[key].append(toughness)
    if len(replicates) != EXPECTED_SETTINGS:
        raise ValueError(f"unexpected unique setting count: {len(replicates)}")
    if any(len(values) != 3 for values in replicates.values()):
        raise ValueError("every crossed-barrel design must have exactly three replicates")

    output = ROOT / "data" / "crossed-barrel.csv"
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(
            (
                "setting_id",
                "column_count",
                "twist_angle_deg",
                "outer_radius_mm",
                "wall_thickness_mm",
                "toughness_j",
                "replicate_count",
                "toughness_std_j",
            )
        )
        for index, (setting, values) in enumerate(sorted(replicates.items()), start=1):
            column_count, angle, radius, thickness = setting
            writer.writerow(
                (
                    f"crossed-barrel-{index:04d}",
                    f"{column_count:g}",
                    f"{angle:g}",
                    f"{radius:g}",
                    f"{thickness:g}",
                    repr(statistics.fmean(values)),
                    len(values),
                    repr(statistics.stdev(values)),
                )
            )
    return output


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    args = parser.parse_args()
    output = convert(args.source)
    print(f"crossed_barrel: {output} ({sha256(output)})")


if __name__ == "__main__":
    main()
