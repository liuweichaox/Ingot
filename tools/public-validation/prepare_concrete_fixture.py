#!/usr/bin/env python3
"""Normalize the official UCI Concrete Strength XLS fixture into replay CSV.

This one-time provenance utility intentionally keeps ``xlrd`` optional. The
committed CSV is the benchmark input and is protected by a checksum in the
protocol; rerunning this converter requires ``pandas`` and ``xlrd``.
"""
from __future__ import annotations

import argparse
import csv
from pathlib import Path

import pandas as pd


COLUMNS = {
    "Cement (component 1)(kg in a m^3 mixture)": "cement_kg_m3",
    "Blast Furnace Slag (component 2)(kg in a m^3 mixture)": "slag_kg_m3",
    "Fly Ash (component 3)(kg in a m^3 mixture)": "fly_ash_kg_m3",
    "Water  (component 4)(kg in a m^3 mixture)": "water_kg_m3",
    "Superplasticizer (component 5)(kg in a m^3 mixture)": "superplasticizer_kg_m3",
    "Coarse Aggregate  (component 6)(kg in a m^3 mixture)": "coarse_aggregate_kg_m3",
    "Fine Aggregate (component 7)(kg in a m^3 mixture)": "fine_aggregate_kg_m3",
    "Age (day)": "age_days",
    "Concrete compressive strength(MPa, megapascals) ": "compressive_strength_mpa",
}
CONTROL_FIELDS = tuple(list(COLUMNS.values())[:7])


def render_number(value: float) -> str:
    return format(float(value), ".12g")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    frame = pd.read_excel(args.source).rename(columns=COLUMNS)
    if tuple(frame.columns) != tuple(COLUMNS.values()):
        raise ValueError("unexpected UCI Concrete Strength source columns")
    if frame.isna().any().any() or len(frame) != 1030:
        raise ValueError("expected 1030 complete source rows")

    keys = ["age_days", *CONTROL_FIELDS]
    grouped = (
        frame.groupby(keys, as_index=False)["compressive_strength_mpa"]
        .agg(["mean", "std", "count"])
        .reset_index()
        .sort_values(keys, kind="stable")
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    fields = [
        "setting_id",
        "age_days",
        *CONTROL_FIELDS,
        "compressive_strength_mpa",
        "replicate_count",
        "replicate_std_mpa",
    ]
    with args.output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, lineterminator="\n")
        writer.writeheader()
        for index, row in enumerate(grouped.itertuples(index=False), start=1):
            record = {field: render_number(getattr(row, field)) for field in keys}
            record.update(
                {
                    "setting_id": f"concrete-{index:04d}",
                    "compressive_strength_mpa": render_number(row.mean),
                    "replicate_count": str(int(row.count)),
                    "replicate_std_mpa": (
                        render_number(row.std) if row.count > 1 else ""
                    ),
                }
            )
            writer.writerow(record)


if __name__ == "__main__":
    main()
