# Interoperability fixtures

`Lance.Tests/TestData` contains small, checked-in Lance datasets created by Python:

- `training.lance`: create (v1), append (v2), annotation update (v3), native tags.
- `empty.lance`: empty typed dataset.
- `search.lance`: a Python-built inverted full-text index.
- `rich.lance`: binary payloads and nested annotation fields with nullable children.

The fixtures use pylance 7.0.0, pyarrow 22.0.0 and file format 2.0.
To regenerate into a new empty directory:

```sh
python3 -m pip install -r examples/python/requirements.txt
python3 tools/create_test_fixtures.py /tmp/lance-fixtures
```

The generator never overwrites existing data. Inspect the new fixtures before
replacing checked-in ones. S3 tests are skipped by the ordinary test run; run
`tools/test_s3.py` with `tests/requirements-s3.txt` installed to start an isolated
local Moto server and exercise Python writes and C# reads over S3.
