# Paqet Manager

<p align="center">
  <img src="assets/logo-dark.png" width="128" alt="Paqet Manager Logo" />
</p>

<p align="center">
  <strong>Modern Windows tunnel manager for bypassing internet censorship</strong><br>
  <em>Built with WPF on .NET 8 — native performance, dark UI, zero external runtimes</em>
</p>

---

## Features

- **One-click tunnel** — Connect/disconnect with live status and speed graph
- **Full system tunnel** — Route ALL traffic through TUN virtual adapter (WinTun + tun2socks)
- **SOCKS5 proxy** — Lightweight browser-only mode on port 10800
- **Auto-install** — Downloads paqet, tun2socks, wintun, Npcap on first run
- **LAN exclusion** — Local network traffic (SSH, printers, etc.) bypasses the tunnel
- **System proxy** — Toggle Windows SOCKS5 proxy with WinINet notification
- **Network sharing** — Port-forward proxy to hotspot/LAN devices
- **Speed monitor** — Real-time download/upload graph
- **System tray** — Compact popup, always accessible, minimal footprint
- **Admin elevation** — App requests admin for TUN adapter and routing
- **Auto-connect** — Optional connect on app start
- **Debug logging** — Full diagnostics to `%LOCALAPPDATA%\PaqetManager\logs\`

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    PaqetManager.exe                       │
│         WPF System Tray App (.NET 8, Admin)              │
├──────────────────┬──────────────────────────────────────┤
│   SOCKS5 Mode    │      Full System Tunnel Mode          │
│                  │                                        │
│  Browser → proxy │  ALL Traffic → TUN Adapter → tun2socks│
│  127.0.0.1:10800 │  10.0.85.2/24 → socks5://127.0.0.1   │
├──────────────────┴──────────────────────────────────────┤
│              paqet (KCP raw-packet tunnel)                │
│              127.0.0.1:10800 → VPS:8443                  │
└─────────────────────────────────────────────────────────┘
```

## Requirements

- Windows 10/11 (x64)
- .NET 8 SDK (for building from source)
- [Npcap](https://npcap.com/) — auto-installed on first run
- Admin privileges (required for TUN adapter and routing)

## Quick Start

```bat
:: Clone
git clone https://github.com/mewoZa/PaqetManager.git
cd PaqetManager

:: Run (development)
dotnet run --project src\PaqetManager

:: Build single-file executable + installer
Build.bat

:: Stop all instances and relaunch
Run.bat

:: Stop everything
Stop.bat
```

## Project Structure

```
PaqetManager/
├── PaqetManager.sln              # Solution file
├── Build.bat                     # One-click release build + installer
├── Run.bat / Stop.bat            # Dev helpers
├── assets/                       # Logo and branding
│   ├── logo.png
│   └── logo-dark.png
├── src/PaqetManager/
│   ├── PaqetManager.csproj       # Project config + NuGet refs
│   ├── app.manifest              # Admin elevation manifest
│   ├── App.xaml / App.xaml.cs     # Entry point, tray icon, single instance
│   ├── AppPaths.cs               # Centralized path management
│   ├── Models/
│   │   └── PaqetConfig.cs        # YAML config model + app settings
│   ├── Services/
│   │   ├── PaqetService.cs       # Process management + GitHub download
│   │   ├── TunService.cs         # TUN tunnel (tun2socks + WinTun + routes)
│   │   ├── ProxyService.cs       # System proxy, port forwarding, firewall
│   │   ├── NetworkMonitorService.cs  # Speed monitoring
│   │   ├── ConfigService.cs      # YAML + JSON config persistence
│   │   ├── SetupService.cs       # Auto-install + migration
│   │   └── Logger.cs             # File-based debug logger
│   ├── ViewModels/
│   │   └── MainViewModel.cs      # MVVM state + commands
│   ├── Views/
│   │   ├── MainWindow.xaml       # Main UI layout
│   │   ├── MainWindow.xaml.cs    # Window chrome, drag, auto-hide
│   │   └── Controls/
│   │       └── SpeedGraph.cs     # Custom speed graph renderer
│   ├── Helpers/
│   │   └── PasswordBoxHelper.cs  # PasswordBox attached behavior
│   ├── Themes/
│   │   └── Dark.xaml             # Dark theme + control styles
│   ├── Converters/
│   │   └── ValueConverters.cs    # Bool→Visibility, color converters
│   └── Assets/
│       └── paqet.ico             # App icon
├── installer/
│   └── PaqetSetup.iss            # InnoSetup installer script
└── .gitignore
```

## Data Directories

All app data is stored under `%LOCALAPPDATA%\PaqetManager\`:

| Path | Contents |
|------|----------|
| `bin\` | `paqet_windows_amd64.exe`, `tun2socks.exe`, `wintun.dll` |
| `config\` | `client.yaml` (server configuration) |
| `logs\` | Debug log files (when debug mode enabled) |
| `settings.json` | App preferences (tunneling mode, auto-connect, etc.) |

On first run, the app automatically migrates files from the old `%USERPROFILE%\paqet\` location.

## Architecture

| Layer | Technology |
|-------|-----------|
| **UI** | WPF + XAML, custom dark theme, frameless window |
| **Pattern** | MVVM with CommunityToolkit.Mvvm (source generators) |
| **Services** | Plain C# classes, no DI container |
| **System** | Win32 interop (WinINet, registry, netsh, tasklist) |
| **Tray** | System.Windows.Forms.NotifyIcon (built-in) |
| **Build** | .NET 8, single-file publish, self-contained |

## Paqet Configuration

Config file: `%LOCALAPPDATA%\PaqetManager\config\client.yaml`

```yaml
role: "client"
password: "your-password"
interface: "Ethernet"

server:
  address: "server.example.com"
  port: 443

socks5:
  - listen: "127.0.0.1:10800"
```

## Proxy Sharing (Hotspot)

When "Share on Network" is enabled:
1. `netsh interface portproxy` forwards `0.0.0.0:10800 → 127.0.0.1:10800`
2. Firewall rule allows inbound TCP 10800
3. Hotspot clients set SOCKS5 proxy to `<this-ip>:10800`

## License

MIT
