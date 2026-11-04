#!/usr/bin/env python3
"""Regenerate fixtures into a new directory; run with uv run."""
import json
from pathlib import Path
import sys

import lance
import pyarrow as pa
import typed_args as ta

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "examples/python"))
from prepare_dataset import prepare


def rich_table() -> pa.Table:
    schema = pa.schema([
        ("sample_id", pa.int64()),
        ("payload", pa.binary()),
        pa.field("annotation", pa.struct([("label", pa.string()), ("bbox", pa.list_(pa.float32()))]), nullable=False),
    ])
    return pa.Table.from_pylist([
        {"sample_id": 1, "payload": b"\x00\xff\x10", "annotation": {"label": "猫", "bbox": [1.0, 2.0, 3.0, 4.0]}},
        {"sample_id": 2, "payload": None, "annotation": {"label": None, "bbox": None}},
    ], schema=schema)


def main(output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("Output must be an empty directory; existing fixtures are never overwritten")
    release = prepare(str(output / "training.lance"))
    (output / "release.json").write_text(json.dumps(release, indent=2), encoding="utf-8")
    lance.write_dataset(pa.table({"sample_id": pa.array([], type=pa.int64())}), str(output / "empty.lance"), data_storage_version="2.0")
    search = lance.write_dataset(pa.table({"sample_id": [1, 2], "text": ["a striped tiger", "a flying bird"]}), str(output / "search.lance"), data_storage_version="2.0")
    search.create_scalar_index("text", "INVERTED")
    lance.write_dataset(rich_table(), str(output / "rich.lance"), data_storage_version="2.0")


class Args(ta.TypedArgs):
    model_config = ta.ParserConfig(description=__doc__)
    output: Path


if __name__ == "__main__":
    main(Args.parse_args().output)
