using System.Runtime.InteropServices;

internal static class SteamworksNative
{
    private const int SteamErrMsgSize = 1024;
    private static IntPtr libraryHandle;
    private static ShutdownDelegate? shutdown;
    private static RunCallbacksDelegate? runCallbacks;

    public static SteamInitResult Initialize(string path)
    {
        libraryHandle = NativeLibrary.Load(path);
        shutdown = TryGetDelegate<ShutdownDelegate>("SteamAPI_Shutdown");
        runCallbacks = TryGetDelegate<RunCallbacksDelegate>("SteamAPI_RunCallbacks");

        var init = TryGetDelegate<InitDelegate>("SteamAPI_Init");
        if (init is not null)
        {
            return init()
                ? SteamInitResult.Ok("SteamAPI_Init")
                : SteamInitResult.Fail("SteamAPI_Init was exported, but returned false.");
        }

        var initSafe = TryGetDelegate<InitDelegate>("SteamAPI_InitSafe");
        if (initSafe is not null)
        {
            return initSafe()
                ? SteamInitResult.Ok("SteamAPI_InitSafe")
                : SteamInitResult.Fail("SteamAPI_InitSafe was exported, but returned false.");
        }

        var initFlat = TryGetDelegate<InitFlatDelegate>("SteamAPI_InitFlat");
        if (initFlat is not null)
        {
            return InitializeWithFlatApi(initFlat);
        }

        return SteamInitResult.Fail(
            "The selected DLL loaded, but none of these Steamworks initialization exports were found: " +
            "SteamAPI_Init, SteamAPI_InitSafe, SteamAPI_InitFlat.");
    }

    public static void RunCallbacks() => runCallbacks?.Invoke();

    public static void Shutdown()
    {
        shutdown?.Invoke();

        if (libraryHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(libraryHandle);
            libraryHandle = IntPtr.Zero;
        }

        shutdown = null;
        runCallbacks = null;
    }

    private static SteamInitResult InitializeWithFlatApi(InitFlatDelegate initFlat)
    {
        var errorBuffer = Marshal.AllocHGlobal(SteamErrMsgSize);
        try
        {
            Span<byte> empty = stackalloc byte[SteamErrMsgSize];
            Marshal.Copy(empty.ToArray(), 0, errorBuffer, SteamErrMsgSize);

            var resultCode = initFlat(errorBuffer);
            if (resultCode == 0)
            {
                return SteamInitResult.Ok("SteamAPI_InitFlat");
            }

            var errorMessage = Marshal.PtrToStringUTF8(errorBuffer);
            return SteamInitResult.Fail(
                string.IsNullOrWhiteSpace(errorMessage)
                    ? $"SteamAPI_InitFlat returned failure code {resultCode}."
                    : $"SteamAPI_InitFlat returned failure code {resultCode}: {errorMessage}");
        }
        finally
        {
            Marshal.FreeHGlobal(errorBuffer);
        }
    }

    private static TDelegate? TryGetDelegate<TDelegate>(string exportName)
        where TDelegate : Delegate
    {
        if (libraryHandle == IntPtr.Zero)
        {
            return null;
        }

        return NativeLibrary.TryGetExport(libraryHandle, exportName, out var address)
            ? Marshal.GetDelegateForFunctionPointer<TDelegate>(address)
            : null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitFlatDelegate(IntPtr errorMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RunCallbacksDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShutdownDelegate();
}

internal sealed record SteamInitResult(bool Success, string Method, string Message)
{
    public static SteamInitResult Ok(string method) => new(true, method, "");

    public static SteamInitResult Fail(string message) => new(false, "", message);
}
