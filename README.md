# Paqet Tunnel

<p align="center">
  <img src="assets/logo-dark.png" width="128" alt="Paqet Tunnel Logo" />
</p>

<p align="center">
  <strong>Encrypted KCP tunnel with a modern Windows GUI</strong><br>
  <em>One-click setup for both server and client</em>
</p>

---

## Install

### Windows Client

Open **PowerShell as Admin** and run:

```powershell
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1
```

Or with server details (fully automatic):

```powershell
irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1 install -Addr YOUR_SERVER:8443 -Key "YOUR_KEY" -y
```

### Linux Server

```bash
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh | sudo bash -s install
```

Or with a custom key:

```bash
curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh | sudo bash -s install --key "YOUR_KEY" --yes
```

After server install, it prints the exact Windows one-liner with your server's IP and key.

## Commands

Both scripts support the same commands:

| Command | Description |
|---------|-------------|
| `install` | Install and configure everything |
| `update` | Update to latest version |
| `uninstall` | Remove completely |
| `status` | Show current status |
| `help` | Show all options |

### Options

| Flag | Description |
|------|-------------|
| `-Addr` / `--addr` | Server address (`ip:port`) |
| `-Key` / `--key` | Pre-shared encryption key |
| `-Build` / `--build` | Build from source instead of downloading release |
| `-y` / `--yes` | Skip all confirmations |

## How It Works

1. **Server** runs [paqet](https://github.com/hanselime/paqet) with KCP encrypted transport
2. **Client** connects via paqet → SOCKS5 proxy on `127.0.0.1:10800`
3. **Full system tunnel** (optional) routes all traffic through a TUN adapter

The setup scripts handle everything: dependencies, compilation or download, configuration, and service setup.

## Credits

- [paqet](https://github.com/hanselime/paqet) by hanselime — KCP tunnel engine
- [tun2socks](https://github.com/xjasonlyu/tun2socks) — TUN-to-SOCKS5
- [WinTun](https://www.wintun.net/) — Windows TUN driver

## License

MIT
