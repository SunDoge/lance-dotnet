using Apache.Arrow;
using Apache.Arrow.C;
using Apache.Arrow.Ipc;
using SunDoge.Lance.Interop;
using NativeDataset = SunDoge.Lance.Interop.LanceDataset;
using NativeScanner = SunDoge.Lance.Interop.LanceScanner;

namespace SunDoge.Lance;

/// <summary>A disposable Lance dataset snapshot. Reopen to observe external commits.</summary>
public sealed unsafe class LanceDataset : IDisposable
{
    private readonly NativeHandle _handle;
    private readonly IReadOnlyDictionary<string, string>? _storageOptions;
    public string Uri { get; }

    internal LanceDataset(NativeDataset* pointer, string uri, IReadOnlyDictionary<string, string>? storageOptions)
    {
        _handle = new NativeHandle(pointer, HandleKind.Dataset);
        Uri = uri;
        _storageOptions = storageOptions is null ? null : new Dictionary<string, string>(storageOptions);
    }

    /// <summary>Open a specific version, or the latest committed version when version is zero.</summary>
    public static LanceDataset Open(string uri, IReadOnlyDictionary<string, string>? storageOptions = null, ulong version = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        using var path = new Utf8(uri);
        using var options = Utf8Array.StorageOptions(storageOptions);
        return new LanceDataset(NativeMethods.lance_dataset_open(path.Pointer, options.Pointer, version), uri, storageOptions);
    }

    /// <summary>Open another snapshot without changing this handle. Zero opens the latest version.</summary>
    public LanceDataset OpenVersion(ulong version)
    {
        using var lease = _handle.Acquire();
        return Open(Uri, _storageOptions, version);
    }

    public ulong Version
    {
        get
        {
            using var lease = _handle.Acquire();
            var value = NativeMethods.lance_dataset_version((NativeDataset*)lease.Pointer);
            LanceException.CheckLastError();
            return value;
        }
    }

    /// <summary>Query storage for the latest version without changing this snapshot.</summary>
    public ulong LatestVersion
    {
        get
        {
            using var lease = _handle.Acquire();
            var value = NativeMethods.lance_dataset_latest_version((NativeDataset*)lease.Pointer);
            LanceException.CheckLastError();
            return value;
        }
    }

    public ulong CountRows()
    {
        using var lease = _handle.Acquire();
        var count = NativeMethods.lance_dataset_count_rows((NativeDataset*)lease.Pointer);
        LanceException.CheckLastError();
        return count;
    }

    public Schema Schema
    {
        get
        {
            using var lease = _handle.Acquire();
            ArrowSchema schema = default;
            try
            {
                LanceException.Check(NativeMethods.lance_dataset_schema((NativeDataset*)lease.Pointer, &schema));
                return CArrowSchemaImporter.ImportSchema((CArrowSchema*)&schema);
            }
            finally { if (schema.release != null) schema.release(&schema); }
        }
    }

    public IReadOnlyList<LanceVersion> GetVersions()
    {
        using var lease = _handle.Acquire();
        using var versions = new NativeHandle(NativeMethods.lance_dataset_versions((NativeDataset*)lease.Pointer), HandleKind.Versions);
        using var snapshot = versions.Acquire();
        var pointer = (LanceVersions*)snapshot.Pointer;
        var count = NativeMethods.lance_versions_count(pointer);
        LanceException.CheckLastError();
        var result = new List<LanceVersion>(checked((int)count));
        for (nuint i = 0; i < count; i++)
        {
            var version = NativeMethods.lance_versions_id_at(pointer, i);
            LanceException.CheckLastError();
            var timestamp = NativeMethods.lance_versions_timestamp_ms_at(pointer, i);
            LanceException.CheckLastError();
            result.Add(new LanceVersion(version, DateTimeOffset.FromUnixTimeMilliseconds(timestamp)));
        }
        return result;
    }

