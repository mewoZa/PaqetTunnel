# Paqet Tunnel — AI Agent Setup Guide

> **This file is for AI agents (ChatGPT, Claude, Copilot, etc.) to fully install and manage Paqet Tunnel for users.**
> Copy the relevant section below and paste it to your AI assistant for automated setup.

---

## Quick Reference — Copy & Paste to Your AI Agent

### 🖥️ Server Setup (Linux VPS)

Copy this to your AI agent:

```
Install Paqet Tunnel VPN server on this Linux VPS.

Run this single command:
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh -o /tmp/pt.sh && sudo bash /tmp/pt.sh install --yes

The script auto-detects network interface, local IP, and gateway MAC.
It generates a random encryption key, configures iptables, and starts a systemd service.
After install, it prints the exact Windows client command with the server IP and key.
Save that output — the user needs it for client setup.

Port: 8443/UDP (KCP protocol). Ensure firewall allows UDP 8443.
Config: /etc/paqet/server.yaml
Service: systemctl {status|restart|stop} paqet
Logs: journalctl -u paqet -f

Management commands:
  sudo bash /tmp/pt.sh status     # Check server status
  sudo bash /tmp/pt.sh update     # Update paqet binary
  sudo bash /tmp/pt.sh restart    # Restart service
  sudo bash /tmp/pt.sh uninstall  # Remove everything
  sudo bash /tmp/pt.sh logs       # Live logs
```

### 💻 Client Setup (Windows)

Copy this to your AI agent:

```
Install Paqet Tunnel VPN client on this Windows machine.

Run this in PowerShell (as Administrator):
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1 install -Addr SERVER_IP:8443 -Key "SECRET_KEY" -y

Replace SERVER_IP with the VPS IP address and SECRET_KEY with the key from server setup.

The script downloads paqet binary, tun2socks, WinTun driver, builds the GUI app,
auto-detects network interface/IP/gateway, creates config, adds Defender exclusions,
creates desktop shortcut, and sets up auto-start via Task Scheduler.

Install path: %LOCALAPPDATA%\PaqetTunnel\
Config: %LOCALAPPDATA%\PaqetTunnel\config\client.yaml
SOCKS5 proxy: 127.0.0.1:10800

Management commands (PowerShell as Admin):
  & $env:TEMP\pt.ps1 status     # Check install status
  & $env:TEMP\pt.ps1 update     # Update to latest
  & $env:TEMP\pt.ps1 uninstall  # Remove everything
```

### 🔧 Server + Client Together (Full Setup)

Copy this to your AI agent:

```
Set up Paqet Tunnel VPN — both server and client.

STEP 1 — SERVER (on the Linux VPS):
SSH into the VPS and run:
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh -o /tmp/pt.sh && sudo bash /tmp/pt.sh install --yes

After it finishes, copy the Windows install command it prints (contains IP + key).
Also verify: sudo systemctl status paqet (should be active/running).
Ensure firewall allows UDP port 8443: sudo ufw allow 8443/udp (or equivalent).

STEP 2 — CLIENT (on the Windows machine):
Run the command from Step 1 output in PowerShell as Admin. It looks like:
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1 install -Addr <VPS_IP>:8443 -Key "<KEY>" -y

STEP 3 — VERIFY:
Launch PaqetTunnel.exe from the desktop shortcut. Click Connect.
The status should change to Connected with a green indicator.
Test: open a browser and visit https://whatismyipaddress.com — it should show the VPS IP.

TROUBLESHOOTING:
- If it won't connect: check VPS firewall (UDP 8443), check paqet logs (journalctl -u paqet -f)
- If DNS leaks: go to Settings in the app, enable a DNS provider (Cloudflare recommended)
- If slow: try changing KCP mode in config from "fast" to "fast2"
- If connection drops: the app auto-reconnects (up to 5 attempts with exponential backoff)
```

---

## Detailed Technical Guide

### What is Paqet Tunnel?

Paqet Tunnel is an encrypted VPN built on the **KCP protocol** (UDP-based, faster than TCP in lossy networks). It uses **raw pcap** (gopacket) to send/receive packets directly on the NIC, bypassing the OS routing table — which means no routing loops and stealth-level invisibility to port scanners.

The Windows client is a **WPF system tray app** with a modern dark UI, 10 themes, built-in diagnostics, smart DNS, and two tunnel modes: SOCKS5 proxy (browser-only) and full system tunnel (all traffic via TUN adapter).

### Architecture Overview

