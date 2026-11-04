# Interoperability fixtures

`Lance.Tests/TestData` contains small, checked-in Lance datasets created by Python:

- `training.lance`: create (v1), append (v2), annotation update (v3), native tags.
- `empty.lance`: empty typed dataset.
- `search.lance`: a Python-built inverted full-text index.
- `rich.lance`: binary payloads and nested annotation fields with nullable children.

The fixtures use pylance 7.0.0, pyarrow 22.0.0 and file format 2.0.
Regeneration uses the current Python dependencies in `uv.lock` (pylance 14.0.2
and pyarrow 26.0.0 have also passed C# interoperability checks). To generate into
a new empty directory:

```sh
uv run tools/create_test_fixtures.py /tmp/lance-fixtures
```

The generator never overwrites existing data. Inspect the new fixtures before
replacing checked-in ones. S3 tests are skipped by the ordinary test run; run
`uv run --group s3 tools/test_s3.py` to start an isolated
local Moto server and exercise Python writes and C# reads over S3.

`AsyncScanTests` exercises native poll/waker iteration, repeated and concurrent
enumerations, cancellation, early disposal, historical snapshots and deferred
errors. The optional S3 test exercises both synchronous and asynchronous reads.
