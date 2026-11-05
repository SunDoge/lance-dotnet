using SunDoge.Lance.Interop;
using NativeSession = SunDoge.Lance.Interop.LanceSession;

namespace SunDoge.Lance;

/// <summary>Shares metadata and index caches across dataset handles.</summary>
public sealed unsafe class LanceSession : IDisposable
{
    private readonly NativeHandle _handle;

    /// <summary>Cache capacities are in bytes; zero requests zero capacity.</summary>
    public LanceSession(ulong indexCacheSizeBytes = 256 * 1024 * 1024, ulong metadataCacheSizeBytes = 64 * 1024 * 1024) =>
        _handle = new NativeHandle(NativeMethods.lance_session_new(indexCacheSizeBytes, metadataCacheSizeBytes), HandleKind.Session);

    public LanceDataset Open(string uri, LanceStorageOptions? storageOptions = null, ulong version = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        using var lease = _handle.Acquire();
        using var path = new Utf8(uri);
        var nativeOptions = storageOptions?.ToNativeOptions();
        using var options = Utf8Array.StorageOptions(nativeOptions);
        var pointer = NativeMethods.lance_dataset_open_with_session(path.Pointer, options.Pointer, version, (NativeSession*)lease.Pointer);
        return new LanceDataset(pointer, uri, nativeOptions);
    }

    public LanceSessionCacheStats GetCacheStatistics()
    {
        using var lease = _handle.Acquire();
        LanceSessionCacheStats result;
        LanceException.Check(NativeMethods.lance_session_get_cache_stats((NativeSession*)lease.Pointer, &result));
        return result;
    }

    /// <summary>Already-open datasets and streams remain usable after the session is disposed.</summary>
    public void Dispose() => _handle.Dispose();
}
