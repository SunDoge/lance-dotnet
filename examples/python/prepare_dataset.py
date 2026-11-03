"""Create reproducible training snapshots. pip install -r requirements.txt"""
import argparse
import json
from pathlib import Path

import lance
import pyarrow as pa


def prepare(uri: str, storage_options: dict[str, str] | None = None) -> dict:
    # Use a fresh dataset URI: creating an existing dataset intentionally fails.
    schema = pa.schema([
        pa.field("sample_id", pa.int64(), nullable=False),
        pa.field("payload_uri", pa.string()),
        pa.field("annotation", pa.string()),
        pa.field("split", pa.string()),
        pa.field("embedding", pa.list_(pa.float32(), 2)),
    ])
    initial = pa.Table.from_pylist([
        {"sample_id": 1, "payload_uri": "s3://raw/猫.jpg", "annotation": "cat", "split": "train", "embedding": [1.0, 0.0]},
        {"sample_id": 2, "payload_uri": "s3://raw/dog.jpg", "annotation": "dog", "split": "validation", "embedding": [0.0, 1.0]},
    ], schema=schema)
    ds = lance.write_dataset(initial, uri, mode="create", storage_options=storage_options, data_storage_version="2.0")
    first_version = ds.version
    ds.tags.create("initial", first_version)
    added = pa.Table.from_pylist([
        {"sample_id": 3, "payload_uri": "s3://raw/bird.jpg", "annotation": "bird", "split": "train", "embedding": [0.8, 0.2]},
    ], schema=schema)
    ds = lance.write_dataset(added, uri, mode="append", storage_options=storage_options, data_storage_version="2.0")
    appended_version = ds.version
    ds.update({"annotation": "'tiger'"}, where="sample_id = 1")
    ds.tags.create("training-release", ds.version)
    # C# uses the resolved numeric version; the native tag protects the snapshot.
    return {
        "dataset_uri": uri,
        "initial_version": first_version,
        "appended_version": appended_version,
        "version": ds.version,
        "tag": "training-release",
        "filter": "split = 'train'",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("uri")
    parser.add_argument("--manifest", default="release.json")
    parser.add_argument("--storage-options", help="Path to a JSON dictionary of endpoint/region options; prefer environment credentials")
    args = parser.parse_args()
    options = json.loads(Path(args.storage_options).read_text()) if args.storage_options else None
    release = prepare(args.uri, options)
    Path(args.manifest).write_text(json.dumps(release, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Created release version {release['version']} ({release['tag']})")
