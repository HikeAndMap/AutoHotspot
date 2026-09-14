# AutoHotspot

Keeps the Windows **Mobile hotspot** switched on: it starts at every sign-in, turns Wi-Fi on when it's off, retries until the network is ready, and switches the hotspot back on within seconds whenever Windows turns it off.

<img src="AutoHotspot/AutoHotspot.ico" width="64" alt="AutoHotspot icon">

## Why

Windows has no built-in "always on" option for Mobile hotspot. After a restart it stays off, it won't start while Wi-Fi is switched off, and its power-saving setting turns it off when no devices are connected. AutoHotspot takes care of all three.

## How it works

AutoHotspot is a single small exe with two modes:

| Started as | What it does |
|---|---|
| `AutoHotspot.exe` | Opens a dialog with an **Enabled / Disabled** toggle and live status. |
| `AutoHotspot.exe --startup` | Runs in the background with a tray icon and keeps the hotspot on. |

When you **enable** it, the dialog:

1. Adds a sign-in entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` that runs `"<path>\AutoHotspot.exe" --startup`. No administrator rights are needed.
2. Starts the background instance right away, so you don't have to restart.

Every 5 seconds the background instance:

1. Turns the Wi-Fi radio on if it's off (`Windows.Devices.Radios`).
2. Finds a connection to share: the internet connection first, then any other connected network Windows allows sharing.
3. Starts the hotspot if it isn't on (`Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager`).

It **never gives up**. At sign-in the adapters and network often take a while to come up, so it keeps trying and starts the hotspot as soon as the connection is available.

### Notifications

AutoHotspot shows at most two notifications per sign-in:

- **Once** when the hotspot couldn't be started yet, to say it keeps trying.
- **Once** when the hotspot finally comes on after those retries.

Later restarts of the hotspot (for example after Windows' power saving turns it off) happen silently.

### Tray icon

While the background instance runs it shows a tray icon. Hover over it for the current status. Right-click it to:

- **Open AutoHotspot** (opens the dialog)
- **Open log**
- **Stop until next sign-in**

When you **disable** it in the dialog, the sign-in entry is removed and the running background instance stops.

## Good to know

- **Sign-in, not boot.** The hotspot API needs a signed-in user, so AutoHotspot starts when you sign in to Windows.
- **Hotspot name and password** are set in Windows: *Settings > Network & internet > Mobile hotspot*. The dialog has a link to that page.
- **Moving the exe**: open the dialog once from the new location. If it's enabled, the sign-in entry is updated to the new path.
- **Log file**: `%LOCALAPPDATA%\AutoHotspot\log.txt`. Only state changes are logged, and the file is rotated at 1 MB.
- **Radio permission**: if the log says Windows doesn't allow controlling radios, check *Settings > Privacy & security > Radios*.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- A Wi-Fi adapter that supports Mobile hotspot
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0), unless you publish self-contained

## Building

Open `AutoHotspot.sln` in Visual Studio 2022 (17.8 or later), or run:

```bash
dotnet build AutoHotspot.sln -c Release
```

To publish a single exe that doesn't need .NET installed:

```bash
dotnet publish AutoHotspot/AutoHotspot.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The project uses no NuGet packages. The Windows Runtime APIs come from the `net8.0-windows10.0.19041.0` target framework.

## License

[MIT](LICENSE)
