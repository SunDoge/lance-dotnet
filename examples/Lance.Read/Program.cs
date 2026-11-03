using System.Text.Json;
using SunDoge.Lance;

if (args.Length != 2 || args[0] != "--manifest")
{
    Console.Error.WriteLine("Usage: dotnet run --project examples/Lance.Read -- --manifest release.json");
    return 1;
}

using var release = JsonDocument.Parse(File.ReadAllText(args[1]));
var root = release.RootElement;
var uri = root.GetProperty("dataset_uri").GetString()!;
var version = root.GetProperty("version").GetUInt64();
using var dataset = LanceDataset.Open(uri, version: version);
using var reader = dataset.Scan(new LanceScanOptions
{
    Filter = root.GetProperty("filter").GetString(),
    BatchSize = 1024,
});
long total = 0;
while (await reader.ReadNextRecordBatchAsync() is { } batch)
{
    using (batch)
    {
        // Feed the Arrow batch to a TFRecord / ShardPack encoder here.
        Console.WriteLine($"Read {batch.Length} samples with {batch.ColumnCount} columns");
        total += batch.Length;
    }
}
Console.WriteLine($"Dataset version {dataset.Version}: {total} selected samples");
return 0;
