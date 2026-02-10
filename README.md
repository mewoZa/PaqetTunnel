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

</div>

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
| 🩺 **CLI Diagnostics** | Built-in `--diag`, `--dns`, `--ping`, `--speed`, `--info` tools |

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

## 🩺 CLI Diagnostics

The app includes built-in diagnostic tools accessible from the command line:

```
PaqetTunnel.exe --diag    # Full suite: DNS + connectivity + speed + system info
PaqetTunnel.exe --dns     # Benchmark all DNS providers, rank by latency
PaqetTunnel.exe --ping    # Test SOCKS5 port, HTTP/HTTPS through tunnel, ICMP to server
PaqetTunnel.exe --speed   # Download speed test (1MB + 10MB) through tunnel vs direct
PaqetTunnel.exe --info    # Show paths, config, binary status, theme, debug flags
```

---

## 📋 Setup Script Commands

Both scripts provide an **interactive menu** when run without arguments, or accept commands directly:

| Command | Windows | Linux |
|---------|---------|-------|
| **Menu** | `& $env:TEMP\pt.ps1` | `sudo bash /tmp/pt.sh` |
| **Install** | `& $env:TEMP\pt.ps1 install` | `sudo bash /tmp/pt.sh install` |
| **Update** | `& $env:TEMP\pt.ps1 update` | `sudo bash /tmp/pt.sh update` |
| **Uninstall** | `& $env:TEMP\pt.ps1 uninstall` | `sudo bash /tmp/pt.sh uninstall` |
| **Status** | `& $env:TEMP\pt.ps1 status` | `sudo bash /tmp/pt.sh status` |

### Flags

| Windows (`setup.ps1`) | Linux (`setup.sh`) | Description |
|------------------------|---------------------|-------------|
| `-Addr ip:port` | `--addr ip:port` | Server address |
| `-Key "secret"` | `--key "secret"` | Pre-shared encryption key |
| `-Iface name` | `--iface name` | Network interface override |
| `-SocksPort 10800` | — | SOCKS5 listen port |
| `-Build` | `--build` | Build from source (requires Go + CGO) |
| `-Force` | — | Force reinstall |
| `-y` | `--yes` | Skip all confirmations |

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

## 🙏 Credits

- [paqet](https://github.com/hanselime/paqet) by hanselime — KCP tunnel engine
- [tun2socks](https://github.com/xjasonlyu/tun2socks) by xjasonlyu — TUN-to-SOCKS5 adapter
- [WinTun](https://www.wintun.net/) by WireGuard — Windows TUN driver

## 📄 License

[MIT](LICENSE)
