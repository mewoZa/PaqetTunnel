# Paqet Tunnel

<p align="center">
  <img src="assets/logo-dark.png" width="128" alt="Paqet Tunnel" />
</p>

<p align="center">
  <strong>Encrypted KCP tunnel with a modern Windows GUI</strong><br>
  <em>One-click setup · Full system tunnel · Live speed monitor</em>
</p>

<p align="center">
  <img src="assets/screenshot.png" width="320" alt="Paqet Tunnel — Connected" />
</p>

---

## Install

### Windows Client

Open **PowerShell as Admin** and run:

```powershell
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1
```

Or fully automatic with server details:

```powershell
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1 install -Addr YOUR_SERVER:8443 -Key "YOUR_KEY" -y
```

### Linux Server

```bash
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh | sudo bash -s install
```

With a custom key:

```bash
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh | sudo bash -s install --key "YOUR_KEY" --yes
```

After server install, it prints the exact Windows one-liner with your server's IP and key — just copy and paste.

## Features

- **One-click connect** with live speed graph
- **Full system tunnel** — routes all traffic through TUN adapter
- **SOCKS5 proxy** — lightweight browser-only mode (`127.0.0.1:10800`)
- **Auto-connect** and **start with Windows** options
- **LAN exclusion** — local network traffic bypasses the tunnel
- **Share on network** — forward proxy to other devices
- **System tray** — minimal footprint, always accessible

## Commands

Both scripts support:

| Command | Description |
|---------|-------------|
| `install` | Install and configure everything |
| `update` | Update to latest version |
| `uninstall` | Remove completely |
| `status` | Show current status |
| `help` | Show all options |

| Flag | Description |
|------|-------------|
| `-Addr` / `--addr` | Server address (`ip:port`) |
| `-Key` / `--key` | Pre-shared encryption key |
| `-Build` / `--build` | Build from source instead of downloading |
| `-y` / `--yes` | Skip confirmations |

## How It Works

```
Client                              Server
┌─────────────┐    KCP encrypted    ┌─────────────┐
│ PaqetTunnel │ ◄═══════════════► │   paqet     │ ◄──► Internet
│ SOCKS5:10800│    UDP raw packet   │   :8443     │
└─────────────┘                     └─────────────┘
```

1. **Server** runs paqet with KCP encrypted transport on your VPS
2. **Client** connects and provides SOCKS5 proxy on `127.0.0.1:10800`
3. **Full system tunnel** (optional) routes all traffic through a TUN virtual adapter

> **Note:** Windows Defender may flag the paqet binary as a false positive — this is common for tunneling tools. The installer automatically adds exclusions.

## Credits

- [paqet](https://github.com/hanselime/paqet) by hanselime — KCP tunnel engine
- [tun2socks](https://github.com/xjasonlyu/tun2socks) — TUN-to-SOCKS5
- [WinTun](https://www.wintun.net/) — Windows TUN driver

## License

MIT