    public IReadOnlyList<ulong> GetFragmentIds()
    {
        using var lease = _handle.Acquire();
        var pointer = (NativeDataset*)lease.Pointer;
        var count = NativeMethods.lance_dataset_fragment_count(pointer);
        LanceException.CheckLastError();
        var ids = new ulong[checked((int)count)];
        if (ids.Length > 0)
        {
            fixed (ulong* buffer = ids) LanceException.Check(NativeMethods.lance_dataset_fragment_ids(pointer, buffer));
        }
        return ids;
    }

    public IReadOnlyList<LanceFieldStatistics> GetDataStatistics()
    {
        using var lease = _handle.Acquire();
        using var statistics = new NativeHandle(NativeMethods.lance_dataset_calculate_data_stats((NativeDataset*)lease.Pointer), HandleKind.Statistics);
        using var snapshot = statistics.Acquire();
        var pointer = (LanceDataStatistics*)snapshot.Pointer;
        var count = NativeMethods.lance_data_statistics_count(pointer);
        LanceException.CheckLastError();
        var result = new List<LanceFieldStatistics>(checked((int)count));
        for (nuint i = 0; i < count; i++)
        {
            var id = NativeMethods.lance_data_statistics_field_id_at(pointer, i);
            LanceException.CheckLastError();
            var bytes = NativeMethods.lance_data_statistics_bytes_on_disk_at(pointer, i);
            LanceException.CheckLastError();
            result.Add(new LanceFieldStatistics(id, bytes));
        }
        return result;
    }

    /// <summary>Create an independent Arrow stream. Dispose the stream and each returned batch.</summary>
    /// <remarks>Native calls, including batch reads, block. Cancellation is observed between reads, not during native I/O.</remarks>
    public IArrowArrayStream Scan(LanceScanOptions? options = null)
    {
        using var scanner = CreateScanner(options);
        using var scanLease = scanner.Acquire();
        var pointer = (NativeScanner*)scanLease.Pointer;
        ArrowArrayStream stream = default;
        try
        {
            LanceException.Check(NativeMethods.lance_scanner_to_arrow_stream(pointer, &stream));
            return CArrowArrayStreamImporter.ImportArrayStream((CArrowArrayStream*)&stream);
        }
        finally { if (stream.release != null) stream.release(&stream); }
    }

    /// <summary>Read batches using native polling and wake notifications.</summary>
    /// <remarks>
    /// The sequence opens a scanner when enumeration starts; keep the dataset alive until
    /// then. Each batch belongs to the caller and must be disposed. Breaking enumeration
    /// or cancelling disposes the scanner. In lance-c v0.1.9 initial query preparation is
    /// blocking and runs once on a worker thread; cancellation is observed when it returns.
    /// Later pending reads suspend without occupying a .NET worker thread.
    /// </remarks>
    public IAsyncEnumerable<RecordBatch> ScanAsync(LanceScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new LanceScanOptions();
        ValidateScanOptions(options);
        return LanceAsyncScan.Read(this, options, cancellationToken);
    }

    internal NativeHandle CreateScanner(LanceScanOptions? options)
    {
        options ??= new LanceScanOptions();
        ValidateScanOptions(options);
        using var lease = _handle.Acquire();
        using var columns = new Utf8Array(options.Columns);
        using var filter = new Utf8(options.Filter);
        var scanner = new NativeHandle(NativeMethods.lance_scanner_new((NativeDataset*)lease.Pointer, columns.Pointer, filter.Pointer), HandleKind.Scanner);
        try
        {
            using var scanLease = scanner.Acquire();
            var pointer = (NativeScanner*)scanLease.Pointer;
            if (options.Limit is { } limit) LanceException.Check(NativeMethods.lance_scanner_set_limit(pointer, limit));
            LanceException.Check(NativeMethods.lance_scanner_set_offset(pointer, options.Offset));
            LanceException.Check(NativeMethods.lance_scanner_set_batch_size(pointer, options.BatchSize));
            LanceException.Check(NativeMethods.lance_scanner_with_row_id(pointer, options.WithRowId));
            if (options.FragmentIds is { } ids)
            {
                var values = ids.ToArray();
                fixed (ulong* buffer = values) LanceException.Check(NativeMethods.lance_scanner_set_fragment_ids(pointer, buffer, (nuint)values.Length));
            }
            if (options.Nearest is { } nearest) ConfigureNearest(pointer, nearest);
            if (options.FullTextQuery is { } query)
            {
                using var text = new Utf8(query);
                LanceException.Check(NativeMethods.lance_scanner_full_text_search(pointer, text.Pointer, null, 0));
            }
            return scanner;
        }
        catch
        {
            scanner.Dispose();
            throw;
        }
    }

