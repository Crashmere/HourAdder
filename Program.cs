using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!TryParseArgs(args, out var options))
        {
            PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("HourAdder's Steamworks prototype must be run on Windows with Steam installed and logged in.");
            Console.Error.WriteLine("Build or publish it on macOS, then copy the win-x64 output to your Windows 11 machine.");
            return 1;
        }

        if (options.AppId is null or 0)
        {
            Console.Error.WriteLine("Missing or invalid AppID.");
            PrintUsage();
            return 2;
        }

        var steamApiPath = ResolveSteamApiPath(options.SteamApiPath);
        if (steamApiPath is null)
        {
            Console.Error.WriteLine("Could not find steam_api64.dll.");
            Console.Error.WriteLine("Pass it explicitly, for example:");
            Console.Error.WriteLine(@"  HourAdder.exe --app-id 730 --steam-api ""C:\Program Files (x86)\Steam\steamapps\common\<Game>\steam_api64.dll""");
            return 2;
        }

        var appId = options.AppId.Value;
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var appDirectory = AppContext.BaseDirectory;
        var appIdFilePath = Path.Combine(appDirectory, "steam_appid.txt");

        Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());

        try
        {
            Directory.SetCurrentDirectory(appDirectory);
            await File.WriteAllTextAsync(appIdFilePath, appId.ToString());

            Console.WriteLine($"HourAdder Steamworks prototype");
            Console.WriteLine($"AppID: {appId}");
            Console.WriteLine($"steam_api64.dll: {steamApiPath}");
            Console.WriteLine();
            Console.WriteLine("Initializing Steamworks...");

            var initResult = SteamworksNative.Initialize(steamApiPath);
            if (!initResult.Success)
            {
                Console.Error.WriteLine(initResult.Message);
                Console.Error.WriteLine("Check that Steam is running, the account is logged in, and the selected AppID is owned/installed.");
                return 1;
            }

            Console.WriteLine("Steamworks initialized successfully.");
            Console.WriteLine($"Initialization method: {initResult.Method}");
            Console.WriteLine("If this works for playtime, Steam should now show the selected AppID as running.");
            Console.WriteLine("Press Ctrl+C to stop.");

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Cancel();
            };

            while (!stop.IsCancellationRequested)
            {
                SteamworksNative.RunCallbacks();
                await Task.Delay(TimeSpan.FromSeconds(1), stop.Token).WaitAsync(CancellationToken.None);
            }

            return 0;
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine($"Failed to load steam_api64.dll: {ex.Message}");
            return 1;
        }
        catch (BadImageFormatException ex)
        {
            Console.Error.WriteLine($"steam_api64.dll architecture mismatch. Use a 64-bit DLL with the win-x64 build. {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            try
            {
                SteamworksNative.Shutdown();
            }
            catch
            {
                // Shutdown is best-effort when initialization failed before the native library loaded.
            }

            TryDeleteGeneratedAppIdFile(appIdFilePath, appId);
            Directory.SetCurrentDirectory(originalCurrentDirectory);
        }
    }

    private static string? ResolveSteamApiPath(string? providedPath)
    {
        if (!string.IsNullOrWhiteSpace(providedPath))
        {
            var fullPath = Path.GetFullPath(providedPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "steam_api64.dll"),
            Path.Combine(Environment.CurrentDirectory, "steam_api64.dll")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryDeleteGeneratedAppIdFile(string path, uint appId)
    {
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() == appId.ToString())
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Leaving steam_appid.txt behind is harmless for this prototype.
        }
    }

    private static bool TryParseArgs(string[] args, out Options options)
    {
        options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;

                case "--app-id":
                    if (++i >= args.Length || !uint.TryParse(args[i], out var appId))
                    {
                        return false;
                    }
                    options.AppId = appId;
                    break;

                case "--steam-api":
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    {
                        return false;
                    }
                    options.SteamApiPath = args[i];
                    break;

                default:
                    if (options.AppId is null && uint.TryParse(arg, out var positionalAppId))
                    {
                        options.AppId = positionalAppId;
                        break;
                    }

                    return false;
            }
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("HourAdder Steamworks prototype");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  HourAdder.exe --app-id <appid> [--steam-api <path-to-steam_api64.dll>]");
        Console.WriteLine("  HourAdder.exe <appid> [--steam-api <path-to-steam_api64.dll>]");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine(@"  HourAdder.exe --app-id 730 --steam-api ""C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\steam_api64.dll""");
    }

    private sealed class Options
    {
        public uint? AppId { get; set; }
        public string? SteamApiPath { get; set; }
        public bool ShowHelp { get; set; }
    }
}

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
