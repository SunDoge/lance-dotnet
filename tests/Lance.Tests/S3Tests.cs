using Apache.Arrow;
using SunDoge.Lance;
using Dataset = SunDoge.Lance.LanceDataset;

namespace Lance.Tests;

public sealed class S3FactAttribute : FactAttribute
{
    public S3FactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LANCE_TEST_S3_URI")))
            Skip = "Run tools/test_s3.py to start the local S3 test server.";
    }
}

public class S3Tests
{
    [S3Fact]
    public async Task PythonWrittenDatasetCanBeReadThroughS3CompatibleEndpoint()
    {
        var storage = new S3StorageOptions
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("LANCE_TEST_S3_ENDPOINT")!),
            Region = "us-east-1",
            AccessKeyId = "testing",
            SecretAccessKey = "testingsecret",
            AllowHttp = true,
            UseVirtualHostedStyle = false,
        };
        var uri = Environment.GetEnvironmentVariable("LANCE_TEST_S3_URI")!;
        using var session = new SunDoge.Lance.LanceSession();
        using var latest = session.Open(uri, storage);
        using var standalone = Dataset.Open(uri, storage, version: 1);
        Assert.Equal(2UL, standalone.CountRows());
        Assert.Equal(3UL, latest.CountRows());
        Assert.Equal(3UL, latest.Version);
        using var first = latest.OpenVersion(1);
        Assert.Equal(2UL, first.CountRows());
        using var stream = latest.Scan(new LanceScanOptions
        {
            Columns = ["sample_id", "annotation", "payload_uri"],
            Filter = "sample_id = 1",
        });
        using var batch = await stream.ReadNextRecordBatchAsync();
        Assert.NotNull(batch);
        Assert.Equal("tiger", ((StringArray)batch.Column(1)).GetString(0));
        Assert.Equal("s3://raw/猫.jpg", ((StringArray)batch.Column(2)).GetString(0));
        var count = 0;
        await foreach (var asyncBatch in latest.ScanAsync(new LanceScanOptions
        {
            Columns = ["sample_id", "annotation", "payload_uri"],
            Filter = "sample_id = 1", BatchSize = 1,
        }))
        {
            using (asyncBatch)
            {
                count += asyncBatch.Length;
                Assert.Equal("tiger", ((StringArray)asyncBatch.Column(1)).GetString(0));
                Assert.Equal("s3://raw/猫.jpg", ((StringArray)asyncBatch.Column(2)).GetString(0));
            }
        }
        Assert.Equal(1, count);
        var additional = new Dictionary<string, string>
        {
            ["aws_endpoint"] = Environment.GetEnvironmentVariable("LANCE_TEST_S3_ENDPOINT")!,
            ["aws_region"] = "us-east-1",
            ["aws_allow_http"] = "false",
        };
        using var snapshot = Dataset.Open(uri, new S3StorageOptions
        {
            AdditionalOptions = additional,
            AllowHttp = true, // Typed settings override matching additional keys.
            AccessKeyId = "testing", SecretAccessKey = "testingsecret",
            UseVirtualHostedStyle = false,
        });
        additional["aws_endpoint"] = "http://127.0.0.1:1";
        using var historical = snapshot.OpenVersion(1);
        Assert.Equal(2UL, historical.CountRows());
    }
}
