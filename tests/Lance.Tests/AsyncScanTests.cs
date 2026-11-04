using Apache.Arrow;
using SunDoge.Lance;
using Dataset = SunDoge.Lance.LanceDataset;

namespace Lance.Tests;

public class AsyncScanTests
{
    private static string Fixture(string name = "training") => Path.Combine(AppContext.BaseDirectory, "TestData", name + ".lance");

    [Fact]
    public async Task AsyncProjectionFilteringAndBatchingMatchSnapshot()
    {
        using var dataset = Dataset.Open(Fixture());
        var ids = new List<long>();
        await foreach (var batch in dataset.ScanAsync(new LanceScanOptions
        {
            Columns = ["sample_id", "annotation"], Filter = "split = 'train'", BatchSize = 1,
        }))
        {
            using (batch)
            {
                Assert.Equal(1, batch.Length);
                Assert.Equal(new[] { "sample_id", "annotation" }, batch.Schema.FieldsList.Select(field => field.Name));
                var id = ((Int64Array)batch.Column(0)).GetValue(0)!.Value;
                ids.Add(id);
                Assert.Equal(id == 1 ? "tiger" : "bird", ((StringArray)batch.Column(1)).GetString(0));
            }
        }
        Assert.Equal(new long[] { 1, 3 }, ids.Order());
    }

    [Fact]
    public async Task EmptyScanFinishesWithoutProducingBatch()
    {
        using var dataset = Dataset.Open(Fixture("empty"));
        var count = 0;
        await foreach (var batch in dataset.ScanAsync()) { batch.Dispose(); count++; }
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task EnumerationUsesHistoricalSnapshotAndCanBeRepeatedConcurrently()
    {
        using var dataset = Dataset.Open(Fixture(), version: 1);
        var scan = dataset.ScanAsync(new LanceScanOptions { Columns = ["sample_id", "annotation"], BatchSize = 1 });
        var results = await Task.WhenAll(ReadRows(scan), ReadRows(scan));
        foreach (var rows in results)
        {
            Assert.Equal(2, rows.Count);
            Assert.Equal("cat", rows.Single(row => row.Id == 1).Annotation);
        }
    }

    [Fact]
    public async Task PreCancelledTokenDoesNotStartScan()
    {
        using var dataset = Dataset.Open(Fixture());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var batch in dataset.ScanAsync(cancellationToken: cancellation.Token)) batch.Dispose();
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task CancellationBetweenBatchesRetiresScannerAndPreservesReturnedBatch()
    {
        using var dataset = Dataset.Open(Fixture());
        using var cancellation = new CancellationTokenSource();
        // WithCancellation also exercises EnumeratorCancellation token forwarding.
        await using var iterator = dataset.ScanAsync(new LanceScanOptions { BatchSize = 1 })
            .WithCancellation(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        using var first = iterator.Current;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await iterator.MoveNextAsync());
        Assert.Equal(1, first.Length);
        Assert.True(((Int64Array)first.Column(0)).GetValue(0) > 0);
        Assert.Equal(3UL, dataset.CountRows());
    }

    [Fact]
    public async Task BatchOutlivesEarlyEnumeratorDisposalAndDataset()
    {
        var dataset = Dataset.Open(Fixture());
        var iterator = dataset.ScanAsync(new LanceScanOptions { BatchSize = 1 }).GetAsyncEnumerator();
        try
        {
            var pending = iterator.MoveNextAsync();
            dataset.Dispose();
            Assert.True(await pending);
            using var batch = iterator.Current;
            await iterator.DisposeAsync();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.True(((Int64Array)batch.Column(0)).GetValue(0) > 0);
        }
        finally
        {
            await iterator.DisposeAsync();
            dataset.Dispose();
        }
    }

    [Theory]
    [InlineData("does_not_exist", null)]
    [InlineData("sample_id", "this is invalid SQL")]
    public async Task NativePreparationErrorsAreDeliveredToAwaitingCaller(string column, string? filter)
    {
        using var dataset = Dataset.Open(Fixture());
        var error = await Assert.ThrowsAsync<LanceException>(async () =>
        {
            await foreach (var batch in dataset.ScanAsync(new LanceScanOptions { Columns = [column], Filter = filter })) batch.Dispose();
        });
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public async Task EarlyBreakAndGcDoNotLeaveNativeCallbacksPointingAtFreedContexts()
    {
        using var dataset = Dataset.Open(Fixture());
        for (var i = 0; i < 30; i++)
        {
            await foreach (var batch in dataset.ScanAsync(new LanceScanOptions { BatchSize = 1 }))
            {
                batch.Dispose();
                break;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.Equal(3UL, dataset.CountRows());
    }

    [Fact]
    public async Task AsyncVectorSearchPreservesQueryGeneratedColumns()
    {
        using var dataset = Dataset.Open(Fixture());
        var count = 0;
        await foreach (var batch in dataset.ScanAsync(new LanceScanOptions
        {
            Nearest = new LanceVectorQuery { Column = "embedding", Vector = new float[] { 1, 0 }, K = 1, UseIndex = false },
        }))
        {
            using (batch)
            {
                count += batch.Length;
                Assert.Equal(0f, ((FloatArray)batch.Column(batch.Schema.GetFieldIndex("_distance"))).GetValue(0));
            }
        }
        Assert.Equal(1, count);
    }

    private static async Task<List<(long Id, string? Annotation)>> ReadRows(IAsyncEnumerable<RecordBatch> scan)
    {
        var rows = new List<(long, string?)>();
        await foreach (var batch in scan)
        {
            using (batch)
            {
                for (var i = 0; i < batch.Length; i++)
                    rows.Add((((Int64Array)batch.Column(0)).GetValue(i)!.Value, ((StringArray)batch.Column(1)).GetString(i)));
            }
        }
        return rows;
    }
}
