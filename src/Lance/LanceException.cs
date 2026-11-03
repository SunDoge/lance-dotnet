using System.Runtime.InteropServices;
using SunDoge.Lance.Interop;

namespace SunDoge.Lance;

/// <summary>An error reported by the Lance C API.</summary>
public sealed class LanceException : Exception
{
    public LanceErrorCode ErrorCode { get; }

    internal LanceException(LanceErrorCode errorCode, string message) : base(message) => ErrorCode = errorCode;

    internal static unsafe LanceException FromLastError()
    {
        // Errors are thread-local: read both fields immediately on the calling thread.
        var code = NativeMethods.lance_last_error_code();
        var message = NativeMethods.lance_last_error_message();
        try
        {
            return new LanceException(code, Marshal.PtrToStringUTF8((nint)message) ?? "Lance operation failed.");
        }
        finally { NativeMethods.lance_free_string(message); }
    }

    internal static void Check(int status)
    {
        if (status != 0) throw FromLastError();
    }

    internal static void CheckLastError()
    {
        if (NativeMethods.lance_last_error_code() != LanceErrorCode.LANCE_OK) throw FromLastError();
    }
}
