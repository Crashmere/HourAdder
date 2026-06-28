using Microsoft.Win32;
using System.Runtime.Versioning;

internal static class SteamDiscovery
{
    public static SteamDiscoveryResult Discover(string? steamRootOverride = null)
    {
        var steamRoot = ResolveSteamRoot(steamRootOverride);
        if (steamRoot is null)
        {
            return SteamDiscoveryResult.Fail("Could not find the Steam installation directory.");
        }

        var libraries = DiscoverLibraries(steamRoot).ToArray();
        var games = libraries
            .SelectMany(ReadInstalledGames)
            .GroupBy(game => game.AppId)
            .Select(group => group.First())
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(game => game.AppId)
            .ToArray();

        return SteamDiscoveryResult.Ok(steamRoot, libraries, games);
    }

    public static string? FindSteamApi64(SteamGame game)
    {
        if (!Directory.Exists(game.InstallDirectory))
        {
            return null;
        }

        var commonCandidates = new[]
        {
            Path.Combine(game.InstallDirectory, "steam_api64.dll"),
            Path.Combine(game.InstallDirectory, "bin", "steam_api64.dll"),
            Path.Combine(game.InstallDirectory, "Bin64", "steam_api64.dll"),
            Path.Combine(game.InstallDirectory, "Binaries", "Win64", "steam_api64.dll")
        };

        var directMatch = commonCandidates.FirstOrDefault(File.Exists);
        if (directMatch is not null)
        {
            return directMatch;
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            return Directory
                .EnumerateFiles(game.InstallDirectory, "steam_api64.dll", options)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveSteamRoot(string? steamRootOverride)
    {
        if (!string.IsNullOrWhiteSpace(steamRootOverride))
        {
            var fullPath = Path.GetFullPath(steamRootOverride);
            return Directory.Exists(fullPath) ? fullPath : null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var candidates = new[]
        {
            ReadRegistryString(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath"),
            ReadRegistryString(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "InstallPath"),
            ReadRegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            ReadRegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath")
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(Directory.Exists);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryString(string keyName, string valueName)
    {
        try
        {
            return Registry.GetValue(keyName, valueName, null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<SteamLibrary> DiscoverLibraries(string steamRoot)
    {
        var libraryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            steamRoot
        };

        var libraryFoldersPath = Path.Combine(steamRoot, "config", "libraryfolders.vdf");
        if (File.Exists(libraryFoldersPath))
        {
            var tokens = ValveData.ReadQuotedTokens(File.ReadAllText(libraryFoldersPath));
            for (var i = 0; i < tokens.Count - 1; i++)
            {
                if (tokens[i].Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    libraryRoots.Add(tokens[i + 1].Replace(@"\\", @"\"));
                }
            }
        }

        return libraryRoots
            .Select(path => Path.GetFullPath(path))
            .Where(Directory.Exists)
            .Select(path => new SteamLibrary(path, Path.Combine(path, "steamapps")))
            .Where(library => Directory.Exists(library.SteamAppsDirectory))
            .ToArray();
    }

    private static IEnumerable<SteamGame> ReadInstalledGames(SteamLibrary library)
    {
        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(library.SteamAppsDirectory, "appmanifest_*.acf");
        }
        catch
        {
            yield break;
        }

        foreach (var manifestPath in manifests)
        {
            SteamGame? game = null;

            try
            {
                var tokens = ValveData.ReadQuotedTokens(File.ReadAllText(manifestPath));
                var appIdText = ValveData.ValueAfterKey(tokens, "appid");
                var name = ValveData.ValueAfterKey(tokens, "name");
                var installDirName = ValveData.ValueAfterKey(tokens, "installdir");

                if (uint.TryParse(appIdText, out var appId) &&
                    !string.IsNullOrWhiteSpace(name) &&
                    !string.IsNullOrWhiteSpace(installDirName))
                {
                    var installDirectory = Path.Combine(library.SteamAppsDirectory, "common", installDirName);
                    game = new SteamGame(appId, name, installDirectory, manifestPath, library);
                }
            }
            catch
            {
                // Ignore malformed manifests and continue listing the rest of the library.
            }

            if (game is not null)
            {
                yield return game;
            }
        }
    }
}

internal static class ValveData
{
    public static IReadOnlyList<string> ReadQuotedTokens(string text)
    {
        var tokens = new List<string>();

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                continue;
            }

            i++;
            var value = new List<char>();
            while (i < text.Length)
            {
                var current = text[i];
                if (current == '\\' && i + 1 < text.Length)
                {
                    value.Add(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (current == '"')
                {
                    break;
                }

                value.Add(current);
                i++;
            }

            tokens.Add(new string(value.ToArray()));
        }

        return tokens;
    }

    public static string? ValueAfterKey(IReadOnlyList<string> tokens, string key)
    {
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return tokens[i + 1];
            }
        }

        return null;
    }
}

internal sealed record SteamDiscoveryResult(
    bool Success,
    string? ErrorMessage,
    string? SteamRoot,
    IReadOnlyList<SteamLibrary> Libraries,
    IReadOnlyList<SteamGame> Games)
{
    public static SteamDiscoveryResult Ok(
        string steamRoot,
        IReadOnlyList<SteamLibrary> libraries,
        IReadOnlyList<SteamGame> games) =>
        new(true, null, steamRoot, libraries, games);

    public static SteamDiscoveryResult Fail(string errorMessage) =>
        new(false, errorMessage, null, Array.Empty<SteamLibrary>(), Array.Empty<SteamGame>());
}

internal sealed record SteamLibrary(string RootDirectory, string SteamAppsDirectory);

internal sealed record SteamGame(
    uint AppId,
    string Name,
    string InstallDirectory,
    string ManifestPath,
    SteamLibrary Library);
