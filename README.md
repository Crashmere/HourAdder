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

Run with no arguments to list installed games and select one interactively:

```powershell
.\HourAdder.exe
```

List installed games without starting idling:

```powershell
.\HourAdder.exe --list
```

Start a specific AppID and let HourAdder find `steam_api64.dll` automatically:

```powershell
.\HourAdder.exe --app-id 3280350
```

You can still pass `steam_api64.dll` manually when auto-discovery fails:

```powershell
.\HourAdder.exe --app-id 730 --steam-api "C:\Program Files (x86)\Steam\steamapps\common\<Game>\steam_api64.dll"
```

If Steam is installed in an unusual location:

```powershell
.\HourAdder.exe --steam-root "D:\Steam"
```

Stop with `Ctrl+C`.

