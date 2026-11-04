#!/usr/bin/env python3
"""Fetch a pinned upstream native library and verify its release checksum."""
import hashlib
import os
from pathlib import Path
import shutil
import tarfile
import tempfile
from typing import Annotated, Literal

import httpx2
import typed_args as ta

VERSION = "0.1.9"
TARGETS = {
    "linux-x64": ("x86_64-unknown-linux-gnu-debian12", "liblance_c.so"),
    "linux-arm64": ("aarch64-unknown-linux-gnu-debian12", "liblance_c.so"),
    "osx-arm64": ("aarch64-apple-darwin", "liblance_c.dylib"),
}
ROOT = Path(__file__).resolve().parents[1]


class Args(ta.TypedArgs):
    model_config = ta.ParserConfig(description=__doc__)
    rid: Annotated[Literal["linux-x64", "linux-arm64", "osx-arm64"], ta.Arg("--rid", required=True)]


def fetch(rid: str) -> None:
    target, library = TARGETS[rid]
    asset = f"lance-c-v{VERSION}-{target}.tar.xz"
    base = f"https://github.com/lance-format/lance-c/releases/download/v{VERSION}"
    with httpx2.Client(follow_redirects=True, timeout=60) as client, tempfile.TemporaryDirectory() as tmp:
        checksum = client.get(f"{base}/{asset}.sha256")
        checksum.raise_for_status()
        parts = checksum.text.split()
        expected = parts[0].lower() if parts else ""
        if len(expected) != 64 or any(c not in "0123456789abcdef" for c in expected):
            raise ValueError("Invalid upstream SHA256 checksum")
        archive = Path(tmp) / asset
        digest = hashlib.sha256()
        with client.stream("GET", f"{base}/{asset}") as response, archive.open("wb") as output:
            response.raise_for_status()
            for chunk in response.iter_bytes(chunk_size=1024 * 1024):
                output.write(chunk)
                digest.update(chunk)
        if digest.hexdigest() != expected:
            raise ValueError(f"Checksum mismatch for {asset}")
        destination = ROOT / "src/Lance/runtimes" / rid / "native"
        destination.mkdir(parents=True, exist_ok=True)
        pending = destination / (library + ".tmp")
        try:
            with tarfile.open(archive, "r:xz") as tar:
                candidates = [m for m in tar.getmembers() if m.isfile() and m.name.removeprefix("./") == f"lib/{library}"]
                if len(candidates) != 1:
                    raise ValueError(f"Expected exactly one lib/{library}")
                contents = tar.extractfile(candidates[0])
                if contents is None:
                    raise ValueError("Missing native library contents")
                with contents, pending.open("wb") as output:
                    shutil.copyfileobj(contents, output)
            os.replace(pending, destination / library)
        finally:
            pending.unlink(missing_ok=True)
    print(f"Installed lance-c {VERSION} for {rid}")


if __name__ == "__main__":
    fetch(Args.parse_args().rid)
