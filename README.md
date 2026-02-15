<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/logo.svg" />
  <source media="(prefers-color-scheme: light)" srcset="assets/logo.svg" />
  <img src="assets/logo.svg" width="160" alt="Paqet Tunnel" />
</picture>

# Paqet Tunnel

**Encrypted KCP tunnel with a modern Windows GUI**

*One-click setup · Full system tunnel · Smart DNS · 10 themes · LAN sharing*

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078d4.svg)](#)

<img src="assets/screenshot1.png" width="280" alt="Paqet Tunnel — Settings" />&nbsp;&nbsp;<img src="assets/screenshot2.png" width="280" alt="Paqet Tunnel — Connected" />

---

### 💝 Support Development

If you find this project useful, consider supporting its development!  
*Even just one coffee helps — every bit of support keeps the project alive and thriving.* ☕✨

**Solana:**  
`D8GLXGSkBku64Z5GRdmBnLyb6zrCLuxa8ydnZS3z3Ni1`

**Ethereum:**  
`0x022958603a48078718D4fE940b8eC1D972D003b7`

</div>

---

## 🌐 About

**Break through internet restrictions, beautifully.** PaqetTunnel is a high-performance encrypted VPN that combines military-grade security with a gorgeous, intuitive interface. Built on the blazing-fast KCP protocol, it delivers what others can't: **undetectable tunneling that looks like normal UDP traffic**, bypassing even the most sophisticated censorship systems.

**Why PaqetTunnel?**

- **🚀 KCP Protocol** — Up to 30% faster than TCP-based VPNs, designed for unreliable networks
- **🔒 Encrypted & Stealthy** — XChaCha20-Poly1305 encryption wrapped in UDP that mimics game traffic
- **🎨 Beautiful by Design** — Modern WPF interface with 10 stunning themes (Dark, Nord, Sakura, Cyberpunk...)
- **⚡ One-Click Setup** — Server + client install in under 60 seconds, fully automated
- **🌍 Full System Tunnel** — Route all traffic through the VPN, or use SOCKS5 proxy mode
- **🧠 Smart DNS** — Auto-benchmark 18 DNS providers, picks the fastest for you
- **🏠 LAN Sharing** — Share your tunnel with other devices on your network
- **🔧 Zero Config** — Auto-detects network interface, router MAC, optimal settings

Unlike bloated commercial VPNs, PaqetTunnel gives you **full control** — run your own server, own your data, pay only for the VPS. No monthly subscriptions, no bandwidth caps, no privacy compromises.

---

## ⚡ Install

### 1. Server (Linux VPS)

```bash
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh -o /tmp/pt.sh && sudo bash /tmp/pt.sh
```

### 2. Client (Windows — PowerShell as Admin)

```powershell
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1
```

### Auto Setup

After server install, it prints the exact Windows command with your IP and key — just copy and paste:

```powershell
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1 install -Addr YOUR_SERVER:8443 -Key "YOUR_KEY" -y
```

> **Both scripts show an interactive menu** — install, update, uninstall, status, all from one command.

### 🤖 AI Agent Setup

Want your AI assistant (ChatGPT, Claude, Copilot, etc.) to set it up for you? Copy the block below and paste it to your AI agent:

<details>
<summary><b>📋 Copy this to your AI agent for server setup (click to expand)</b></summary>

```
Install Paqet Tunnel VPN server on this Linux VPS.
Run: curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh -o /tmp/pt.sh && sudo bash /tmp/pt.sh install --yes
It auto-detects everything. After install, save the printed Windows client command (has IP + key).
Port 8443/UDP must be open. Service: systemctl {status|restart|stop} paqet. Logs: journalctl -u paqet -f
Management: sudo bash /tmp/pt.sh {status|update|restart|uninstall|logs}
```

</details>

<details>
<summary><b>📋 Copy this to your AI agent for client setup (click to expand)</b></summary>

```
Install Paqet Tunnel VPN client on this Windows machine.
Run in PowerShell as Admin: irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1 install -Addr SERVER_IP:8443 -Key "SECRET_KEY" -y
Replace SERVER_IP and SECRET_KEY with values from server setup.
It downloads binaries, builds GUI, detects network, creates config, adds shortcuts, sets auto-start.
Install path: %LOCALAPPDATA%\PaqetTunnel\. SOCKS5 proxy: 127.0.0.1:10800.
Management: & $env:TEMP\pt.ps1 {status|update|uninstall}
```

</details>

> 📖 For a comprehensive agent guide with troubleshooting and full technical details, see [**AGENT_SETUP_GUIDE.md**](AGENT_SETUP_GUIDE.md).

---

## 🚀 Getting Started

1. **Install the server** on your Linux VPS using the command above
2. **Copy the Windows command** printed at the end of server install (contains your IP + key)
3. **Run the Windows command** in PowerShell as Admin — it installs everything automatically
4. **Launch Paqet Tunnel** from the desktop shortcut or Start Menu
5. **Click the power button** to connect — status changes to "Connected" with a green indicator
6. **Verify** — visit [whatismyipaddress.com](https://whatismyipaddress.com) — it should show your VPS IP

> **Note:** The Windows installer auto-downloads Git and .NET 8 SDK if not present (~250 MB total). First install takes 2–5 minutes depending on your connection.

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| 🔒 **Full System Tunnel** | Routes all traffic through a TUN virtual adapter via WinTun + tun2socks |
| 🌐 **SOCKS5 Proxy** | Lightweight browser-only mode on `127.0.0.1:10800` |
| ⚡ **KCP Protocol** | UDP-based encrypted transport — faster than TCP in lossy networks |
| 🎯 **Smart DNS** | 17 DNS providers with auto-benchmark to find the fastest |
| 🛡️ **DNS Leak Prevention** | Forces DNS on all network adapters to prevent leaks |
| 📡 **LAN Sharing** | Share the tunnel with other devices on your network via port `10801` |
| 🎨 **10 Themes** | Dark, Light, Nord, Sakura, Ocean, Sunset, Cyberpunk, Dracula, Monokai, Rose |
| 🔄 **Auto-Connect** | Reconnect on start, auto-recover from drops (up to 5 retries) |
| 🚀 **Start with Windows** | Launch at logon, or at boot (before logon) as SYSTEM service |
| 📊 **Live Monitoring** | Real-time upload/download speed, health checks, process stats |
| 🖥️ **System Tray** | Minimal footprint — runs silently in the taskbar |
| 🩺 **CLI Tools** | 10+ commands: `--diag`, `--dns`, `--ping`, `--speed`, `--info`, `--check`, `--update`, `--server` |

---

## 🏗️ Architecture

Paqet Tunnel has two modes: **SOCKS5 Proxy** (browser/app-level) and **Full System Tunnel** (all traffic).

```
  MODE 1: SOCKS5 PROXY              MODE 2: FULL SYSTEM TUNNEL
  (browser/app only)                (all system traffic)

  ┌─────────────────┐              ┌─────────────────────────┐
  │  Browser / App  │              │  All System Traffic     │
  │   ↓ proxy.pac   │              │   ↓                     │
  └────────┬────────┘              │  ┌───────────────┐      │
           │                       │  │ WinTun 10.0.85│      │
           │                       │  └───────┬───────┘      │
           │                       │  ┌───────┴───────┐      │
           │                       │  │   tun2socks   │      │
           │                       │  │  TUN → SOCKS5 │      │
           │                       │  └───────┬───────┘      │
           │                       └──────────┼──────────────┘
           │                                  │
           └──────────┬───────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────────────┐
│  Windows Client (PaqetTunnel.exe manages everything)        │
│                                                             │
│            ┌──────────────────────┐                         │
│            │   paqet (client)     │                         │
│            │   SOCKS5 :10800     │                         │
│            │   KCP encrypted     │                         │
│            └──────────┬──────────┘                         │
│                       │ raw pcap — bypasses OS routing      │
│                       │ UDP/KCP encrypted                   │
└───────────────────────┼─────────────────────────────────────┘
                        │
         ══════════════ Internet ═══════════════
                        │
┌───────────────────────┼─────────────────────────────────────┐
│  Linux Server (VPS)   │                                     │
│            ┌──────────┴──────────┐                         │
│            │   paqet (server)    │──▶ Internet              │
│            │   KCP :8443         │                         │
│            └─────────────────────┘                         │
│   raw pcap — bypasses OS routing ∙ iptables NOTRACK+RST    │
│   systemd hardened (NoNewPrivileges, ProtectHome)          │
└─────────────────────────────────────────────────────────────┘
```

### How It Works

1. **Server** — paqet listens on your VPS using KCP encrypted transport over raw pcap (gopacket), bypassing the OS routing table entirely
2. **Client** — paqet connects via KCP and exposes a local SOCKS5 proxy on `127.0.0.1:10800`
3. **SOCKS5 mode** — browsers and apps use the proxy via a PAC file; lightweight, per-app control
4. **TUN mode** — WinTun creates a virtual adapter (`10.0.85.2`), tun2socks translates all system packets to SOCKS5 → forces *everything* through the tunnel
5. **DNS** — forced on all adapters (not just the default) to prevent leaks; 17 providers with auto-benchmark
6. **LAN sharing** — portproxy forwards `0.0.0.0:10801` → `127.0.0.1:10800` so other devices on your network can use the tunnel
7. **No server bypass route needed** — paqet uses raw pcap on both sides, so the tunnel traffic doesn't hit the OS routing table

### Key Design Decisions

| Decision | Why |
|----------|-----|
| Port **10800** (not 1080) | Windows ICS/svchost grabs 1080; 10800 avoids conflicts |
| Raw pcap (gopacket) | No routing loops — tunnel traffic bypasses the OS network stack |
| PAC file for system proxy | Browsers natively read PAC; more reliable than manual proxy settings |
| DNS on **all** adapters | Prevents apps from using ISP DNS if they bind to the wrong adapter |
| Portproxy for LAN sharing | Uses built-in Windows `netsh` — no extra software needed |

### Config Sync (Client ↔ Server)

Paqet has no handshake protocol — crypto parameters must match exactly on both sides. PaqetTunnel automatically detects when a config change requires server-side sync and orchestrates the update safely.

**Breaking fields** (must match): `key`, `block` (cipher), `mode`, `mtu`
**Performance fields** (recommended to match): `rcvwnd`, `sndwnd`, `smuxbuf`, `streambuf`
**Client-only fields** (no sync needed): SOCKS5 settings, log level, TCP flags, buffers

**Sync flow** (when breaking fields change):
1. Pre-flight: verify SSH connection works
2. Backup: `server.yaml` → `server.yaml.bak`
3. Patch: update changed fields via SSH
4. Schedule delayed server restart (`nohup sleep 2 && systemctl restart paqet`)
5. Save local config immediately (while old tunnel still alive)
6. Disconnect → wait for server restart → reconnect with new config
7. **Rollback** on failure: restore backup via direct SSH (bypassing tunnel)

---

## 🎨 Themes

| Theme | Style | Vibe |
|-------|-------|------|
| 🌑 **Dark** | GitHub-inspired dark | Clean, professional |
| ☀️ **Light** | Bright, airy | Daytime comfort |
| ❄️ **Nord** | Arctic blue palette | Calm, focused |
| 🌸 **Sakura** | Cherry blossom pink | Soft, anime-inspired |
| 🌊 **Ocean** | Deep sea blue | Immersive, cool |
| 🌅 **Sunset** | Warm amber glow | Cozy, evening |
| 🔮 **Cyberpunk** | Neon pink/purple | Sci-fi, electric |
| 🧛 **Dracula** | Classic dev purple | Iconic, easy on eyes |
| 🖥️ **Monokai** | Retro dev green | Nostalgic, hacker |
| 🌹 **Rose** | Elegant rosé | Refined, soft |

Switch themes instantly from Settings — no restart needed.

---

## 🧬 DNS Providers

Built-in smart DNS with auto-benchmark to find the fastest provider:

| Provider | Primary | Secondary |
|----------|---------|-----------|
| Cloudflare | `1.1.1.1` | `1.0.0.1` |
| Cloudflare Malware | `1.1.1.2` | `1.0.0.2` |
| Cloudflare Family | `1.1.1.3` | `1.0.0.3` |
| Google | `8.8.8.8` | `8.8.4.4` |
| Quad9 | `9.9.9.9` | `149.112.112.112` |
| OpenDNS | `208.67.222.222` | `208.67.220.220` |
| AdGuard | `94.140.14.14` | `94.140.15.15` |
| AdGuard Family | `94.140.14.15` | `94.140.15.16` |
| NextDNS | `45.90.28.0` | `45.90.30.0` |
| CleanBrowsing Security | `185.228.168.9` | `185.228.169.9` |
| CleanBrowsing Family | `185.228.168.168` | `185.228.169.168` |
| DNS.SB | `185.222.222.222` | `45.11.45.11` |
| Comodo Secure | `8.26.56.26` | `8.20.247.20` |
| Verisign | `64.6.64.6` | `64.6.65.6` |
| Control D | `76.76.2.0` | `76.76.10.0` |
| Level3/Lumen | `4.2.2.1` | `4.2.2.2` |
| Mullvad | `194.242.2.2` | `193.19.108.2` |

Use **Auto** mode to benchmark all providers and select the fastest, or pick manually from Settings.

---

## 🩺 CLI Tools

PaqetTunnel doubles as a full command-line toolkit — run diagnostics, manage updates, and control your VPS server all from the terminal.

```
PaqetTunnel.exe [--command] [options]
```

| Command | Description |
|---------|-------------|
| `--diag` | Run full diagnostic suite |
| `--dns` | Benchmark DNS providers |
| `--ping` | Test tunnel connectivity |
| `--speed` | Speed test (tunnel vs direct) |
| `--info` | Show install & config status |
| `--check` | Check for updates |
| `--update` | Install latest update |
| `--server <cmd>` | Manage VPS over SSH |
| `--help` | Show all commands |

> 📖 Full CLI reference with examples and sample output → [Advanced: CLI Reference](#-cli-reference)

---

## 📋 Setup Script Commands

Both scripts show an **interactive menu** when run without arguments, or accept commands directly:

| Command | Windows | Linux |
|---------|---------|-------|
| **Menu** | `& $env:TEMP\pt.ps1` | `sudo bash /tmp/pt.sh` |
| **Install** | `& $env:TEMP\pt.ps1 install` | `sudo bash /tmp/pt.sh install` |
| **Update** | `& $env:TEMP\pt.ps1 update` | `sudo bash /tmp/pt.sh update` |
| **Uninstall** | `& $env:TEMP\pt.ps1 uninstall` | `sudo bash /tmp/pt.sh uninstall` |
| **Status** | `& $env:TEMP\pt.ps1 status` | `sudo bash /tmp/pt.sh status` |

> 📖 Full flag reference and examples → [Advanced: Setup Script Reference](#-setup-script-reference)

---

## 🛠️ Building from Source

### Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows)
- [Go 1.23+](https://go.dev/dl/) with CGO enabled (for building paqet from source)
- Linux server: `build-essential`, `libpcap-dev`

### Build the GUI

```powershell
dotnet publish src/PaqetTunnel/PaqetTunnel.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

### Build paqet from source

```bash
# Linux
CGO_ENABLED=1 go build -o paqet ./cmd/main.go

# Windows (requires MinGW + Npcap)
set CGO_ENABLED=1
go build -o paqet.exe ./cmd/main.go
```

---

## 📁 Project Structure

```
PaqetTunnel/
├── src/PaqetTunnel/             # WPF application (.NET 8, MVVM)
│   ├── Views/                   # XAML UI (MainWindow + controls)
│   ├── ViewModels/              # MainViewModel (connection, settings, diagnostics)
│   ├── Services/                # Core services
│   │   ├── PaqetService.cs      # paqet binary: start, stop, download, health check
│   │   ├── TunService.cs        # TUN adapter: WinTun + tun2socks + routing
│   │   ├── DnsService.cs        # DNS: 17 providers, benchmark, leak prevention
│   │   ├── ProxyService.cs      # System proxy (PAC), LAN sharing (portproxy), auto-start
│   │   ├── ConfigService.cs     # YAML config + app settings management
│   │   ├── SshService.cs        # SSH server management (install, update, logs)
│   │   ├── DiagnosticService.cs # Latency/throughput benchmarks + reports
│   │   ├── NetworkMonitorService.cs  # Real-time speed tracking
│   │   ├── UpdateService.cs     # App update checker
│   │   ├── CredentialHelper.cs  # AES encryption for stored credentials
│   │   ├── ThemeManager.cs      # Runtime theme switching (10 themes)
│   │   └── Logger.cs            # Centralized file logging
│   ├── Models/                  # PaqetConfig, DiagnosticReport
│   ├── Themes/                  # 10 theme ResourceDictionaries
│   └── Program.cs               # CLI entry point (--diag, --dns, --ping, --speed, --info)
├── setup.sh                     # Linux server installer (interactive menu)
├── setup.ps1                    # Windows client installer (interactive menu)
├── installer/                   # InnoSetup script (optional .exe installer builder)
├── paqet/                       # paqet submodule (Go, KCP engine)
└── assets/                      # Logo, screenshots
```

---

## 🔒 Security

| Layer | Protection |
|-------|-----------|
| **Transport** | KCP encryption with AES and pre-shared key |
| **Network** | Raw pcap (gopacket) — sends/receives directly on the NIC, bypasses OS routing |
| **Server** | iptables NOTRACK eliminates conntrack overhead; RST DROP makes port invisible to scans |
| **DNS** | Forced on all adapters to prevent ISP DNS leaks |
| **Process** | systemd hardened: `NoNewPrivileges`, `ProtectHome`, capability-restricted |
| **Ports** | 10800/10801 reserved in Windows to prevent svchost from grabbing them |

---

## 📝 Notes

- Windows Defender may flag paqet as a false positive — the installer automatically adds exclusions.
- TUN adapter uses IP `10.0.85.2` with gateway `10.0.85.1`.
- SOCKS5 port is `10800` (not 1080 — avoids Windows ICS conflicts).
- LAN sharing port is `10801` (portproxy is volatile — re-created each startup).

---

## 📚 Complete Technical Reference

<details>
<summary><b>Server Configuration</b></summary>

**Config file**: `/etc/paqet/server.yaml`

```yaml
role: "server"
log:
  level: "info"
listen:
  addr: ":8443"                       # Listen port
network:
  interface: "eth0"                   # Physical NIC name
  ipv4:
    addr: "10.0.0.5:8443"            # Local IP:port (for raw pcap)
    router_mac: "aa:bb:cc:dd:ee:ff"  # Gateway MAC address
  tcp:
    local_flag: ["PA"]               # TCP flags for packet crafting
transport:
  protocol: "kcp"
  kcp:
    mode: "fast"                     # KCP mode: fast, fast2, normal
    block: "aes"                     # Encryption cipher
    key: "base64_secret_key"         # Pre-shared key (must match client)
```

**File layout:**
```
/opt/paqet/paqet              ← binary
/usr/local/bin/paqet          ← symlink
/etc/paqet/server.yaml        ← config
/etc/systemd/system/paqet.service
```

**iptables rules** (auto-configured by setup.sh):
- `NOTRACK` on server port — disables conntrack for raw pcap
- `RST DROP` on server port — hides port from scanners (nmap sees "filtered")
- All rules use `-w 5` (waits for xtables lock to prevent race conditions)

</details>

<details>
<summary><b>Client Configuration</b></summary>

**Config file**: `%LOCALAPPDATA%\PaqetTunnel\config\client.yaml`

```yaml
role: "client"
log:
  level: "info"
socks5:
  - listen: "127.0.0.1:10800"           # SOCKS5 proxy address
server:
  addr: "VPS_IP:8443"                   # Server address
network:
  interface: "Ethernet"                  # Physical NIC name
  guid: "\Device\NPF_{ADAPTER-GUID}"    # WinPcap device GUID
  ipv4:
    addr: "192.168.1.100:0"             # Local IP (for raw pcap)
    router_mac: "aa:bb:cc:dd:ee:ff"     # Gateway MAC address
  tcp:
    local_flag: ["PA"]
    remote_flag: ["PA"]
transport:
  protocol: "kcp"
  kcp:
    mode: "fast"
    block: "aes"
    key: "same_key_as_server"            # Must match server
```

**File layout:**
```
%LOCALAPPDATA%\PaqetTunnel\
├── PaqetTunnel.exe          ← GUI app
├── bin\
│   ├── paqet_windows_amd64.exe
│   ├── tun2socks.exe
│   └── wintun.dll
├── config\client.yaml       ← paqet config
├── logs\                    ← log files
├── diagnostics\             ← saved reports
├── settings.json            ← app preferences
├── .version                 ← version tag
└── .commit                  ← git commit SHA
```

</details>

<details>
<summary><b>App Settings (settings.json)</b></summary>

Located at `%LOCALAPPDATA%\PaqetTunnel\settings.json`. All fields are optional — defaults apply when absent.

```json
{
  "AutoStart": false,
  "StartBeforeLogon": false,
  "StartMinimized": false,
  "AutoConnectOnLaunch": false,
  "FullSystemTunnel": false,
  "SystemProxyEnabled": false,
  "ProxySharingEnabled": false,
  "DebugMode": false,
  "Theme": "dark",
  "DnsProvider": "auto",
  "CustomDnsPrimary": "",
  "CustomDnsSecondary": "",
  "ServerSshHost": "",
  "ServerSshPort": 22,
  "ServerSshUser": "root",
  "ServerSshKeyPath": "",
  "ServerSshPassword": ""
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `AutoStart` | bool | `false` | Launch at Windows logon (Task Scheduler) |
| `StartBeforeLogon` | bool | `false` | Start as SYSTEM service at boot (before logon) |
| `StartMinimized` | bool | `false` | Hide window on launch — tray icon only |
| `AutoConnectOnLaunch` | bool | `false` | Connect automatically when app starts |
| `FullSystemTunnel` | bool | `false` | Use TUN mode (all traffic) vs SOCKS5 (browser only) |
| `SystemProxyEnabled` | bool | `false` | Set Windows system proxy via PAC file |
| `ProxySharingEnabled` | bool | `false` | Share tunnel on LAN via port 10801 |
| `DebugMode` | bool | `false` | Enable verbose debug logging |
| `Theme` | string | `"dark"` | UI theme (dark, light, nord, sakura, ocean, sunset, cyberpunk, dracula, monokai, rose) |
| `DnsProvider` | string | `"auto"` | DNS provider name or "auto" for benchmark-selected |
| `CustomDnsPrimary` | string | `""` | Custom primary DNS server IP |
| `CustomDnsSecondary` | string | `""` | Custom secondary DNS server IP |
| `ServerSshHost` | string | `""` | VPS IP/hostname for `--server` commands |
| `ServerSshPort` | int | `22` | SSH port |
| `ServerSshUser` | string | `"root"` | SSH username |
| `ServerSshKeyPath` | string | `""` | Path to SSH private key file |
| `ServerSshPassword` | string | `""` | SSH password (encrypted with DPAPI on disk) |

</details>

<details>
<summary><b>Ports & Networking</b></summary>

| Port | Protocol | Side | Purpose |
|------|----------|------|---------|
| **8443** | UDP | Server | KCP tunnel listener (configurable) |
| **10800** | TCP | Client | SOCKS5 proxy (localhost) |
| **10801** | TCP | Client | LAN sharing portproxy (optional) |
| **10802** | TCP | Client | PAC HTTP server (localhost, for Chrome) |

**TUN adapter** (full system tunnel mode):
- Name: `PaqetTun`
- IP: `10.0.85.2`, Gateway: `10.0.85.1`, Mask: `255.255.255.0`
- Routes: `0.0.0.0/1` + `128.0.0.0/1` → `10.0.85.1` (captures all traffic)
- LAN exclusions: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` → original gateway

</details>

<details>
<summary><b>Troubleshooting</b></summary>

| Problem | Solution |
|---------|----------|
| **Won't connect** | Check VPS firewall (UDP 8443). Check `journalctl -u paqet -f`. Verify key matches. |
| **Connected but no internet** | Set DNS in Settings (Cloudflare recommended). Run Auto benchmark. |
| **DNS leaks** | Enable DNS provider in Settings. Use Full System Tunnel for max protection. |
| **Slow speed** | Run `--speed` diagnostic. Check server bandwidth. Try KCP mode "fast2". |
| **Connection drops** | Auto-reconnects (5 attempts). Check server resources (`free -h`, `top`). |
| **Defender blocks paqet** | Exclusions added by installer. Manual: Settings → Exclusions → add install path. |
| **Port 10800 in use** | Run `netstat -ano \| findstr 10800`. Kill the process or reboot. |
| **TUN not working** | Check `wintun.dll` exists. Disable other VPN software. |
| **Server port in use** | Run `ss -tlnup \| grep 8443`. Change port in config or kill the process. |

</details>

---

## 🔧 Advanced: CLI Reference

Full reference for every PaqetTunnel CLI command. Run from PowerShell, CMD, or any terminal.

### `--diag` — Full Diagnostic Suite

Runs all four diagnostic tools in sequence: DNS benchmark → connectivity test → speed test → system info.

```powershell
PaqetTunnel.exe --diag
```

```
PaqetTunnel Diagnostics
=======================

[1/4] DNS Benchmark...
#   Provider                  Latency  Server
1   Cloudflare                  12ms   1.1.1.1         * FASTEST
2   Google                      18ms   8.8.8.8
3   Quad9                       25ms   9.9.9.9
...
17/17 providers reachable

[2/4] Connectivity...
SOCKS5 proxy (127.0.0.1:10800): listening [OK]
  HTTP via tunnel:  142ms - {"origin": "203.0.113.50"}
  HTTPS via tunnel: 165ms - {"ip": "203.0.113.50"}
ICMP ping to 203.0.113.50:
  [1] 32ms  [2] 28ms  [3] 31ms  [4] 29ms  [5] 30ms

[3/4] Speed Test...
Through tunnel (SOCKS5):
  Cloudflare 10MB: 45.2 Mbps (9765KB in 1720ms)
  Cloudflare 1MB:  38.7 Mbps (976KB in 201ms)
Direct (no tunnel):
  Cloudflare 10MB: 92.1 Mbps (9765KB in 868ms)
  Cloudflare 1MB:  78.4 Mbps (976KB in 100ms)

[4/4] System Info...
  Install:    C:\Users\You\AppData\Local\PaqetTunnel
  Binary:     [OK] ...\bin\paqet_windows_amd64.exe
  ...

[OK] Full diagnostic complete.
```

### `--dns` — DNS Benchmark

Benchmarks all 17 built-in DNS providers and ranks by response time. Uses your local IP from `client.yaml` for accurate results.

```powershell
PaqetTunnel.exe --dns
```

```
#   Provider                  Latency  Server
-----------------------------------------------------------
1   Cloudflare                  12ms   1.1.1.1         * FASTEST
2   Google                      18ms   8.8.8.8
3   Quad9                       25ms   9.9.9.9
4   DNS.SB                      31ms   185.222.222.222
...
16  Verisign                   210ms   64.6.64.6
17  Level3/Lumen             timeout   4.2.2.1

16/17 providers reachable
```

Color-coded: 🟢 <50ms, 🟡 <200ms, ⚪ <timeout, ⚫ timeout.

### `--ping` — Connectivity Test

Tests three layers: SOCKS5 port availability → HTTP/HTTPS through tunnel → ICMP ping to server.

```powershell
PaqetTunnel.exe --ping
```

```
SOCKS5 proxy (127.0.0.1:10800): listening [OK]

Tunnel connectivity to 203.0.113.50:
  HTTP via tunnel:  142ms - {"origin": "203.0.113.50"}
  HTTPS via tunnel: 165ms - {"ip": "203.0.113.50"}

ICMP ping to 203.0.113.50:
  [1] 32ms  [2] 28ms  [3] 31ms  [4] 29ms  [5] 30ms
```

Useful for verifying the tunnel is working and checking latency. The HTTP test shows your exit IP (should be your VPS).

### `--speed` — Speed Test

Downloads 1 MB + 10 MB files from Cloudflare's speed test CDN, first through the tunnel (SOCKS5 proxy), then direct (no tunnel). Shows throughput in Mbps.

```powershell
PaqetTunnel.exe --speed
```

```
Through tunnel (SOCKS5):
  Cloudflare 10MB: 45.2 Mbps (9765KB in 1720ms)
  Cloudflare 1MB:  38.7 Mbps (976KB in 201ms)

Direct (no tunnel):
  Cloudflare 10MB: 92.1 Mbps (9765KB in 868ms)
  Cloudflare 1MB:  78.4 Mbps (976KB in 100ms)
```

Color-coded: 🟢 >10 Mbps, 🟡 >2 Mbps, 🔴 ≤2 Mbps.

### `--info` — System Info

Displays installation paths, binary status, configuration values, and current settings. Detects legacy installations.

```powershell
PaqetTunnel.exe --info
```

```
  Install:    C:\Users\You\AppData\Local\PaqetTunnel
  Binary:     [OK] C:\...\bin\paqet_windows_amd64.exe
  Config:     [OK] C:\...\config\client.yaml
  Tun2socks:  [OK] C:\...\bin\tun2socks.exe
  WinTun:     [OK] C:\...\bin\wintun.dll

  Server:     203.0.113.50:8443
  Interface:  Ethernet
  Local IP:   192.168.1.100:0
  SOCKS5:     127.0.0.1:10800
  Key set:    yes

  Theme:      monokai
  DNS:        auto
  Debug:      False
  TUN mode:   True
  Auto-start: True

  [!] Legacy install found: C:\Program Files\Paqet Tunnel
    Run 'setup.ps1 update' to clean up.
```

`[OK]` means file exists. `[--]` means missing. The legacy warning only appears if an old Program Files install is detected.

### `--check` — Check for Updates

Compares local commit SHA (from `.commit` file) against the latest commit on GitHub `master` branch.

```powershell
PaqetTunnel.exe --check
```

```
Checking for updates...

  Update available!
  Local:   abc1234
  Remote:  def5678

  Run --update to install.
```

Or if already up to date:
```
  [OK] Already up to date (abc1234)
```

### `--update` — Install Update

Checks for updates, downloads the latest source, rebuilds, and replaces the running binary. The app restarts automatically.

```powershell
PaqetTunnel.exe --update
```

```
Checking for updates...
  Update: abc1234 -> def5678

Starting update...
  Downloading latest source...
  Building PaqetTunnel...
  Replacing binary...

  Update started — app will restart shortly.
```

### `--server <subcommand>` — Remote VPS Management

Manages the paqet server on your VPS over SSH. Requires SSH credentials configured in GUI Settings or `settings.json`.

| Subcommand | Description |
|-----------|-------------|
| `--server test` | Test SSH connection — verifies host, port, auth |
| `--server status` | Show systemd service status (`systemctl status paqet`) |
| `--server config` | Read and display current server config (YAML) |
| `--server sync` | Compare local config with server, patch differences interactively |
| `--server reset` | Reset server config to defaults (encryption key preserved) |
| `--server install` | Install paqet server on the VPS |
| `--server update` | Download latest paqet binary and restart service |
| `--server uninstall` | Stop service, remove all files, clean iptables |
| `--server restart` | Restart the paqet systemd service |
| `--server logs` | Tail recent journal logs (`journalctl -u paqet`) |

```powershell
# Test SSH connection first
PaqetTunnel.exe --server test
#   Host: root@203.0.113.50:22
#   Auth: key (C:\Users\You\.ssh\id_ed25519)
#   Testing SSH connection...
#   [OK] Connected successfully

# Check server status
PaqetTunnel.exe --server status

# Read server config
PaqetTunnel.exe --server config
#   role: "server"
#   transport:
#     kcp:
#       mode: "fast"
#       block: "aes"
#       ...

# Sync local changes to server (interactive — prompts before applying)
PaqetTunnel.exe --server sync
#   Changes to sync (2):
#     block: salsa20
#     mode: normal
#   Apply changes to server? [y/N] y
#   Restart server now? [y/N] y

# Reset server config to defaults (key preserved)
PaqetTunnel.exe --server reset

# View live server logs
PaqetTunnel.exe --server logs
```

**SSH configuration** — set in `settings.json`:
```json
{
  "ServerSshHost": "203.0.113.50",
  "ServerSshUser": "root",
  "ServerSshPort": 22,
  "ServerSshKeyPath": "C:\\Users\\You\\.ssh\\id_ed25519"
}
```

Or use password auth (stored encrypted with Windows DPAPI):
```json
{
  "ServerSshHost": "203.0.113.50",
  "ServerSshUser": "root",
  "ServerSshPassword": "your-password"
}
```

### `--connect` — GUI Auto-Connect Flag

Launches the GUI and immediately connects. Used internally by the Task Scheduler auto-start task — generally not called manually.

```powershell
PaqetTunnel.exe --connect
```

### `--help` — Help

Prints all available commands and examples.

```powershell
PaqetTunnel.exe --help
```

---

## 🔧 Advanced: Setup Script Reference

### Linux Server — `setup.sh`

Full interactive installer for the paqet server. Downloads the script once, then use it for all management tasks.

```bash
# Download the script
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh -o /tmp/pt.sh

# Interactive menu
sudo bash /tmp/pt.sh

# Direct commands
sudo bash /tmp/pt.sh install          # Install server
sudo bash /tmp/pt.sh update           # Update paqet binary, restart service
sudo bash /tmp/pt.sh uninstall        # Remove everything (binary, config, service, iptables)
sudo bash /tmp/pt.sh status           # Show install status + service status + config info
sudo bash /tmp/pt.sh restart          # Restart systemd service
sudo bash /tmp/pt.sh logs             # Tail live logs (journalctl -u paqet -f)
sudo bash /tmp/pt.sh help             # Show all commands and flags
```

**Flags:**

| Flag | Description | Default |
|------|-------------|---------|
| `--addr ip:port` | Bind address for the server | `0.0.0.0:8443` |
| `--key "secret"` | Pre-shared encryption key | Auto-generated (base64, 32 bytes) |
| `--iface name` | Network interface | Auto-detected (default route) |
| `--build` | Build from source instead of downloading pre-built binary | Download release |
| `--yes` / `-y` | Skip all confirmation prompts | Interactive |

**Examples:**

```bash
# Silent install with all defaults (auto-detect everything)
sudo bash /tmp/pt.sh install --yes

# Install with custom port and key
sudo bash /tmp/pt.sh install --addr 0.0.0.0:9443 --key "MySecretKey123" --yes

# Install on a specific interface
sudo bash /tmp/pt.sh install --iface ens3 --yes

# Build from source instead of downloading pre-built binary (requires Go 1.23+)
sudo bash /tmp/pt.sh install --build --yes

# Update server binary to latest release
sudo bash /tmp/pt.sh update --yes

# Check status
sudo bash /tmp/pt.sh status
# Output:
#   ✔ Paqet installed: /opt/paqet/paqet
#   ✔ Config: /etc/paqet/server.yaml
#   ✔ Service: active (running)
#   Server: 0.0.0.0:8443
#   Interface: eth0
#   Key: ****...****
```

**What `install` does step by step:**
1. Installs dependencies (`curl`, `jq`, `tar`)
2. Downloads latest paqet binary from GitHub releases (or builds from source with `--build`)
3. Auto-detects: network interface, server IP, gateway MAC address
4. Generates encryption key (if not provided)
5. Creates `/etc/paqet/server.yaml` config
6. Configures iptables: NOTRACK (disable conntrack) + RST DROP (hide port) with persistence
7. Creates hardened systemd service (`NoNewPrivileges`, `ProtectHome`, capability-restricted)
8. Starts the service and prints the Windows client command (ready to copy-paste)

**What `uninstall` removes:**
- Systemd service + timer
- `/opt/paqet/` binary directory
- `/etc/paqet/` config directory
- iptables NOTRACK and RST DROP rules
- `/usr/local/bin/paqet` symlink

### Windows Client — `setup.ps1`

Full interactive installer for the client app. Run in PowerShell **as Administrator**.

```powershell
# Download the script
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1

# Interactive menu
& $env:TEMP\pt.ps1

# Direct commands
& $env:TEMP\pt.ps1 install             # Install client
& $env:TEMP\pt.ps1 update              # Pull latest, rebuild, update binary
& $env:TEMP\pt.ps1 uninstall           # Remove everything
& $env:TEMP\pt.ps1 status              # Show install status + running processes
& $env:TEMP\pt.ps1 help                # Show all commands and flags
```

**Flags:**

| Flag | Alias | Description | Default |
|------|-------|-------------|---------|
| `-Addr ip:port` | `-a` | Server address (IP:port) | *(prompted)* |
| `-Key "secret"` | | Pre-shared encryption key | *(prompted)* |
| `-Iface name` | `-i` | Network interface name | Auto-detected |
| `-SocksPort 10800` | | SOCKS5 proxy listen port | `10800` |
| `-Build` | | Build paqet from source (requires Go + MinGW + Npcap) | Download release |
| `-Force` | | Force reinstall even if already installed | Skip if installed |
| `-Server` | `-s` | Install in server mode instead of client | Client mode |
| `-y` | | Skip all confirmation prompts | Interactive |
| `-Silent` | | Suppress output | Normal output |
| `-Launch` | | Auto-launch app after install | Don't launch |

**Examples:**

```powershell
# Full silent install with server address and key
& $env:TEMP\pt.ps1 install -Addr 203.0.113.50:8443 -Key "MySecretKey123" -y

# Install with custom SOCKS5 port
& $env:TEMP\pt.ps1 install -Addr 203.0.113.50:8443 -Key "key" -SocksPort 11080 -y

# Force reinstall on a specific interface
& $env:TEMP\pt.ps1 install -Addr 203.0.113.50:8443 -Key "key" -Iface "Wi-Fi" -Force -y

# Build paqet from source (instead of downloading pre-built binary)
& $env:TEMP\pt.ps1 install -Addr 203.0.113.50:8443 -Key "key" -Build -y

# Update to latest version
& $env:TEMP\pt.ps1 update

# Check installation status
& $env:TEMP\pt.ps1 status
# Output:
#   ✔ Installed: C:\Users\You\AppData\Local\PaqetTunnel
#   ✔ Binary: paqet_windows_amd64.exe
#   ✔ GUI: PaqetTunnel.exe (running, PID 1234)
#   Server: 203.0.113.50:8443
#   SOCKS5: 127.0.0.1:10800
```

**What `install` does step by step:**
1. Checks for Git and .NET 8 SDK — downloads and installs if missing
2. Clones the PaqetTunnel repo to `%LOCALAPPDATA%\PaqetTunnel\source`
3. Downloads latest paqet binary from GitHub releases (or builds from source with `-Build`)
4. Builds the .NET WPF GUI (`dotnet publish`)
5. Downloads tun2socks + wintun.dll
6. Auto-detects: network interface, local IP, gateway MAC, adapter GUID
7. Creates `client.yaml` config
8. Adds Windows Defender exclusions
9. Creates Desktop + Start Menu shortcuts
10. Registers Task Scheduler auto-start task
11. Cleans up legacy `C:\Program Files\Paqet Tunnel\` installation if found

**What `uninstall` removes:**
- `%LOCALAPPDATA%\PaqetTunnel\` directory (binary, config, logs)
- Desktop and Start Menu shortcuts
- Task Scheduler auto-start task
- Windows Defender exclusions
- Optionally removes source code

## 🙏 Credits

- [paqet](https://github.com/hanselime/paqet) by hanselime — KCP tunnel engine
- [tun2socks](https://github.com/xjasonlyu/tun2socks) by xjasonlyu — TUN-to-SOCKS5 adapter
- [WinTun](https://www.wintun.net/) by WireGuard — Windows TUN driver

## 📄 License

[MIT](LICENSE)
