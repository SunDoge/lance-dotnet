using Apache.Arrow;
using Apache.Arrow.Ipc;
using SunDoge.Lance;
using SunDoge.Lance.Interop;
using Dataset = SunDoge.Lance.LanceDataset;
using Session = SunDoge.Lance.LanceSession;

namespace Lance.Tests;

public class DatasetTests
{
    private static string Fixture(string name = "training") => Path.Combine(AppContext.BaseDirectory, "TestData", name + ".lance");

    [Fact]
    public async Task PythonSnapshotsPreserveSamplesAndHistoricalAnnotations()
    {
        using var latest = Dataset.Open(Fixture());
        Assert.Equal(3UL, latest.Version);
        Assert.Equal(3UL, latest.CountRows());
        using var initial = latest.OpenVersion(1);
        using var appended = latest.OpenVersion(2);
        Assert.Equal(2UL, initial.CountRows());
        Assert.Equal(3UL, appended.CountRows());
        Assert.Equal(3UL, initial.LatestVersion);
        Assert.Equal(1UL, initial.Version);
        Assert.Equal(new long[] { 1, 2 }, (await ReadRows(initial.Scan())).Select(row => row.Id));
        Assert.Equal("cat", (await ReadRows(initial.Scan())).Single(row => row.Id == 1).Annotation);
        Assert.Equal("cat", (await ReadRows(appended.Scan())).Single(row => row.Id == 1).Annotation);
        Assert.Equal("tiger", (await ReadRows(latest.Scan())).Single(row => row.Id == 1).Annotation);
        Assert.Equal(new ulong[] { 1, 2, 3 }, latest.GetVersions().Select(version => version.Version));
        Assert.All(latest.GetVersions(), entry => Assert.True(entry.Timestamp > DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task SchemaProjectionFilterAndBatchesWorkAcrossFfi()
    {
        using var dataset = Dataset.Open(Fixture());
        Assert.Equal(new[] { "sample_id", "payload_uri", "annotation", "split", "embedding" }, dataset.Schema.FieldsList.Select(field => field.Name));
        using var reader = dataset.Scan(new LanceScanOptions
        {
            Columns = ["sample_id", "annotation", "payload_uri"],
            Filter = "split = 'train'",
            BatchSize = 1,
        });
        var ids = new List<long>();
        while (await reader.ReadNextRecordBatchAsync() is { } batch)
        {
            using (batch)
            {
                Assert.Equal(1, batch.Length);
                Assert.Equal(3, batch.ColumnCount);
                ids.Add(((Int64Array)batch.Column(0)).GetValue(0)!.Value);
                if (ids[^1] == 1) Assert.Equal("s3://raw/猫.jpg", ((StringArray)batch.Column(2)).GetString(0));
            }
        }
        Assert.Equal(new long[] { 1, 3 }, ids.Order());
    }

    [Fact]
    public async Task LimitOffsetAndZeroLimitAreApplied()
    {
        using var dataset = Dataset.Open(Fixture());
        var rows = await ReadRows(dataset.Scan(new LanceScanOptions { Limit = 1, Offset = 1 }));
        Assert.Single(rows);
        Assert.Empty(await ReadRows(dataset.Scan(new LanceScanOptions { Limit = 0 })));
    }

    [Fact]
    public async Task TakePreservesRequestedOrderAndDuplicates()
    {
        using var dataset = Dataset.Open(Fixture(), version: 2);
        var rows = await ReadRows(dataset.Take([2, 0, 2], ["sample_id", "annotation"]));
        Assert.Equal(new long[] { 3, 1, 3 }, rows.Select(row => row.Id));
        Assert.Empty(await ReadRows(dataset.Take([])));
    }

    [Fact]
    public async Task RowIdsCanBeTakenFromTheSameSnapshot()
    {
        using var dataset = Dataset.Open(Fixture());
        using var scan = dataset.Scan(new LanceScanOptions { Filter = "sample_id = 3", WithRowId = true });
        using var batch = await scan.ReadNextRecordBatchAsync();
        Assert.NotNull(batch);
        var fieldIndex = batch.Schema.GetFieldIndex("_rowid");
        var rowId = ((UInt64Array)batch.Column(fieldIndex)).GetValue(0)!.Value;
        var rows = await ReadRows(dataset.TakeRows([rowId, rowId]));
        Assert.Equal(new long[] { 3, 3 }, rows.Select(row => row.Id));
    }

    [Fact]
    public async Task FragmentScopesAndStorageStatisticsAreAvailable()
    {
        using var dataset = Dataset.Open(Fixture(), version: 2);
        var ids = dataset.GetFragmentIds();
        Assert.Equal(2, ids.Count);
        var count = 0;
        foreach (var id in ids)
            count += (await ReadRows(dataset.Scan(new LanceScanOptions { FragmentIds = [id] }))).Count;
        Assert.Equal(3, count);
        Assert.Empty(await ReadRows(dataset.Scan(new LanceScanOptions { FragmentIds = [] })));
        Assert.NotEmpty(dataset.GetDataStatistics());
        Assert.Contains(dataset.GetDataStatistics(), entry => entry.BytesOnDisk > 0);
    }

    [Fact]
    public async Task NativeStreamAndBatchOutliveDatasetAndReader()
    {
        var session = new Session();
        var dataset = session.Open(Fixture());
        var stream = dataset.Scan();
        session.Dispose();
        dataset.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var batch = await stream.ReadNextRecordBatchAsync();
        Assert.NotNull(batch);
        stream.Dispose();
        stream.Dispose();
        using (batch) Assert.True(((Int64Array)batch.Column(0)).GetValue(0) > 0);
        dataset.Dispose();
        Assert.Throws<ObjectDisposedException>(() => dataset.CountRows());
        Assert.Throws<ObjectDisposedException>(() => session.Open(Fixture()));
    }

    [Fact]
    public void EmptyDatasetAndNativeErrorsAreNotConfused()
    {
        var error = Assert.Throws<LanceException>(() => Dataset.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
        Assert.Equal(LanceErrorCode.LANCE_ERR_NOT_FOUND, error.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        using var empty = Dataset.Open(Fixture("empty"));
        Assert.Equal(0UL, empty.CountRows());
        Assert.Empty(empty.GetFragmentIds());
        Assert.Throws<LanceException>(() => Dataset.Open(Fixture(), version: 999));
    }

    [Fact]
    public void InvalidStringsAndScanOptionsFailWithoutCrashing()
    {
        Assert.Throws<ArgumentException>(() => Dataset.Open("bad\0path"));
        using var dataset = Dataset.Open(Fixture());
        Assert.Throws<ArgumentOutOfRangeException>(() => dataset.Scan(new LanceScanOptions { BatchSize = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => dataset.Scan(new LanceScanOptions { Offset = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => dataset.Scan(new LanceScanOptions { Limit = -1 }));
        Assert.Throws<LanceException>(() => dataset.Scan(new LanceScanOptions { Columns = ["does_not_exist"] }));
        Assert.Throws<LanceException>(() => dataset.Scan(new LanceScanOptions { Filter = "this is invalid SQL" }));
        using var valid = dataset.Scan();
        Assert.NotNull(valid.Schema);
    }

    [Fact]
    public async Task ExactVectorSearchWorksWithoutAnIndex()
    {
        using var dataset = Dataset.Open(Fixture());
        using var stream = dataset.Scan(new LanceScanOptions
        {
            Nearest = new LanceVectorQuery { Column = "embedding", Vector = new float[] { 1, 0 }, K = 1, UseIndex = false },
        });
        using var batch = await stream.ReadNextRecordBatchAsync();
        Assert.NotNull(batch);
        Assert.Equal(1L, ((Int64Array)batch.Column(batch.Schema.GetFieldIndex("sample_id"))).GetValue(0));
        Assert.Equal(0f, ((FloatArray)batch.Column(batch.Schema.GetFieldIndex("_distance"))).GetValue(0));
    }

    [Fact]
    public async Task FullTextSearchReadsPythonIndex()
    {
        using var dataset = Dataset.Open(Fixture("search"));
        using var stream = dataset.Scan(new LanceScanOptions { FullTextQuery = "tiger" });
        using var batch = await stream.ReadNextRecordBatchAsync();
        Assert.NotNull(batch);
        Assert.Equal(1, batch.Length);
        Assert.Equal(1L, ((Int64Array)batch.Column(batch.Schema.GetFieldIndex("sample_id"))).GetValue(0));
    }

    [Fact]
    public async Task BinaryPayloadAndNestedAnnotationFieldsSurviveArrowInterop()
    {
        using var dataset = Dataset.Open(Fixture("rich"));
        using var stream = dataset.Scan();
        using var batch = await stream.ReadNextRecordBatchAsync();
        Assert.NotNull(batch);
        var payload = (BinaryArray)batch.Column(1);
        Assert.Equal(new byte[] { 0, 255, 16 }, payload.GetBytes(0).ToArray());
        Assert.True(payload.IsNull(1));
        var annotations = (StructArray)batch.Column(2);
        Assert.True(annotations.Fields[0].IsNull(1));
        Assert.True(annotations.Fields[1].IsNull(1));
        Assert.Equal("猫", ((StringArray)annotations.Fields[0]).GetString(0));
        var boxes = (ListArray)annotations.Fields[1];
        var coordinates = (FloatArray)boxes.Values;
        Assert.Equal(new float?[] { 1, 2, 3, 4 }, Enumerable.Range(0, 4).Select(coordinates.GetValue));
    }

    [Fact]
    public void SessionCacheStatisticsCanBeRead()
    {
        using var session = new Session(0, 0);
        using var dataset = session.Open(Fixture());
        Assert.Equal(3UL, dataset.CountRows());
        _ = session.GetCacheStatistics();
    }

    private static async Task<List<(long Id, string? Annotation)>> ReadRows(IArrowArrayStream stream)
    {
        using (stream)
        {
            var result = new List<(long, string?)>();
            while (await stream.ReadNextRecordBatchAsync() is { } batch)
            {
                using (batch)
                {
                    var ids = (Int64Array)batch.Column(batch.Schema.GetFieldIndex("sample_id"));
                    var annotations = (StringArray)batch.Column(batch.Schema.GetFieldIndex("annotation"));
                    for (var i = 0; i < batch.Length; i++) result.Add((ids.GetValue(i)!.Value, annotations.GetString(i)));
                }
            }
            return result;
        }
    }
}
