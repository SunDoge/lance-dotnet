using System.Reflection;
using System.Runtime.InteropServices;

namespace SunDoge.Lance;

/// <summary>Native runtime configuration. Configure before the first native call.</summary>
public static class LanceRuntime
{
    public const string NativeVersion = "0.1.9";
    private static readonly object Gate = new();
    private static string? _libraryPath;
    private static nint _library;

    /// <summary>Use a specific lance-c shared library. The library stays loaded for the process lifetime.</summary>
    public static void SetLibraryPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("lance-c library not found.", fullPath);
        lock (Gate)
        {
            if (_library != 0) throw new InvalidOperationException("lance-c has already been loaded.");
            _libraryPath = fullPath;
        }
    }

    internal static void Initialize() => NativeLibrary.SetDllImportResolver(typeof(LanceRuntime).Assembly, Resolve);

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "lance_c") return 0;
        lock (Gate)
        {
            if (_library != 0) return _library;
            var explicitPath = _libraryPath ?? Environment.GetEnvironmentVariable("LANCE_NATIVE_LIBRARY");
            if (!string.IsNullOrEmpty(explicitPath)) return _library = NativeLibrary.Load(explicitPath);
            var filename = OperatingSystem.IsWindows() ? "lance_c.dll" :
                OperatingSystem.IsMacOS() ? "liblance_c.dylib" : "liblance_c.so";
            var localPath = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", filename);
            if (File.Exists(localPath)) return _library = NativeLibrary.Load(localPath);
            // Uses normal .NET/native loader probing, including NuGet runtime assets.
            return _library = NativeLibrary.Load(name, assembly, searchPath);
        }
    }
}
