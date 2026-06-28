# HourAdder

Minimal Steamworks API validation prototype.

This prototype is intentionally small:

- accepts one Steam AppID
- loads an external `steam_api64.dll`
- calls `SteamAPI_Init`
- keeps the process alive until `Ctrl+C`

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

Find a game installation directory that contains `steam_api64.dll`, then run:

```powershell
.\HourAdder.exe --app-id 730 --steam-api "C:\Program Files (x86)\Steam\steamapps\common\<Game>\steam_api64.dll"
```

Stop with `Ctrl+C`.