```
Windows Client                         Linux VPS Server
┌──────────────────────┐              ┌──────────────────────┐
│ PaqetTunnel.exe      │              │ paqet (server)       │
│  ├─ WPF GUI          │              │  ├─ KCP on :8443     │
│  ├─ paqet (client)   │──── KCP ────│  ├─ raw pcap         │
│  │  └─ SOCKS5 :10800 │   UDP/KCP   │  ├─ iptables NOTRACK │
│  ├─ tun2socks        │  encrypted   │  └─ systemd service  │
│  │  └─ TUN 10.0.85.2 │              │                      │
│  ├─ WinTun driver    │              │ Config:              │
│  ├─ DNS management   │              │  /etc/paqet/         │
│  └─ System proxy/PAC │              │  server.yaml         │
│                      │              │                      │
│ Path:                │              │ Binary:              │
│  %LOCALAPPDATA%\     │              │  /opt/paqet/paqet    │
│  PaqetTunnel\        │              │                      │
└──────────────────────┘              └──────────────────────┘
```

### Ports & Networking

| Port | Protocol | Side | Purpose |
|------|----------|------|---------|
| **8443** | UDP | Server | KCP tunnel listener (configurable) |
| **10800** | TCP | Client | SOCKS5 proxy (local only) |
| **10801** | TCP | Client | LAN sharing portproxy (optional, forwards → 10800) |
| **10802** | TCP | Client | PAC HTTP server (local only, for Chrome proxy config) |

### Server Installation — Full Details

**Requirements:**
- Linux VPS (Ubuntu 20.04+, Debian 11+, CentOS 8+, or any systemd-based distro)
- Root access
- Open UDP port (default: 8443)

**What the install script does:**

1. **Installs dependencies**: `curl`, `tar`, `iptables`, `iptables-persistent`
2. **Downloads paqet binary** from `hanselime/paqet` GitHub releases (or builds from source with `--build`)
3. **Auto-detects network**: interface name, local IP, gateway MAC address
4. **Generates encryption key** (32-byte random, base64-encoded) if not provided
5. **Creates config** at `/etc/paqet/server.yaml`
6. **Configures iptables**:
   - `NOTRACK` rules on server port (disables conntrack for raw pcap)
   - `RST DROP` on server port (prevents kernel from sending TCP RST, hides port from scanners)
   - All rules use `-w 5` flag (waits for xtables lock)
   - Rules persisted via `netfilter-persistent`
7. **Creates systemd service** with security hardening:
   - `NoNewPrivileges=yes`, `ProtectHome=yes`, `ProtectSystem=strict`
   - `CAP_NET_RAW` + `CAP_NET_ADMIN` capabilities
   - Auto-restart on failure (5s delay)
8. **Starts service** and prints the Windows client install command

**Server config (`/etc/paqet/server.yaml`):**

```yaml
role: "server"
log:
  level: "info"
listen:
  addr: ":8443"
network:
  interface: "eth0"
  ipv4:
    addr: "10.0.0.5:8443"
    router_mac: "aa:bb:cc:dd:ee:ff"
  tcp:
    local_flag: ["PA"]
transport:
  protocol: "kcp"
  kcp:
    mode: "fast"
    block: "aes"
    key: "your_base64_key_here"
```

**Server file layout:**

```
/opt/paqet/paqet                      ← binary
/usr/local/bin/paqet                  ← symlink
/etc/paqet/server.yaml                ← config
/etc/systemd/system/paqet.service     ← service unit
```

**Server management:**

```bash
# Status check
sudo systemctl status paqet
sudo bash /tmp/pt.sh status

# View live logs
journalctl -u paqet -f
sudo bash /tmp/pt.sh logs

# Restart
sudo systemctl restart paqet

# Update binary only
sudo bash /tmp/pt.sh update

# Full uninstall (removes binary, config, service, iptables rules)
sudo bash /tmp/pt.sh uninstall

# Custom install (specify port and key)
sudo bash /tmp/pt.sh install --addr 0.0.0.0:9443 --key "mySecretKey123" --yes

# Build from source (requires Go 1.23+, auto-installed by script)
sudo bash /tmp/pt.sh install --build --yes
```

**Firewall rules needed:**

```bash
# UFW
sudo ufw allow 8443/udp

# iptables (manual)
sudo iptables -A INPUT -p udp --dport 8443 -j ACCEPT

# firewalld
sudo firewall-cmd --permanent --add-port=8443/udp && sudo firewall-cmd --reload
```

### Client Installation — Full Details

