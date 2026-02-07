# Paqet Tunnel

<p align="center">
  <img src="assets/logo-dark.png" width="128" alt="Paqet Tunnel Logo" />
</p>

<p align="center">
  <strong>Modern Windows tunnel manager for bypassing internet censorship</strong><br>
  <em>Built with WPF on .NET 8 — native performance, dark UI, zero external runtimes</em>
</p>

---

## About

Paqet Tunnel is a Windows desktop application that provides a user-friendly interface for managing encrypted KCP tunnels. It wraps the [**paqet**](https://github.com/hanselime/paqet) tunnel engine (written in Go by [hanselime](https://github.com/hanselime)) and adds full system tunneling, a modern dark UI, and automated setup.

> **Note:** The paqet tunnel engine is a third-party open-source project. We fork and compile it from source as a [git submodule](paqet/). All credit for the core tunneling technology goes to [hanselime/paqet](https://github.com/hanselime/paqet).

## Features

- **One-click connect** — Connect/disconnect with live status and speed graph
- **Full system tunnel** — Route ALL traffic through a TUN virtual adapter (WinTun + tun2socks)
- **SOCKS5 proxy** — Lightweight browser-only mode on port 10800
- **Auto-install** — Downloads and installs all dependencies on first run
- **LAN exclusion** — Local network traffic (SSH, printers, etc.) bypasses the tunnel
- **System proxy** — Toggle Windows SOCKS5 proxy with automatic save/restore
- **Network sharing** — Forward proxy to hotspot/LAN devices
- **Speed monitor** — Real-time download/upload speed with live graph
- **System tray** — Compact popup, minimal footprint, always accessible
- **Auto-connect** — Optional connect on app launch or Windows startup
- **Debug logging** — Full diagnostics to `%LOCALAPPDATA%\PaqetTunnel\logs\`
- **Self-compiled paqet** — Built from source via git submodule, no pre-built binaries

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                    PaqetTunnel.exe                       │
│         WPF System Tray App (.NET 8, Admin)              │
├──────────────────┬───────────────────────────────────────┤
│   SOCKS5 Mode    │      Full System Tunnel Mode          │
│                  │                                       │
│  Browser → proxy │  ALL Traffic → TUN → tun2socks        │
│  127.0.0.1:10800 │  10.0.85.2/24 → socks5://127.0.0.1   │
├──────────────────┴───────────────────────────────────────┤
│              paqet (KCP encrypted tunnel)                 │
│              127.0.0.1:10800 ←→ VPS:8443                 │
└──────────────────────────────────────────────────────────┘
```

**Full System Tunnel** creates a virtual network adapter (WinTun), routes all system traffic through it via tun2socks, which forwards to the local SOCKS5 proxy provided by paqet. LAN traffic (192.168.x.x, 10.x.x.x, etc.) is excluded to preserve local connectivity.

## Requirements

- Windows 10/11 (x64)
- Admin privileges (required for TUN adapter and routing)
- [Npcap](https://npcap.com/) — auto-installed on first run
- .NET 8 SDK + Go 1.25+ + MinGW (for building from source)

## Quick Start

### One-liner install (Windows)

```powershell
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1
```

Or with arguments:
```powershell
.\setup.ps1 install -Addr 1.2.3.4:8443 -Key "your-secret-key"
```

### Linux server

```bash
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh | sudo bash
```

### Setup script commands

| Command | Description |
|---------|-------------|
| `setup.ps1 install` | Install client (auto-installs all dependencies) |
| `setup.ps1 update` | Update to latest version |
| `setup.ps1 uninstall` | Remove everything |
| `setup.ps1 status` | Show installation status |
| `setup.ps1 server` | Install paqet server mode |
| `setup.ps1 help` | Show all options |

The setup script automatically installs Git, .NET 8 SDK, Go, Inno Setup, and all runtime dependencies. Works on any Windows architecture (x64, ARM64).

## Project Structure

```
PaqetTunnel/
├── setup.ps1                     # Universal setup & management (Windows)
├── setup.sh                      # Server setup (Linux)
├── paqet/                        # Git submodule (mewoZa/paqet fork)
├── assets/                       # Logo and branding
├── src/PaqetTunnel/
│   ├── App.xaml.cs               # Entry point, tray icon, single instance
│   ├── AppPaths.cs               # Centralized path management
│   ├── app.manifest              # Admin elevation manifest
│   ├── Models/
│   │   └── PaqetConfig.cs        # YAML config model + settings
│   ├── Services/
│   │   ├── PaqetService.cs       # paqet process management
│   │   ├── TunService.cs         # TUN tunnel (tun2socks + routes + DNS)
│   │   ├── ProxyService.cs       # System proxy save/restore, firewall
│   │   ├── NetworkMonitorService.cs
│   │   ├── ConfigService.cs      # YAML + JSON persistence
│   │   ├── SetupService.cs       # Auto-install + migration
│   │   ├── UpdateService.cs      # GitHub auto-update checker
│   │   └── Logger.cs             # File-based debug logger
│   ├── ViewModels/
│   │   └── MainViewModel.cs      # MVVM state + commands
│   ├── Views/
│   │   ├── MainWindow.xaml       # UI layout
│   │   └── Controls/SpeedGraph.cs
│   ├── Themes/Dark.xaml          # Dark theme + control styles
│   └── Converters/ValueConverters.cs
├── installer/
│   └── PaqetSetup.iss            # InnoSetup installer script
└── .gitignore
```

## Data Directories

All data stored under `%LOCALAPPDATA%\PaqetTunnel\`:

| Path | Contents |
|------|----------|
| `bin\` | `paqet_windows_amd64.exe`, `tun2socks.exe`, `wintun.dll` |
| `config\` | `client.yaml` (tunnel configuration) |
| `logs\` | Debug logs (when enabled) |
| `settings.json` | App preferences |

## Configuration

Config file: `%LOCALAPPDATA%\PaqetTunnel\config\client.yaml`

```yaml
role: "client"
password: "your-key"
interface: "Ethernet"

server:
  address: "your-server.com"
  port: 8443

socks5:
  - listen: "127.0.0.1:10800"
```

## Technical Details

| Component | Technology |
|-----------|-----------|
| UI | WPF, custom dark theme, frameless window |
| Pattern | MVVM (CommunityToolkit.Mvvm source generators) |
| Tunnel | paqet — Go, KCP protocol, compiled from source |
| TUN | tun2socks + WinTun driver |
| Build | .NET 8 self-contained single-file + InnoSetup |
| System | Win32 interop (WinINet, registry, netsh) |

## Credits

- **[paqet](https://github.com/hanselime/paqet)** by [hanselime](https://github.com/hanselime) — The core KCP tunnel engine
- **[tun2socks](https://github.com/xjasonlyu/tun2socks)** — TUN-to-SOCKS5 proxy
- **[WinTun](https://www.wintun.net/)** — Layer 3 TUN driver for Windows
- **[Npcap](https://npcap.com/)** — Windows packet capture library

## License

MIT
