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
        var storage = new Dictionary<string, string>
        {
            ["aws_endpoint"] = Environment.GetEnvironmentVariable("LANCE_TEST_S3_ENDPOINT")!,
            ["aws_region"] = "us-east-1",
            ["aws_access_key_id"] = "testing",
            ["aws_secret_access_key"] = "testingsecret",
            ["aws_allow_http"] = "true",
            ["aws_virtual_hosted_style_request"] = "false",
        };
        using var latest = Dataset.Open(Environment.GetEnvironmentVariable("LANCE_TEST_S3_URI")!, storage);
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
    }
}
