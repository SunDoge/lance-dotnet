using System.Reflection;
using System.Runtime.InteropServices;
using Apache.Arrow.C;
using SunDoge.Lance;
using SunDoge.Lance.Interop;

namespace Lance.Tests;

public class InteropTests
{
    [Fact]
    public unsafe void GeneratedArrowStructsMatchApacheArrowAbi()
    {
        Assert.Equal(sizeof(CArrowSchema), sizeof(ArrowSchema));
        Assert.Equal(sizeof(CArrowArray), sizeof(ArrowArray));
        Assert.Equal(sizeof(CArrowArrayStream), sizeof(ArrowArrayStream));
        Assert.Equal(8, (int)Marshal.OffsetOf<LanceWriteParams>(nameof(LanceWriteParams.max_rows_per_group)));
        Assert.Equal((nint)32, Marshal.OffsetOf<LanceWriteParams>(nameof(LanceWriteParams.enable_stable_row_ids)));
    }

    [Fact]
    public void AllGeneratedEntryPointsExistInPinnedNativeLibrary()
    {
        var filename = OperatingSystem.IsWindows() ? "lance_c.dll" : OperatingSystem.IsMacOS() ? "liblance_c.dylib" : "liblance_c.so";
        var path = Environment.GetEnvironmentVariable("LANCE_NATIVE_LIBRARY") ?? Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", filename);
        var library = NativeLibrary.Load(path);
        try
        {
            var imports = typeof(NativeMethods).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<DllImportAttribute>()).OfType<DllImportAttribute>().ToArray();
            Assert.True(imports.Length >= 90);
            foreach (var import in imports)
            {
                Assert.Equal(CallingConvention.Cdecl, import.CallingConvention);
                Assert.True(NativeLibrary.TryGetExport(library, import.EntryPoint!, out _), import.EntryPoint);
            }
        }
        finally { NativeLibrary.Free(library); }
    }
}
