#!/usr/bin/env python3
"""Fetch an unmodified pinned upstream native library and verify its release checksum."""
import argparse
import hashlib
import os
import shutil
from pathlib import Path
import tarfile
import tempfile
import urllib.request

VERSION = "0.1.9"
TARGETS = {
    "linux-x64": ("x86_64-unknown-linux-gnu-debian12", "liblance_c.so"),
    "linux-arm64": ("aarch64-unknown-linux-gnu-debian12", "liblance_c.so"),
    "osx-arm64": ("aarch64-apple-darwin", "liblance_c.dylib"),
}
ROOT = Path(__file__).resolve().parents[1]


def fetch(rid: str) -> None:
    target, library = TARGETS[rid]
    asset = f"lance-c-v{VERSION}-{target}.tar.xz"
    base = f"https://github.com/lance-format/lance-c/releases/download/v{VERSION}"
    with urllib.request.urlopen(f"{base}/{asset}.sha256") as response:
        expected = response.read().decode().split()[0].lower()
    if len(expected) != 64 or any(c not in "0123456789abcdef" for c in expected):
        raise ValueError("Invalid upstream SHA256 checksum")
    with tempfile.TemporaryDirectory() as tmp:
        archive = Path(tmp) / asset
        urllib.request.urlretrieve(f"{base}/{asset}", archive)
        with archive.open("rb") as input_file:
            actual = hashlib.file_digest(input_file, "sha256").hexdigest()
        if actual != expected:
            raise ValueError(f"Checksum mismatch for {asset}")
        destination = ROOT / "src/Lance/runtimes" / rid / "native"
        destination.mkdir(parents=True, exist_ok=True)
        with tarfile.open(archive, "r:xz") as tar:
            # Extract only the expected library, never arbitrary archive paths.
            candidates = [m for m in tar.getmembers() if m.isfile() and m.name.lstrip("./") == f"lib/{library}"]
            if len(candidates) != 1:
                raise ValueError(f"Expected exactly one lib/{library}")
            contents = tar.extractfile(candidates[0])
            if contents is None:
                raise ValueError("Missing native library contents")
            staged = Path(tmp) / library
            with staged.open("wb") as output:
                shutil.copyfileobj(contents, output)
            # Copy alongside the destination so replacement is atomic across filesystems.
            pending = destination / (library + ".tmp")
            shutil.copyfile(staged, pending)
            os.replace(pending, destination / library)
    print(f"Installed lance-c {VERSION} for {rid}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=TARGETS, required=True)
    fetch(parser.parse_args().rid)
