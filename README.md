# SunDoge.Lance

C# bindings for the [Lance dataset engine](https://github.com/lance-format/lance-c), not LanceDB.

The safe managed API opens datasets, reads version history and schemas, scans Arrow batches,
and performs projected/filtered/vector/full-text reads. Python can manage ingestion,
annotations, tags and retention. The complete lance-c v0.1.9 API is also available as
unsafe generated bindings in `SunDoge.Lance.Interop.NativeMethods`.

Requires .NET 8 or later and the matching **lance-c v0.1.9** native library. No Rust toolchain
is required for ordinary consumers. Native binaries are fetched explicitly before packaging;
there are no network downloads at application startup.

## Build

```sh
uv run tools/fetch_native.py --rid linux-x64
dotnet test Lance.slnx
dotnet pack src/Lance/Lance.csproj -c Release -o artifacts
```

Python tooling uses Python 3.14 and uv. `uv run` manages the environment using
`pyproject.toml` and `uv.lock`; downloads use httpx2 and CLI arguments use typed-args.
S3-only dependencies are installed with `--group s3`.

Fetch every required RID before packing. Upstream v0.1.9 supplies Linux x64/arm64 and macOS
arm64; it does not supply Windows binaries. Linux uses the Debian 12 (glibc 2.36+) assets.
A package only includes the binaries present when it is packed. To supply a library
separately, set `LANCE_NATIVE_LIBRARY` to its absolute path, or call
`LanceRuntime.SetLibraryPath(path)` before the first native call. Standard native loader
search paths are supported too. The runtime never unloads the shared library while Arrow
buffers may reference its callbacks.

## Read a fixed training snapshot

```csharp
using SunDoge.Lance;

using var dataset = LanceDataset.Open("/data/images.lance", version: 12);
Console.WriteLine($"Version {dataset.Version}, {dataset.CountRows()} samples");

using var reader = dataset.Scan(new LanceScanOptions
{
    Columns = ["sample_id", "payload_uri", "annotation"],
    Filter = "split = 'train'",
    BatchSize = 1024,
});
while (await reader.ReadNextRecordBatchAsync() is { } batch)
{
    using (batch)
    {
        // Pass this Arrow RecordBatch to your TFRecord / ShardPack writer.
    }
}
```

Dispose readers and each returned batch. Streams own their snapshots and remain valid
after the dataset or session is disposed. Batches remain valid after their reader closes.
Dataset handles do not automatically refresh: `OpenVersion(0)` opens the latest snapshot.
Native calls are blocking, including the Arrow reader's `ReadNextRecordBatchAsync`;
cancellation is checked before each read and does not interrupt native I/O. Do not read
one Arrow stream concurrently from multiple threads.

`Take` reads logical row offsets; `TakeRows` reads `_rowid` values from the same snapshot.
These are different identifiers. Use a persistent application `sample_id` for sample identity.
For cache sharing, create a `LanceSession` and open multiple datasets through it.

## S3-compatible storage

```csharp
var storage = new Dictionary<string, string>
{
    ["aws_region"] = "us-east-1",
    ["aws_endpoint"] = "https://your-s3-endpoint",
    ["aws_virtual_hosted_style_request"] = "false",
    // Supply AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY and optionally
    // AWS_SESSION_TOKEN via the process environment or supported credential provider.
};
using var dataset = LanceDataset.Open("s3://bucket/datasets/images.lance", storage, version: 12);
```

Storage options pass through unchanged. Only add `aws_allow_http=true` for an HTTP endpoint.
Concurrent writers require atomic conditional PUT support in the storage provider; see
[Lance object store configuration](https://lance.org/guide/object_store/). The C# wrapper
does not provide distributed locking or a dataset catalog. Keep externally referenced
payload objects immutable: a dataset snapshot does not snapshot an overwritten external object.

## Version history and Python-controlled writes

```csharp
foreach (var entry in dataset.GetVersions())
    Console.WriteLine($"{entry.Version}: {entry.Timestamp:O}");
```

See `examples/python/prepare_dataset.py` for append, annotation updates, tagging and a release
manifest containing the numeric version to pass to C#. Use a Python Lance version compatible
with the native reader and validate that version combination before upgrading. The supplied
native reader is tested against the checked-in pylance 7.0.0 fixtures and fresh
pylance 14.0.2 writes, using data format 2.0.

Versions are snapshots of the entire dataset, not a per-row `version` column. Tag important
training snapshots in Python **before** allowing cleanup. A row in an external SQL table or
a JSON export manifest does not protect the Lance version from cleanup. Tags protect their
referenced snapshots under Lance cleanup; deleting a tag allows later reclamation. Avoid
moving training tags once they have been used, and record the resolved numeric version too.

lance-c v0.1.9 has version APIs but no tag APIs. Its C writer also installs automatic cleanup
for newly created datasets (every 20 versions, reclaiming history older than 14 days), with
no C configuration API. The generated write bindings preserve upstream behavior. The high-level
API deliberately focuses on reads; configure writes, tags and retention using Python.
Existing cleanup policies are not changed by opening a dataset from C#.

## Regenerate FFI

The pinned upstream header is vendored in `native/lance.h`, with its Apache-2.0 license.
With Rust and libclang installed:

```sh
cargo run --locked --manifest-path tools/GenerateBindings/Cargo.toml
```

This uses bindgen + csbindgen to generate all native declarations; no additional Rust shim
or modified Lance runtime is required. Generated code is checked in so .NET builds do not
need those tools. Raw pointers, Arrow C callbacks and thread-local errors in the unsafe API
remain the caller's responsibility.

## Validation

The checked-in fixtures were created with pylance 7.0.0 and pyarrow 22.0.0, using
Lance data format 2.0. Tests cover historical annotations, binary payloads and
nested annotation fields, projection/filtering, row offsets/IDs, fragment scopes,
vector/full-text reads, resource lifetime, and all generated native symbols.
In this tested writer/format combination a NULL parent struct is read back as a
non-NULL struct with default-valued children even in Python. Use a non-NULL
annotation struct with nullable children, or validate a newer format before using
NULL parent structs. The C# wrapper does not rewrite values returned by Lance.

An optional local S3 integration test uses Moto (no real credentials or bucket):

```sh
uv run --group s3 tools/test_s3.py
```

It creates and tags the dataset through Python on the S3 endpoint, then reads its
history and current annotations through C#. This validates S3 transport, not a
particular production provider's conditional-write guarantees.

Run the end-to-end C# reader example:

```sh
dotnet run --project examples/Lance.Read -- --manifest tests/Lance.Tests/TestData/release.json
```

## License

Copyright 2026 SunDoge. Licensed under [Apache-2.0](LICENSE).
Vendored lance-c headers and native libraries retain their upstream copyright
and [license](native/LICENSE).
