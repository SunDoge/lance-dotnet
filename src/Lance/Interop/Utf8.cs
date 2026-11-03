using System.Runtime.InteropServices;

namespace SunDoge.Lance.Interop;

internal sealed unsafe class Utf8 : IDisposable
{
    private nint _pointer;
    internal byte* Pointer => (byte*)_pointer;
    internal Utf8(string? value)
    {
        if (value is null) return;
        if (value.Contains('\0')) throw new ArgumentException("Native strings cannot contain NUL characters.", nameof(value));
        _pointer = Marshal.StringToCoTaskMemUTF8(value);
    }
    public void Dispose()
    {
        Marshal.FreeCoTaskMem(_pointer);
        _pointer = 0;
    }
}

internal sealed unsafe class Utf8Array : IDisposable
{
    private readonly List<Utf8> _strings = [];
    private nint _pointer;
    internal byte** Pointer => (byte**)_pointer;
    internal Utf8Array(IEnumerable<string>? values)
    {
        if (values is null) return;
        try
        {
            foreach (var value in values)
            {
                ArgumentNullException.ThrowIfNull(value);
                _strings.Add(new Utf8(value));
            }
            _pointer = (nint)NativeMemory.AllocZeroed((nuint)(_strings.Count + 1), (nuint)sizeof(nint));
            for (var i = 0; i < _strings.Count; i++) Pointer[i] = _strings[i].Pointer;
        }
        catch { Dispose(); throw; }
    }
    internal static Utf8Array StorageOptions(IReadOnlyDictionary<string, string>? options) =>
        new(options?.SelectMany(pair => new[] { pair.Key, pair.Value }));
    public void Dispose()
    {
        foreach (var value in _strings) value.Dispose();
        _strings.Clear();
        NativeMemory.Free((void*)_pointer);
        _pointer = 0;
    }
}
