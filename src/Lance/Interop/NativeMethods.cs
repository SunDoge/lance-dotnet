namespace SunDoge.Lance.Interop;

public static unsafe partial class NativeMethods
{
    static NativeMethods() => LanceRuntime.Initialize();
}
