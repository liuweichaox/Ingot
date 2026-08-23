#!/usr/bin/env python3
"""Create deterministic v4 fixtures from the preregistered UCI archives.

The converter verifies archive hashes and structural metadata before writing
CSV snapshots. It does not calculate targets, fit models, or run evaluation.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
from io import BytesIO
import math
from pathlib import Path
import re
from xml.etree import ElementTree
import zipfile


ROOT = Path(__file__).resolve().parent
ENERGY_ARCHIVE_SHA256 = (
    "499441eee27929a4b00417f58fd8c63c9cc14b8a71520cd0dd27fcb626738351"
)
SYNCHRONOUS_ARCHIVE_SHA256 = (
    "e34f66b956cd086facbdaabb5587a6d26f513e9eacaf73ff5611710464f05580"
)
ENERGY_COLUMNS = (
    "relative_compactness",
    "surface_area",
    "wall_area",
    "roof_area",
    "overall_height",
    "orientation",
    "glazing_area",
    "glazing_distribution",
    "heating_load",
    "cooling_load",
)
SYNCHRONOUS_COLUMNS = (
    "load_current",
    "power_factor",
    "power_factor_error",
    "excitation_current_change",
    "excitation_current",
)
XML_NAMESPACE = {
    "main": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_archive(path: Path, expected: str, name: str) -> None:
    actual = sha256(path)
    if actual != expected:
        raise ValueError(
            f"{name} archive checksum mismatch: expected {expected}, got {actual}"
        )


def column_index(reference: str) -> int:
    match = re.match(r"[A-Z]+", reference)
    if match is None:
        raise ValueError(f"invalid spreadsheet cell reference: {reference}")
    value = 0
    for character in match.group(0):
        value = value * 26 + ord(character) - ord("A") + 1
    return value - 1


def read_energy_rows(archive: Path) -> list[list[float]]:
    require_archive(archive, ENERGY_ARCHIVE_SHA256, "energy-efficiency")
    with zipfile.ZipFile(archive) as bundle:
        workbook_bytes = bundle.read("ENB2012_data.xlsx")
    with zipfile.ZipFile(BytesIO(workbook_bytes)) as workbook:
        shared_root = ElementTree.fromstring(
            workbook.read("xl/sharedStrings.xml")
        )
        shared = [
            "".join(item.itertext())
            for item in shared_root.findall("main:si", XML_NAMESPACE)
        ]
        sheet_root = ElementTree.fromstring(
            workbook.read("xl/worksheets/sheet1.xml")
        )

    records: list[list[str | None]] = []
    for row in sheet_root.findall(
        ".//main:sheetData/main:row", XML_NAMESPACE
    ):
        values: list[str | None] = [None] * len(ENERGY_COLUMNS)
        for cell in row.findall("main:c", XML_NAMESPACE):
            index = column_index(cell.attrib["r"])
            if index >= len(values):
                continue
            value_node = cell.find("main:v", XML_NAMESPACE)
            if value_node is None or value_node.text is None:
                continue
            value = value_node.text
            if cell.attrib.get("t") == "s":
                value = shared[int(value)]
            values[index] = value
        if all(value is not None for value in values):
            records.append(values)

    expected_header = tuple(f"X{index}" for index in range(1, 9)) + ("Y1", "Y2")
    if not records or tuple(records[0]) != expected_header:
        raise ValueError("energy-efficiency workbook has an unexpected header")
    numeric = [[float(str(value)) for value in row] for row in records[1:]]
    validate_rows(numeric, expected_rows=768, control_count=8, name="energy")
    return numeric


def read_synchronous_rows(archive: Path) -> list[list[float]]:
    require_archive(archive, SYNCHRONOUS_ARCHIVE_SHA256, "synchronous-machine")
    with zipfile.ZipFile(archive) as bundle:
        raw = bundle.read("synchronous machine.csv").decode("utf-8-sig")
    records = list(csv.reader(raw.splitlines(), delimiter=";"))
    if not records or tuple(records[0]) != ("Iy", "PF", "e", "dIf", "If"):
        raise ValueError("synchronous-machine archive has an unexpected header")
    numeric = [
        [float(value.replace(",", ".")) for value in row]
        for row in records[1:]
        if row
    ]
    validate_rows(
        numeric,
        expected_rows=557,
        control_count=4,
        name="synchronous-machine",
    )
    return numeric


def validate_rows(
    rows: list[list[float]], *, expected_rows: int, control_count: int, name: str
) -> None:
    if len(rows) != expected_rows:
        raise ValueError(f"unexpected {name} row count: {len(rows)}")
    if any(not all(math.isfinite(value) for value in row) for row in rows):
        raise ValueError(f"{name} contains a non-finite value")
    controls = [tuple(row[:control_count]) for row in rows]
    if len(controls) != len(set(controls)):
        raise ValueError(f"{name} contains duplicate control settings")


def render(value: float) -> str:
    return format(value, ".15g")


def write_fixture(name: str, columns: tuple[str, ...], rows: list[list[float]]) -> Path:
    output = ROOT / "data" / f"{name}.csv"
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(("setting_id", *columns))
        for index, row in enumerate(rows, start=1):
            writer.writerow(
                (f"{name}-{index:04d}", *(render(value) for value in row))
            )
    return output


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--energy-archive", type=Path, required=True)
    parser.add_argument("--synchronous-archive", type=Path, required=True)
    args = parser.parse_args()
    fixtures = (
        (
            "energy-efficiency",
            ENERGY_COLUMNS,
            read_energy_rows(args.energy_archive),
        ),
        (
            "synchronous-machine",
            SYNCHRONOUS_COLUMNS,
            read_synchronous_rows(args.synchronous_archive),
        ),
    )
    for name, columns, rows in fixtures:
        output = write_fixture(name, columns, rows)
        print(f"{name}: {output} ({sha256(output)})")


if __name__ == "__main__":
    main()