**Requirements:**
- Windows 10/11 (x64)
- PowerShell 5.1+ (built-in on Windows 10+)
- Administrator privileges (for WinTun driver, routes, proxy settings)
- Git (auto-installed if not present — ~50 MB)
- .NET 8 SDK (auto-installed if not present — ~200 MB)

**What the install script does:**

1. **Clones PaqetTunnel repo** to `%USERPROFILE%\PaqetTunnel`
2. **Builds WPF GUI** with `dotnet publish` (single-file, self-contained)
3. **Downloads binaries**:
   - `paqet_windows_amd64.exe` from `hanselime/paqet` releases
   - `tun2socks.exe` from `xjasonlyu/tun2socks` releases
   - `wintun.dll` from wintun.net
4. **Auto-detects network**: interface name, local IP, gateway MAC, adapter GUID
5. **Creates client config** at `%LOCALAPPDATA%\PaqetTunnel\config\client.yaml`
6. **Adds Windows Defender exclusions** for install directory
7. **Reserves ports** 10800/10801 to prevent system services from claiming them
8. **Creates shortcuts**: Desktop + Start Menu
9. **Sets up auto-start** via Task Scheduler (runs elevated at logon)
10. **Cleans up legacy installs** from `C:\Program Files\Paqet Tunnel\` if found

**Client config (`%LOCALAPPDATA%\PaqetTunnel\config\client.yaml`):**

```yaml
role: "client"
log:
  level: "info"
socks5:
  - listen: "127.0.0.1:10800"
server:
  addr: "YOUR_VPS_IP:8443"
network:
  interface: "Ethernet"
  guid: "\Device\NPF_{12345678-ABCD-EFGH-IJKL-123456789ABC}"
  ipv4:
    addr: "192.168.1.100:0"
    router_mac: "aa:bb:cc:dd:ee:ff"
  tcp:
    local_flag: ["PA"]
    remote_flag: ["PA"]
transport:
  protocol: "kcp"
  kcp:
    mode: "fast"
    block: "aes"
    key: "same_key_as_server"
```

**Client file layout:**

```
%LOCALAPPDATA%\PaqetTunnel\
├── PaqetTunnel.exe                   ← GUI app (single-file, ~30MB)
├── bin\
│   ├── paqet_windows_amd64.exe       ← tunnel engine
│   ├── tun2socks.exe                 ← TUN-to-SOCKS5 adapter
│   └── wintun.dll                    ← Windows TUN driver
├── config\
│   └── client.yaml                   ← paqet configuration
├── logs\                             ← app log files
├── diagnostics\                      ← saved diagnostic reports
├── settings.json                     ← app preferences (theme, DNS, auto-start)
├── .version                          ← installed version tag
└── .commit                           ← installed git commit SHA
```

**Client management:**

```powershell
# Download script (only needed once)
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1

# Install with server address and key
& $env:TEMP\pt.ps1 install -Addr 1.2.3.4:8443 -Key "mykey" -y

# Update to latest version
& $env:TEMP\pt.ps1 update

# Check installation status
& $env:TEMP\pt.ps1 status

# Full uninstall
& $env:TEMP\pt.ps1 uninstall

# Force reinstall (clean)
& $env:TEMP\pt.ps1 install -Force -y
```

### App Features & Settings

| Setting | What it does |
|---------|-------------|
| **Connect/Disconnect** | Starts/stops paqet SOCKS5 proxy |
| **Full System Tunnel** | Enables WinTun TUN adapter → routes ALL traffic through tunnel |
| **System Proxy** | Sets Windows proxy to PAC file pointing at SOCKS5 (browser-only mode) |
| **LAN Sharing** | Opens port 10801 so other devices on your network can use the tunnel |
| **Auto-Connect** | Connects automatically when app starts |
| **Start with Windows** | Launches at logon via Task Scheduler |
| **Start Before Logon** | Runs paqet as SYSTEM service (tunnel active even at login screen) |
| **DNS Provider** | Choose from 17 providers or use Auto benchmark to find fastest |
| **Theme** | 10 themes: Dark, Light, Nord, Sakura, Ocean, Sunset, Cyberpunk, Dracula, Monokai, Rose |
| **Debug Mode** | Verbose logging for troubleshooting |

### CLI Diagnostics

Run from command line or PowerShell:

```powershell
# Full diagnostic suite (DNS + ping + speed + system info)
& "$env:LOCALAPPDATA\PaqetTunnel\PaqetTunnel.exe" --diag

