using Microsoft.Win32.SafeHandles;

namespace SunDoge.Lance.Interop;

internal enum HandleKind { Dataset, Scanner, Session, Versions, Statistics }

internal sealed unsafe class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly HandleKind _kind;
    internal NativeHandle(void* pointer, HandleKind kind) : base(true)
    {
        _kind = kind;
        if (pointer == null) throw LanceException.FromLastError();
        SetHandle((nint)pointer);
    }

    internal HandleLease Acquire() => new(this);

    protected override bool ReleaseHandle()
    {
        switch (_kind)
        {
            case HandleKind.Dataset: NativeMethods.lance_dataset_close((LanceDataset*)handle); break;
            case HandleKind.Scanner: NativeMethods.lance_scanner_close((LanceScanner*)handle); break;
            case HandleKind.Session: NativeMethods.lance_session_close((LanceSession*)handle); break;
            case HandleKind.Versions: NativeMethods.lance_versions_close((LanceVersions*)handle); break;
            case HandleKind.Statistics: NativeMethods.lance_data_statistics_close((LanceDataStatistics*)handle); break;
        }
        return true;
    }
}

internal readonly unsafe ref struct HandleLease
{
    private readonly NativeHandle _handle;
    internal readonly void* Pointer;
    internal HandleLease(NativeHandle handle)
    {
        var added = false;
        handle.DangerousAddRef(ref added);
        _handle = handle;
        Pointer = (void*)handle.DangerousGetHandle();
    }
    public void Dispose() => _handle.DangerousRelease();
}
