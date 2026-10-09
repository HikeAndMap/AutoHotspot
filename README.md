# AutoHotspot

Keeps the Windows **Mobile hotspot** switched on: it starts at every sign-in, turns Wi-Fi on when it's off, retries until the network is ready, and switches the hotspot back on within seconds whenever Windows turns it off. It also warns you when the hotspot's IP range collides with another network on your PC, which Windows never tells you about, and can move the hotspot to a free range.

<img src="AutoHotspot/AutoHotspot.ico" width="64" alt="AutoHotspot icon">

## Download

**[Download AutoHotspot.exe](https://github.com/HikeAndMap/AutoHotspot/releases/latest/download/AutoHotspot.exe)** (64-bit Windows 10 version 2004 or later, or Windows 11)

One file, no installer and no .NET needed. Put it anywhere you like, such as your Documents folder, run it, and switch it on. It doesn't need administrator rights for everyday use (only [changing the hotspot IP](#hotspot-ip-conflicts) asks for permission), and uninstalling means switching it off in the dialog and deleting the file.

The exe isn't code-signed, so the first time you run it Windows shows *"Windows protected your PC"*. Click **More info**, then **Run anyway**. You can check the download against the SHA256 listed on the [release page](https://github.com/HikeAndMap/AutoHotspot/releases/latest):

```powershell
Get-FileHash .\AutoHotspot.exe -Algorithm SHA256
```

## Why

Windows has no built-in "always on" option for Mobile hotspot. After a restart it stays off, it won't start while Wi-Fi is switched off, and its power-saving setting turns it off when no devices are connected. AutoHotspot takes care of all three.

## How it works

AutoHotspot is a single small exe with three modes:

| Started as | What it does |
|---|---|
| `AutoHotspot.exe` | Opens a dialog with an **Enabled / Disabled** toggle and live status. |
| `AutoHotspot.exe --startup` | Runs in the background with a tray icon and keeps the hotspot on. |
| `AutoHotspot.exe --set-hotspot-ip a.b.c.d` | Used internally by **Change IP...** (see [Hotspot IP conflicts](#hotspot-ip-conflicts)). It runs elevated after a permission prompt, changes the hotspot address and exits. |

When you **enable** it, the dialog:

1. Adds a sign-in entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` that runs `"<path>\AutoHotspot.exe" --startup`. No administrator rights are needed.
2. Starts the background instance right away, so you don't have to restart.

Every 5 seconds the background instance:

1. Turns the Wi-Fi radio on if it's off (`Windows.Devices.Radios`).
2. Finds a connection to share: the internet connection first, then any other connected network Windows allows sharing.
3. Starts the hotspot if it isn't on (`Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager`).

It **never gives up**. At sign-in the adapters and network often take a while to come up, so it keeps trying and starts the hotspot as soon as the connection is available.

### Notifications

AutoHotspot shows at most two notifications about the hotspot itself per sign-in:

- **Once** when the hotspot couldn't be started yet, to say it keeps trying.
- **Once** when the hotspot finally comes on after those retries.

Later restarts of the hotspot (for example after Windows' power saving turns it off) happen silently.

A hotspot IP problem (next section) gets its own notification, once per distinct problem per sign-in.

### Hotspot IP conflicts

The Windows hotspot hands out addresses from one fixed range, **192.168.137.0/24** by default. If your PC is also connected to a network, VPN or virtual adapter in the same range, the hotspot keeps dropping or its devices can't reach anything, and Windows doesn't say why. Typical cause: an IT department that assigned `192.168.137.x` addresses, the range Microsoft reserves for the Mobile hotspot. **IT should not use 192.168.137.x**; AutoHotspot is a workaround for when it does.

The background instance checks this at sign-in and again whenever Windows reports that an IP address changed (not on a timer). It compares the hotspot range against:

- the IPv4 address of every other network adapter (VPN adapters included), and
- the routing table, so a VPN route that covers the range counts too.

It reads the range the hotspot is set to from `HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters` (`ScopeAddress`, falling back to Microsoft's default `192.168.137.1`) and the address the hotspot's virtual adapter (for example `Local Area Connection* 10`) really has. It also reports when the two differ, which happens when the setting was changed but the Windows sharing service (`SharedAccess`) was not restarted.

When it finds a problem, a notification names the adapter or route and the subnet it collides with. The same problem is announced once per sign-in. Click the notification, use **Change hotspot IP...** in the tray menu, or press **Change IP...** in the dialog (which also shows the current hotspot IP and flags problems in red).

**Change IP...** does this:

1. Picks a new /24 that overlaps nothing on the PC (all adapters and all routes): `192.168.50.x` first, then `192.168.51.x` and so on up to `192.168.99.x`, then `10.77.50-99.x` and `172.30.50-99.x`. If the address in the registry is already free and only wasn't applied yet, it offers that one instead.
2. Shows it to you and asks for confirmation. Nothing happens before you say yes.
3. Asks Windows for permission (UAC) and runs an elevated copy of the exe that: switches the hotspot off, stops the `SharedAccess` service, writes both `ScopeAddress` and `ScopeAddressBackup`, starts the service, switches the hotspot on, and finally reads the hotspot adapter's real address again.

If a step fails, the previous registry values are restored and the service and hotspot are brought back, so a failed change never leaves sharing stopped. If the adapter still shows the old address afterwards, AutoHotspot tells you that restarting Windows may be necessary. The background keep-alive loop pauses while the change runs, so it doesn't start the hotspot halfway through.

Devices connected to the hotspot reconnect on their own after the change. The hotspot name and password stay the same.

### Tray icon

While the background instance runs it shows a tray icon. Hover over it for the current status. Right-click it to:

- **Open AutoHotspot** (opens the dialog)
- **Change hotspot IP...** (see [Hotspot IP conflicts](#hotspot-ip-conflicts))
- **Open log**
- **Stop until next sign-in**

When you **disable** it in the dialog, the sign-in entry is removed and the running background instance stops.

## Good to know

- **Sign-in, not boot.** The hotspot API needs a signed-in user, so AutoHotspot starts when you sign in to Windows.
- **Hotspot name and password** are set in Windows: *Settings > Network & internet > Mobile hotspot*. The dialog has a link to that page.
- **Moving the exe**: open the dialog once from the new location. If it's enabled, the sign-in entry is updated to the new path.
- **Administrator rights**: only the hotspot IP change needs them, through a normal Windows permission prompt. Declining it changes nothing.
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

The pure subnet-overlap and range-picking logic has tests in `AutoHotspot.Tests`, a plain console runner (no test framework, since the project uses no NuGet packages). They don't touch the registry, adapters or services:

```bash
dotnet run --project AutoHotspot.Tests
```

The project uses no NuGet packages. The Windows Runtime APIs come from the `net8.0-windows10.0.19041.0` target framework.

## License

[MIT](LICENSE)