    /// <summary>Read zero-based logical row offsets in this snapshot.</summary>
    public IArrowArrayStream Take(ReadOnlySpan<ulong> offsets, IReadOnlyList<string>? columns = null) => TakeCore(offsets, columns, false);

    /// <summary>Read row IDs obtained from _rowid in this snapshot; missing IDs may be omitted.</summary>
    public IArrowArrayStream TakeRows(ReadOnlySpan<ulong> rowIds, IReadOnlyList<string>? columns = null) => TakeCore(rowIds, columns, true);

    private IArrowArrayStream TakeCore(ReadOnlySpan<ulong> ids, IReadOnlyList<string>? columns, bool byRowId)
    {
        using var lease = _handle.Acquire();
        using var projection = new Utf8Array(columns);
        ArrowArrayStream stream = default;
        try
        {
            ulong empty = 0;
            fixed (ulong* values = ids)
            {
                // v0.1.9 requires a non-NULL indices pointer even when count is zero.
                var buffer = ids.IsEmpty ? &empty : values;
                var status = byRowId
                    ? NativeMethods.lance_dataset_take_rows((NativeDataset*)lease.Pointer, buffer, (nuint)ids.Length, projection.Pointer, &stream)
                    : NativeMethods.lance_dataset_take((NativeDataset*)lease.Pointer, buffer, (nuint)ids.Length, projection.Pointer, &stream);
                LanceException.Check(status);
            }
            return CArrowArrayStreamImporter.ImportArrayStream((CArrowArrayStream*)&stream);
        }
        finally { if (stream.release != null) stream.release(&stream); }
    }

    private static void ValidateScanOptions(LanceScanOptions options)
    {
        if (options.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options.BatchSize));
        if (options.Offset < 0) throw new ArgumentOutOfRangeException(nameof(options.Offset));
        if (options.Limit < 0) throw new ArgumentOutOfRangeException(nameof(options.Limit));
        if (options.Nearest is not null && options.FullTextQuery is not null)
            throw new ArgumentException("Vector and full-text queries cannot be combined by the Lance scanner.", nameof(options));
        if (options.Nearest is { } nearest)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nearest.Column);
            if (nearest.Vector.IsEmpty || nearest.K == 0 || nearest.NProbes == 0 || nearest.RefineFactor == 0)
                throw new ArgumentException("Vector, K and any probe/refinement settings must be nonzero.", nameof(options));
            foreach (var value in nearest.Vector.Span)
                if (!float.IsFinite(value)) throw new ArgumentException("Query vector values must be finite.", nameof(options));
        }
    }

    private static void ConfigureNearest(NativeScanner* scanner, LanceVectorQuery query)
    {
        using var column = new Utf8(query.Column);
        fixed (float* vector = query.Vector.Span)
            LanceException.Check(NativeMethods.lance_scanner_nearest(scanner, column.Pointer, vector, (nuint)query.Vector.Length, LanceDataType.LANCE_DTYPE_FLOAT32, query.K));
        LanceException.Check(NativeMethods.lance_scanner_set_metric(scanner, query.Metric));
        LanceException.Check(NativeMethods.lance_scanner_set_use_index(scanner, query.UseIndex));
        LanceException.Check(NativeMethods.lance_scanner_set_prefilter(scanner, query.Prefilter));
        if (query.NProbes is { } probes) LanceException.Check(NativeMethods.lance_scanner_set_nprobes(scanner, probes));
        if (query.RefineFactor is { } factor) LanceException.Check(NativeMethods.lance_scanner_set_refine_factor(scanner, factor));
    }

    public void Dispose() => _handle.Dispose();
}