# Benchmark all 17 DNS providers
& "$env:LOCALAPPDATA\PaqetTunnel\PaqetTunnel.exe" --dns

# Test connectivity through tunnel
& "$env:LOCALAPPDATA\PaqetTunnel\PaqetTunnel.exe" --ping

# Speed test (1MB + 10MB downloads, tunnel vs direct)
& "$env:LOCALAPPDATA\PaqetTunnel\PaqetTunnel.exe" --speed

# Show installation info, paths, config values
# Note: PaqetTunnel is a GUI app — use these setup.ps1 commands instead:
& "$env:TEMP\pt.ps1" status            # Check installation status
& "$env:TEMP\pt.ps1" update            # Update to latest version

# Remote server management (via SSH from the app's Server Management tab,
# or directly via SSH):
ssh root@SERVER_IP "systemctl status paqet"
ssh root@SERVER_IP "journalctl -u paqet --no-pager -n 50"
ssh root@SERVER_IP "systemctl restart paqet"
```

### Troubleshooting Guide

| Problem | Solution |
|---------|----------|
| **Won't connect** | Check VPS firewall allows UDP 8443. Check `journalctl -u paqet -f` on server. Verify key matches in both configs. |
| **Connected but no internet** | DNS may be wrong. Open Settings → DNS → select Cloudflare or run Auto benchmark. |
| **DNS leaks** | Enable DNS in Settings (applies to all adapters). Use Full System Tunnel mode for maximum protection. |
| **Slow speed** | Try `--speed` diagnostic. Check server location/bandwidth. Try KCP mode "fast2" in config. |
| **Connection drops** | App auto-reconnects (5 attempts). Check network stability. Check server memory (`free -h`). |
| **Windows Defender blocks paqet** | Script adds exclusions automatically. If still blocked: Settings → Virus & threat → Exclusions → add `%LOCALAPPDATA%\PaqetTunnel\` |
| **Port 10800 in use** | Another app is using it. The app tries to kill the port thief. Run `netstat -ano | findstr 10800` to check. |
| **TUN mode not working** | Ensure WinTun driver is installed (`bin\wintun.dll` exists). Try disabling other VPN software. |
| **Can't start as admin** | The app uses Task Scheduler for elevated start. Check: `schtasks /query /tn PaqetTunnel` |
| **Server shows "address already in use"** | Another process on port 8443. Check: `ss -tlnup | grep 8443`. Kill it or change port. |

### Manual Config Editing

If the auto-detect gets something wrong, edit configs manually:

**Server** — `sudo nano /etc/paqet/server.yaml` then `sudo systemctl restart paqet`

**Client** — edit `%LOCALAPPDATA%\PaqetTunnel\config\client.yaml` then restart the app

Key fields to verify:
- `network.interface` — must match the actual NIC name (`ip link show` on Linux, `Get-NetAdapter` on Windows)
- `network.ipv4.router_mac` — must be the gateway's MAC address (`arp -a` or `ip neigh show`)
- `transport.kcp.key` — must be identical on server and client
- `server.addr` — client must point to server's public IP and port

### Building from Source

**Server (Linux):**
```bash
# The install script can build from source:
sudo bash /tmp/pt.sh install --build --yes

# Manual build:
sudo apt install -y build-essential libpcap-dev
git clone https://github.com/hanselime/paqet.git
cd paqet
CGO_ENABLED=1 go build -o paqet ./cmd/main.go
sudo cp paqet /opt/paqet/paqet
sudo systemctl restart paqet
```

**Client (Windows):**
```powershell
# The install script can build from source:
& $env:TEMP\pt.ps1 install -Build -Addr 1.2.3.4:8443 -Key "mykey" -y

# Manual GUI build:
dotnet publish src/PaqetTunnel/PaqetTunnel.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

### Security Notes

- **Encryption**: AES with pre-shared key over KCP (UDP). Key is in `transport.kcp.key`.
- **Raw pcap**: Paqet uses gopacket to craft packets at Layer 2 — bypasses OS routing entirely.
- **iptables NOTRACK**: Disables connection tracking on the server port — zero conntrack overhead.
- **RST DROP**: Kernel TCP RST responses are dropped on the server port — port is invisible to nmap scans.
- **Credentials**: SSH passwords stored in `settings.json` are AES-encrypted with a machine-derived key.
- **Auto-start**: Uses Windows Task Scheduler (not Registry Run) — more reliable and supports elevated execution.
- **Process elevation**: Only system commands (netsh, reg, route, schtasks) can be elevated — other binaries are blocked.
