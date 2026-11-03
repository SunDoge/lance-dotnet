#!/usr/bin/env python3
"""Write and read a Python-created dataset through a local Moto S3 endpoint.

Install tests/requirements-s3.txt in a Python environment before running.
This checks S3 transport and storage options, not a provider's concurrency guarantees.
"""
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

import boto3

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    endpoint = f"http://127.0.0.1:{port}"
    with tempfile.TemporaryFile() as logs:
        process = subprocess.Popen([sys.executable, "-m", "moto.server", "-H", "127.0.0.1", "-p", str(port)], stdout=logs, stderr=logs)
        try:
            deadline = time.monotonic() + 20
            while True:
                if process.poll() is not None:
                    raise RuntimeError("S3 test server exited during startup")
                try:
                    with urllib.request.urlopen(endpoint, timeout=1):
                        break
                except (urllib.error.URLError, TimeoutError):
                    if time.monotonic() >= deadline:
                        raise RuntimeError("S3 test server did not become ready")
                    time.sleep(0.1)
            client = boto3.client("s3", endpoint_url=endpoint, region_name="us-east-1", aws_access_key_id="testing", aws_secret_access_key="testingsecret")
            client.create_bucket(Bucket="lance-tests")
            sys.path.insert(0, str(ROOT / "examples/python"))
            from prepare_dataset import prepare
            prepare("s3://lance-tests/datasets/training.lance", {
                "aws_endpoint": endpoint,
                "aws_region": "us-east-1",
                "aws_access_key_id": "testing",
                "aws_secret_access_key": "testingsecret",
                "aws_allow_http": "true",
                "aws_virtual_hosted_style_request": "false",
            })
            environment = dict(os.environ, LANCE_TEST_S3_ENDPOINT=endpoint, LANCE_TEST_S3_URI="s3://lance-tests/datasets/training.lance")
            return subprocess.run(["dotnet", "test", "Lance.slnx", "--filter", "FullyQualifiedName~S3Tests"], cwd=ROOT, env=environment).returncode
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


if __name__ == "__main__":
    raise SystemExit(main())
