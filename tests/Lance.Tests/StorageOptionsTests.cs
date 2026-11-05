using SunDoge.Lance;
using Dataset = SunDoge.Lance.LanceDataset;

namespace Lance.Tests;

public class StorageOptionsTests
{
    [Theory]
    [InlineData("relative/path")]
    [InlineData("file:///tmp/data")]
    [InlineData("ftp://example.com")]
    public void InvalidEndpointIsRejectedBeforeNativeOpen(string endpoint)
    {
        var error = Assert.Throws<ArgumentException>(() => Dataset.Open("s3://bucket/data", new S3StorageOptions
        {
            Endpoint = new Uri(endpoint, UriKind.RelativeOrAbsolute),
        }));
        Assert.Equal("Endpoint", error.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0region")]
    public void RegionCannotBeEmptyOrTruncatedByNativeMarshalling(string region)
    {
        var error = Assert.Throws<ArgumentException>(() => Dataset.Open("s3://bucket/data", new S3StorageOptions { Region = region }));
        Assert.Equal("Region", error.ParamName);
    }

    [Fact]
    public void OptionsDoNotIncludeCredentialsInDefaultStringRepresentation()
    {
        var options = new S3StorageOptions { SecretAccessKey = "private-test-secret", SessionToken = "private-test-token" };
        Assert.DoesNotContain("private-test", options.ToString());
    }

    [Fact]
    public void GenericStorageOptionsCanBeUsedWithLocalDataset()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "training.lance");
        using var dataset = Dataset.Open(path, new LanceStorageOptions(), version: 1);
        Assert.Equal(2UL, dataset.CountRows());
    }
}
