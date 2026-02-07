# Paqet Manager

Modern Windows system tray application for managing the [paqet](https://github.com/hanselime/paqet) raw-packet proxy tunnel.

Built with **WPF on .NET 8** — native Windows performance, modern dark UI, zero external runtimes.

## Features

- **One-click tunnel** — Big connect/disconnect button with live status
- **Auto-install** — Downloads paqet binary from GitHub releases + Npcap on first run
- **Migration** — Automatically imports config from old `%USERPROFILE%\paqet\` on first run
- **System proxy** — Toggle Windows SOCKS5 proxy with WinINet notification
- **Network sharing** — Port-forward proxy to hotspot devices (UAC elevated)
- **Speed monitor** — Real-time download/upload graph via lightweight `netstat -e`
- **System tray** — Compact popup window, always accessible, minimal footprint
- **Single instance** — Mutex-based, broadcasts show message to existing instance
- **Auto-start** — Windows startup via registry

## Requirements

- Windows 10/11 (x64)
- .NET 8 SDK (for building from source)
- [Npcap](https://npcap.com/) — auto-installed on first run

## Quick Start

```bat
:: Clone
git clone https://github.com/hanselime/PaqetManager.git
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
├── Run.bat                       # Stop all + launch app
├── Stop.bat                      # Kill all paqet processes
├── src/PaqetManager/
│   ├── PaqetManager.csproj       # Project config + NuGet refs
│   ├── App.xaml / App.xaml.cs     # Entry point, tray icon, single instance
│   ├── AppPaths.cs               # Centralized path management
│   ├── Models/
│   │   └── PaqetConfig.cs        # YAML config model + app settings
│   ├── Services/
│   │   ├── PaqetService.cs       # Process management + GitHub download
│   │   ├── ProxyService.cs       # System proxy, port forwarding, firewall
│   │   ├── NetworkMonitorService.cs  # Speed monitoring (netstat -e)
│   │   ├── ConfigService.cs      # YAML + JSON config persistence
│   │   └── SetupService.cs       # Auto-install + migration orchestration
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
│   │   └── Dark.xaml             # Complete dark theme + control styles
│   ├── Converters/
│   │   └── ValueConverters.cs    # Bool→Visibility, color converters
│   └── Assets/
│       └── paqet.ico             # App icon (generated)
├── installer/
│   └── PaqetSetup.iss            # InnoSetup installer script
├── .github/
│   └── copilot-instructions.md   # AI development guidelines
└── .gitignore
```

## Data Directories

All app data is stored under `%LOCALAPPDATA%\PaqetManager\`:

| Path | Contents |
|------|----------|
| `bin\` | `paqet_windows_amd64.exe` (proxy binary) |
| `config\` | `client.yaml` (server configuration) |
| `settings.json` | App preferences (proxy sharing, auto-start flags) |

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
  - listen: "0.0.0.0:1080"
```

## Proxy Sharing (Hotspot)

When "Share on Network" is enabled:
1. `netsh interface portproxy` forwards `0.0.0.0:1080 → 127.0.0.1:1080`
2. Firewall rule allows inbound TCP 1080
3. Hotspot clients set SOCKS5 proxy to `<this-ip>:1080`

## License

MIT
