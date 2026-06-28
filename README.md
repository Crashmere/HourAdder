# HourAdder

HourAdder is a small Windows CLI tool for idling one locally logged-in Steam account.

Current behavior:

- discovers the local Steam installation
- parses Steam library folders and installed game manifests
- lets you select an installed game from the command line
- finds `steam_api64.dll` in the selected game directory
- initializes Steamworks and keeps the process alive until `Ctrl+C`

It must be run on Windows with Steam installed and already logged in.

## Publish From macOS

```bash
DOTNET_CLI_HOME="../.dotnet_home" dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Copy the whole output directory to Windows, not only `HourAdder.exe`:

```text
artifacts/win-x64/
```

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

Stop with `Ctrl+C`.

