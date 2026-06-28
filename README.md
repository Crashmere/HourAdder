# HourAdder

HourAdder is a small Windows CLI tool for idling one locally logged-in Steam account.

Current behavior:

- discovers the local Steam installation
- parses Steam library folders and installed game manifests
- lets you select an installed game from the command line
- finds `steam_api64.dll` in the selected game directory
- checks whether Steam is running and signed in, and shows the signed-in account
- initializes Steamworks and keeps the process alive until `Ctrl+C`
- shows a live elapsed-time counter while idling and the total time on exit

It must be run on Windows with Steam installed and already logged in.

## Publish From macOS

Publishing for a runtime ID produces a single, trimmed, compressed `HourAdder.exe`
(about 11 MB) with the .NET runtime bundled in, so the Windows machine does not need
.NET installed.

```bash
DOTNET_CLI_HOME="../.dotnet_home" dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

The output is a single file:

```text
artifacts/win-x64/HourAdder.exe
```

Copy just that `HourAdder.exe` to Windows; no other files are required.

Notes:

- The single-file/trim/compression settings only activate when a runtime ID is
  specified, so a plain `dotnet build` (without `-r`) stays offline-friendly on macOS.
- NativeAOT would produce an even smaller native binary, but it cannot cross-compile
  from macOS to `win-x64`, so HourAdder uses single-file plus trimming instead.

## Run On Windows

### Interactive Mode

Run with no arguments to scan Steam libraries, list installed games, and choose one interactively:

```powershell
.\HourAdder.exe
```

The prompt accepts several kinds of input:

- Enter the list number to start the game shown on that row.
- Enter a Steam AppID to start that installed game directly.
- Enter search text to filter the visible list by game name, AppID, or install path.
- Enter `q` or `quit` to exit without starting anything.

Example flow:

```text
  1. Counter-Strike 2 [730]
  2. DEATH STRANDING 2: ON THE BEACH [3280350]

Select by number/AppID, type search text, or q to quit: death

  1. DEATH STRANDING 2: ON THE BEACH [3280350]

Select by number/AppID, type search text, or q to quit: 1
```

### Search And List

List installed games without starting idling:

```powershell
.\HourAdder.exe --list
```

Filter the installed game list with `--search`:

```powershell
.\HourAdder.exe --list --search "death stranding"
```

Start interactive mode with an initial filter:

```powershell
.\HourAdder.exe --search "3280350"
```

Search terms are split by spaces. All terms must match somewhere in the game name, AppID, or install path.

### Start By AppID

Start a specific AppID and let HourAdder find `steam_api64.dll` automatically:

```powershell
.\HourAdder.exe --app-id 3280350
```

### Manual Overrides

You can still pass `steam_api64.dll` manually when auto-discovery fails:

```powershell
.\HourAdder.exe --app-id 730 --steam-api "C:\Program Files (x86)\Steam\steamapps\common\<Game>\steam_api64.dll"
```

If Steam is installed in an unusual location:

```powershell
.\HourAdder.exe --steam-root "D:\Steam"
```

### While Idling

Before initializing, HourAdder reads the local Steam status and warns you if Steam is
not running or no account is signed in. When it can be read, the signed-in Steam
account name is shown. While idling, a live status line shows the elapsed time, and the
console window title is updated to match:

```text
HourAdder
Game: DEATH STRANDING 2: ON THE BEACH
AppID: 3280350
steam_api64.dll: C:\Program Files (x86)\Steam\steamapps\common\DEATH STRANDING 2 - ON THE BEACH\steam_api64.dll
Steam account: your_login_name

Initializing Steamworks...
Steamworks initialized successfully (method: SteamAPI_InitSafe).
Steam should now show "DEATH STRANDING 2: ON THE BEACH" as running.
Press Ctrl+C to stop.

Idling "DEATH STRANDING 2: ON THE BEACH" - elapsed 00:03:42
```

Press `Ctrl+C` to stop. HourAdder shuts Steamworks down cleanly and prints the total
idle time:

```text
Stopped idling "DEATH STRANDING 2: ON THE BEACH". Total time: 00:03:42
```

