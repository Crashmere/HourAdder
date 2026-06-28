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
            Console.Error.WriteLine("HourAdder must be run on Windows with Steam installed and logged in.");
            Console.Error.WriteLine("Build or publish it on macOS, then copy the win-x64 output to your Windows 11 machine.");
            return 1;
        }

        var discovery = SteamDiscovery.Discover(options.SteamRoot);
        if (options.ListOnly)
        {
            return PrintDiscoveryResult(discovery, options.SearchQuery);
        }

        SteamGame? selectedGame = null;
        uint appId;

        if (options.AppId is { } requestedAppId and not 0)
        {
            appId = requestedAppId;
            selectedGame = discovery.Games.FirstOrDefault(game => game.AppId == appId);
        }
        else
        {
            if (!discovery.Success)
            {
                Console.Error.WriteLine(discovery.ErrorMessage);
                Console.Error.WriteLine("Use --steam-root if Steam is installed in an unusual location.");
                return 1;
            }

            if (discovery.Games.Count == 0)
            {
                Console.Error.WriteLine("No installed Steam games were found.");
                return 1;
            }

            selectedGame = PromptForGame(discovery.Games, options.SearchQuery);
            if (selectedGame is null)
            {
                return 1;
            }

            appId = selectedGame.AppId;
        }

        var steamApiPath = ResolveSteamApiPath(options.SteamApiPath, selectedGame);
        if (steamApiPath is null)
        {
            Console.Error.WriteLine("Could not find steam_api64.dll.");

            if (selectedGame is not null)
            {
                Console.Error.WriteLine($"Searched under: {selectedGame.InstallDirectory}");
            }

            Console.Error.WriteLine("Pass it explicitly, for example:");
            Console.Error.WriteLine(@"  HourAdder.exe --app-id 730 --steam-api ""C:\Program Files (x86)\Steam\steamapps\common\<Game>\steam_api64.dll""");
            return 2;
        }

        return await RunSteamworksLoop(appId, selectedGame, steamApiPath);
    }

    private static int PrintDiscoveryResult(SteamDiscoveryResult discovery, string? searchQuery)
    {
        if (!discovery.Success)
        {
            Console.Error.WriteLine(discovery.ErrorMessage);
            return 1;
        }

        Console.WriteLine($"Steam root: {discovery.SteamRoot}");
        Console.WriteLine($"Libraries: {discovery.Libraries.Count}");
        var games = FilterGames(discovery.Games, searchQuery);
        Console.WriteLine($"Installed games: {discovery.Games.Count}");
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            Console.WriteLine($"Search: {searchQuery}");
            Console.WriteLine($"Matches: {games.Count}");
        }

        Console.WriteLine();
        PrintGames(games);
        return 0;
    }

    private static SteamGame? PromptForGame(IReadOnlyList<SteamGame> games, string? initialSearchQuery)
    {
        var visibleGames = FilterGames(games, initialSearchQuery);
        PrintGames(visibleGames);
        Console.WriteLine();

        while (true)
        {
            Console.Write("Select by number/AppID, type search text, or q to quit: ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (input.Equals("q", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (int.TryParse(input, out var number) &&
                number >= 1 &&
                number <= visibleGames.Count)
            {
                return visibleGames[number - 1];
            }

            if (uint.TryParse(input, out var appId))
            {
                var match = games.FirstOrDefault(game => game.AppId == appId);
                if (match is not null)
                {
                    return match;
                }
            }

            visibleGames = FilterGames(games, input);
            if (visibleGames.Count == 0)
            {
                Console.WriteLine($"No games matched \"{input}\".");
                visibleGames = games;
            }
            else
            {
                Console.WriteLine();
                PrintGames(visibleGames);
            }
        }
    }

    private static IReadOnlyList<SteamGame> FilterGames(IReadOnlyList<SteamGame> games, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return games;
        }

        var terms = searchQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
        {
            return games;
        }

        return games
            .Where(game => terms.All(term => MatchesGame(game, term)))
            .ToArray();
    }

    private static bool MatchesGame(SteamGame game, string term) =>
        game.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        game.AppId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
        game.InstallDirectory.Contains(term, StringComparison.CurrentCultureIgnoreCase);

    private static void PrintGames(IReadOnlyList<SteamGame> games)
    {
        if (games.Count == 0)
        {
            Console.WriteLine("No installed games found.");
            return;
        }

        for (var i = 0; i < games.Count; i++)
        {
            var game = games[i];
            Console.WriteLine($"{i + 1,3}. {game.Name} [{game.AppId}]");
        }
    }

    private static string? ResolveSteamApiPath(string? providedPath, SteamGame? selectedGame)
    {
        if (!string.IsNullOrWhiteSpace(providedPath))
        {
            var fullPath = Path.GetFullPath(providedPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        if (selectedGame is not null)
        {
            var discoveredPath = SteamDiscovery.FindSteamApi64(selectedGame);
            if (discoveredPath is not null)
            {
                return discoveredPath;
            }
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "steam_api64.dll"),
            Path.Combine(Environment.CurrentDirectory, "steam_api64.dll")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<int> RunSteamworksLoop(uint appId, SteamGame? selectedGame, string steamApiPath)
    {
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var appDirectory = AppContext.BaseDirectory;
        var appIdFilePath = Path.Combine(appDirectory, "steam_appid.txt");

        Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());

        try
        {
            Directory.SetCurrentDirectory(appDirectory);
            await File.WriteAllTextAsync(appIdFilePath, appId.ToString());

            Console.WriteLine("HourAdder");
            if (selectedGame is not null)
            {
                Console.WriteLine($"Game: {selectedGame.Name}");
                Console.WriteLine($"Install directory: {selectedGame.InstallDirectory}");
            }

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
            Console.WriteLine("Steam should now show the selected game as running.");
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
                await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
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
            // Leaving steam_appid.txt behind is harmless.
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

                case "-l":
                case "--list":
                    options.ListOnly = true;
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

                case "--steam-root":
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    {
                        return false;
                    }

                    options.SteamRoot = args[i];
                    break;

                case "-s":
                case "--search":
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    {
                        return false;
                    }

                    options.SearchQuery = args[i];
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
        Console.WriteLine("HourAdder");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  HourAdder.exe");
        Console.WriteLine("  HourAdder.exe --list");
        Console.WriteLine("  HourAdder.exe --app-id <appid> [--steam-api <path-to-steam_api64.dll>]");
        Console.WriteLine("  HourAdder.exe <appid> [--steam-api <path-to-steam_api64.dll>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --list                 List installed Steam games and exit.");
        Console.WriteLine("  --app-id <appid>        Idle a specific installed Steam AppID.");
        Console.WriteLine("  --steam-api <path>      Use a specific steam_api64.dll.");
        Console.WriteLine("  --steam-root <path>     Use a specific Steam installation directory.");
        Console.WriteLine("  --search <text>         Filter installed games by name, AppID, or path.");
    }

    private sealed class Options
    {
        public uint? AppId { get; set; }
        public string? SteamApiPath { get; set; }
        public string? SteamRoot { get; set; }
        public string? SearchQuery { get; set; }
        public bool ShowHelp { get; set; }
        public bool ListOnly { get; set; }
    }
}
