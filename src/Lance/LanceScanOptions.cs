using SunDoge.Lance.Interop;

namespace SunDoge.Lance;

/// <summary>Projection, filtering and batching for an independent dataset snapshot scan.</summary>
public sealed record LanceScanOptions
{
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public long? Limit { get; init; }
    public long Offset { get; init; }
    public int BatchSize { get; init; } = 1024;
    public bool WithRowId { get; init; }
    public IReadOnlyList<ulong>? FragmentIds { get; init; }
    public LanceVectorQuery? Nearest { get; init; }
    public string? FullTextQuery { get; init; }
}

/// <summary>A float32 nearest-neighbor query. Values are copied by the native setter.</summary>
public sealed record LanceVectorQuery
{
    public required string Column { get; init; }
    public required ReadOnlyMemory<float> Vector { get; init; }
    public uint K { get; init; } = 10;
    public LanceMetricType Metric { get; init; } = LanceMetricType.LANCE_METRIC_L2;
    public bool UseIndex { get; init; } = true;
    public bool Prefilter { get; init; } = true;
    public uint? NProbes { get; init; }
    public uint? RefineFactor { get; init; }
}

public sealed record LanceVersion(ulong Version, DateTimeOffset Timestamp);
public sealed record LanceFieldStatistics(uint FieldId, ulong BytesOnDisk);
