#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════
# Paqet Tunnel — Linux Server Setup
# Usage: curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh | sudo bash
#    Or: ./setup.sh [install|update|uninstall|status] [options]
#
# Options:
#   --addr <ip:port>   Bind address (default: 0.0.0.0:8443)
#   --key <secret>     Pre-shared key (auto-generated if omitted)
#   --iface <name>     Network interface (auto-detected if omitted)
#   --build            Build from source instead of downloading release
#   --yes              Skip confirmations
# ═══════════════════════════════════════════════════════════════════
set -euo pipefail

VERSION="1.0.0"
REPO="mewoZa/PaqetTunnel"
REPO_URL="https://github.com/$REPO.git"
UPSTREAM_REPO="hanselime/paqet"
INSTALL_DIR="/opt/paqet"
CONFIG_DIR="/etc/paqet"
SERVICE_NAME="paqet"
BINARY="paqet"

# Colors
C='\033[0;36m' G='\033[0;32m' R='\033[0;31m' Y='\033[0;33m' D='\033[0;90m' W='\033[0m' B='\033[1m'

step()  { echo -e "  ${C}›${W} $1"; }
ok()    { echo -e "  ${G}✓${W} $1"; }
warn()  { echo -e "  ${Y}!${W} $1"; }
err()   { echo -e "  ${R}✗${W} $1"; }
dim()   { echo -e "    ${D}$1${W}"; }
line()  { echo -e "  ${D}────────────────────────────────${W}"; }

banner() {
    echo ""
    echo -e "  ${C}»${W} ${B}Paqet Tunnel${W} ${D}Server${W}"
    echo -e "  ${D}  Setup v$VERSION${W}"
    line
}

confirm() {
    [[ "$YES" == "1" ]] && return 0
    echo -en "  ${Y}?${W} $1 ${D}[Y/n]${W} "
    read -r r
    [[ -z "$r" || "$r" =~ ^[Yy] ]]
}

# ── Detection ──────────────────────────────────────────────────

detect_os() {
    if [[ -f /etc/os-release ]]; then
        . /etc/os-release
        echo "$ID"
    elif command -v lsb_release &>/dev/null; then
        lsb_release -si | tr '[:upper:]' '[:lower:]'
    else
        echo "unknown"
    fi
}

detect_arch() {
    case "$(uname -m)" in
        x86_64|amd64) echo "amd64" ;;
        aarch64|arm64) echo "arm64" ;;
        armv7*|armhf) echo "arm" ;;
        *) echo "amd64" ;;
    esac
}

has_cmd() { command -v "$1" &>/dev/null; }

# ── Dependencies ───────────────────────────────────────────────

ensure_packages() {
    local os
    os=$(detect_os)
    step "Installing system packages..."

    case "$os" in
        ubuntu|debian|pop)
            export DEBIAN_FRONTEND=noninteractive
            apt-get update -qq
            apt-get install -y -qq git curl wget build-essential libpcap-dev >/dev/null 2>&1
            ;;
        centos|rhel|rocky|alma|fedora)
            if has_cmd dnf; then
                dnf install -y -q git curl wget gcc make libpcap-devel >/dev/null 2>&1
            else
                yum install -y -q git curl wget gcc make libpcap-devel >/dev/null 2>&1
            fi
            ;;
        arch|manjaro)
            pacman -Sy --noconfirm git curl wget base-devel libpcap >/dev/null 2>&1
            ;;
        *)
            warn "Unknown distro ($os). Ensure git, curl, wget, gcc, libpcap-dev are installed."
            ;;
    esac
    ok "System packages ready"
}

ensure_go() {
    if has_cmd go; then
        local ver
        ver=$(go version 2>/dev/null | sed 's/.*go\([0-9.]*\).*/\1/')
        ok "Go $ver"
        return
    fi

    step "Installing Go..."
    local arch
    arch=$(detect_arch)

    # Detect latest stable Go version
    local go_ver="1.25.0"
    local latest
    latest=$(curl -fsSL 'https://go.dev/dl/?mode=json' 2>/dev/null | sed -n 's/.*"version":"\(go[^"]*\)".*/\1/p' | head -1)
    if [[ -n "$latest" ]]; then
        go_ver="${latest#go}"
    fi

    local url="https://go.dev/dl/go${go_ver}.linux-${arch}.tar.gz"

    curl -fsSL "$url" -o /tmp/go.tar.gz
    rm -rf /usr/local/go
    tar -C /usr/local -xzf /tmp/go.tar.gz
    rm -f /tmp/go.tar.gz

    export PATH="/usr/local/go/bin:$PATH"
    echo 'export PATH=/usr/local/go/bin:$PATH' > /etc/profile.d/go.sh

    if has_cmd go; then
        ok "Go $(go version | sed 's/.*go\([0-9.]*\).*/\1/') installed"
    else
        err "Go installation failed"
        exit 1
    fi
}

# ── Build ──────────────────────────────────────────────────────

download_paqet() {
    step "Fetching latest release info..."
    local arch
    arch=$(detect_arch)
    local api_url="https://api.github.com/repos/$UPSTREAM_REPO/releases/latest"
    local tag="" dl_url=""

    # Get latest release tag and asset URL
    local json
    json=$(curl -fsSL "$api_url" 2>/dev/null || true)
    if [[ -n "$json" ]]; then
        tag=$(echo "$json" | sed -n 's/.*"tag_name":\s*"\([^"]*\)".*/\1/p' | head -1)
        dl_url=$(echo "$json" | sed -n "s|.*\"browser_download_url\":\s*\"\([^\"]*paqet-linux-${arch}[^\"]*\.tar\.gz\)\".*|\1|p" | head -1)
    fi

    if [[ -z "$dl_url" ]]; then
        warn "Could not find release for linux-$arch"
        return 1
    fi

    dim "Release: $tag"
    step "Downloading paqet ($arch)..."
    local tmp="/tmp/paqet-release.tar.gz"
    curl -fsSL "$dl_url" -o "$tmp"
    if [[ ! -f "$tmp" ]]; then
        err "Download failed"
        return 1
    fi

    # Extract binary
    local tmpdir="/tmp/paqet-extract"
    rm -rf "$tmpdir"
    mkdir -p "$tmpdir"
    tar -xzf "$tmp" -C "$tmpdir"
    rm -f "$tmp"

    local bin
    bin=$(find "$tmpdir" -name "paqet_linux_*" -type f | head -1)
    if [[ -z "$bin" ]]; then
        err "Binary not found in release archive"
        rm -rf "$tmpdir"
        return 1
    fi

    BUILT_BIN="$bin"
    ok "paqet downloaded ($tag, $(du -h "$bin" | cut -f1))"
}

build_paqet() {
    local src="/tmp/paqet-build"

    if [[ -d "$src/.git" ]]; then
        step "Updating source..."
        cd "$src" && git pull --quiet && git submodule update --init --recursive --quiet
    else
        step "Cloning repository..."
        rm -rf "$src"
        git clone --recursive "$REPO_URL" "$src" 2>/dev/null
    fi

    step "Building paqet (this may take a minute)..."
    cd "$src/paqet"
    local arch
    arch=$(detect_arch)
    CGO_ENABLED=1 GOOS=linux GOARCH="$arch" \
        go build -trimpath -ldflags='-s -w' -o "$src/paqet-bin" ./cmd/main.go 2>&1

    if [[ ! -f "$src/paqet-bin" ]]; then
        err "Build failed"
        exit 1
    fi
    ok "paqet built ($(du -h "$src/paqet-bin" | cut -f1))"
    BUILT_BIN="$src/paqet-bin"
}

get_paqet() {
    BUILT_BIN=""
    if [[ "${BUILD:-0}" == "1" ]]; then
        step "Mode: build from source"
        ensure_go
        echo ""
        build_paqet
    else
        step "Mode: download pre-built release"
        if ! download_paqet; then
            warn "Download failed, falling back to build from source..."
            ensure_go
            echo ""
            build_paqet
        fi
    fi
}

# ── Network Detection ──────────────────────────────────────────

detect_interface() {
    # Auto-detect default network interface
    local iface
    iface=$(ip -o -4 route show default 2>/dev/null | awk '{print $5}' | head -1)
    if [[ -z "$iface" ]]; then
        iface=$(ip link show 2>/dev/null | awk -F: '/^[0-9]+:/{if($2!~"lo") print $2}' | tr -d ' ' | head -1)
    fi
    echo "$iface"
}

detect_local_ip() {
    local iface="$1"
    ip -4 addr show "$iface" 2>/dev/null | sed -n 's/.*inet \([0-9.]*\).*/\1/p' | head -1
}

detect_public_ip() {
    curl -fsSL --connect-timeout 5 https://ifconfig.me 2>/dev/null || \
    curl -fsSL --connect-timeout 5 https://api.ipify.org 2>/dev/null || \
    detect_local_ip "$1"
}

detect_gateway_ip() {
    ip -o -4 route show default 2>/dev/null | awk '{print $3}' | head -1
}

detect_router_mac() {
    local gw_ip
    gw_ip=$(detect_gateway_ip)
    if [[ -n "$gw_ip" ]]; then
        # Ping gateway to ensure ARP entry exists
        ping -c 1 -W 1 "$gw_ip" >/dev/null 2>&1 || true
        ip neigh show "$gw_ip" 2>/dev/null | awk '{print $5}' | head -1
    fi
}

setup_iptables() {
    local port="$1"
    step "Configuring iptables for raw packet handling (port $port)..."

    # Remove existing rules first (ignore errors)
    iptables -t raw -D PREROUTING -p tcp --dport "$port" -j NOTRACK 2>/dev/null || true
    iptables -t raw -D OUTPUT -p tcp --sport "$port" -j NOTRACK 2>/dev/null || true
    iptables -t mangle -D OUTPUT -p tcp --sport "$port" --tcp-flags RST RST -j DROP 2>/dev/null || true

    # Add rules
    iptables -t raw -A PREROUTING -p tcp --dport "$port" -j NOTRACK
    iptables -t raw -A OUTPUT -p tcp --sport "$port" -j NOTRACK
    iptables -t mangle -A OUTPUT -p tcp --sport "$port" --tcp-flags RST RST -j DROP

    ok "iptables rules configured"

    # Persist iptables rules if possible
    if has_cmd netfilter-persistent; then
        netfilter-persistent save >/dev/null 2>&1 || true
    elif has_cmd iptables-save; then
        iptables-save > /etc/iptables.rules 2>/dev/null || true
    fi
}

# ── Commands ───────────────────────────────────────────────────

do_install() {
    banner
    [[ $EUID -ne 0 ]] && { err "Run as root: sudo $0 install"; exit 1; }

    ensure_packages
    echo ""

    BUILT_BIN=""
    get_paqet
    echo ""

    # Install binary
    step "Installing to $INSTALL_DIR..."
    mkdir -p "$INSTALL_DIR" "$CONFIG_DIR"
    cp "$BUILT_BIN" "$INSTALL_DIR/$BINARY"
    chmod +x "$INSTALL_DIR/$BINARY"
    ln -sf "$INSTALL_DIR/$BINARY" /usr/local/bin/paqet
    ok "Binary installed"

    # Generate key
    local secret
    if [[ -n "${KEY:-}" ]]; then
        secret="$KEY"
    else
        secret=$("$INSTALL_DIR/$BINARY" secret 2>/dev/null || openssl rand -base64 32)
        secret=$(echo "$secret" | tr -d '\n')
    fi

    # Server address
    local addr="${ADDR:-0.0.0.0:8443}"
    local port="${addr##*:}"

    # Auto-detect network
    local iface="${IFACE:-}"
    if [[ -z "$iface" ]]; then
        iface=$(detect_interface)
    fi
    if [[ -z "$iface" ]]; then
        err "Could not detect network interface. Use --iface <name>"
        exit 1
    fi

    local local_ip
    local_ip=$(detect_local_ip "$iface")
    if [[ -z "$local_ip" ]]; then
        err "Could not detect IP for interface $iface"
        exit 1
    fi

    local public_ip
    public_ip=$(detect_public_ip "$iface")

    local router_mac
    router_mac=$(detect_router_mac)
    if [[ -z "$router_mac" ]]; then
        err "Could not detect router MAC address. Check your network."
        exit 1
    fi

    dim "Interface: $iface"
    dim "Local IP:  $local_ip"
    dim "Public IP: $public_ip"
    dim "Router MAC: $router_mac"

    # Write config
    cat > "$CONFIG_DIR/server.yaml" <<EOF
role: "server"
log:
  level: "info"
listen:
  addr: ":$port"
network:
  interface: "$iface"
  ipv4:
    addr: "$local_ip:$port"
    router_mac: "$router_mac"
transport:
  protocol: "kcp"
  kcp:
    mode: "fast"
    key: "$secret"
EOF
    chmod 600 "$CONFIG_DIR/server.yaml"
    ok "Config created"

    # iptables rules for raw packet handling
    setup_iptables "$port"

    # Systemd service
    step "Creating systemd service..."
    cat > /etc/systemd/system/paqet.service <<EOF
[Unit]
Description=Paqet Tunnel Server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=$INSTALL_DIR/$BINARY run --config $CONFIG_DIR/server.yaml
Restart=always
RestartSec=5
LimitNOFILE=65535

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable paqet --quiet
    systemctl restart paqet
    ok "Service started"

    # Firewall
    step "Configuring firewall (UDP $port)..."
    if has_cmd ufw; then
        ufw allow "$port/udp" >/dev/null 2>&1 && ok "UFW rule added (UDP $port)"
        ufw allow "$port/tcp" >/dev/null 2>&1 && ok "UFW rule added (TCP $port)"
    elif has_cmd firewall-cmd; then
        firewall-cmd --permanent --add-port="$port/udp" >/dev/null 2>&1
        firewall-cmd --permanent --add-port="$port/tcp" >/dev/null 2>&1
        firewall-cmd --reload >/dev/null 2>&1 && ok "firewalld rules added"
    else
        warn "No firewall detected. Ensure TCP+UDP $port is open."
    fi

    # Save version
    echo "$VERSION" > "$INSTALL_DIR/.version"

    echo ""
    line
    ok "Server running!"
    echo ""
    echo -e "  ${B}Server details:${W}"
    dim "Interface: $iface"
    dim "Listen:    :$port"
    dim "Public IP: $public_ip"
    dim "Key:       $secret"
    echo ""
    dim "Service: systemctl status paqet"
    dim "Logs:    journalctl -u paqet -f"
    echo ""
    echo -e "  ${B}Windows client one-liner:${W}"
    echo -e "  ${C}irm https://raw.githubusercontent.com/$REPO/master/setup.ps1 -o \$env:TEMP\\pt.ps1; & \$env:TEMP\\pt.ps1 install -Addr ${public_ip}:${port} -Key \"${secret}\" -y${W}"
    echo ""
}

do_update() {
    banner
    [[ $EUID -ne 0 ]] && { err "Run as root: sudo $0 update"; exit 1; }

    BUILT_BIN=""
    get_paqet
    echo ""

    step "Updating..."
    systemctl stop paqet 2>/dev/null || true
    cp "$BUILT_BIN" "$INSTALL_DIR/$BINARY"
    chmod +x "$INSTALL_DIR/$BINARY"
    systemctl start paqet
    echo "$VERSION" > "$INSTALL_DIR/.version"

    ok "Updated and restarted"
    echo ""
}

do_uninstall() {
    banner
    [[ $EUID -ne 0 ]] && { err "Run as root: sudo $0 uninstall"; exit 1; }

    confirm "Uninstall paqet server?" || exit 0

    # Get port from config before removing
    local port=""
    if [[ -f "$CONFIG_DIR/server.yaml" ]]; then
        port=$(sed -n 's/.*addr:.*:\([0-9]*\)".*/\1/p' "$CONFIG_DIR/server.yaml" | head -1)
        [[ -z "$port" ]] && port=$(sed -n 's/.*addr:.*:\([0-9]*\)/\1/p' "$CONFIG_DIR/server.yaml" | head -1)
    fi

    step "Stopping service..."
    systemctl stop paqet 2>/dev/null || true
    systemctl disable paqet 2>/dev/null || true
    rm -f /etc/systemd/system/paqet.service
    systemctl daemon-reload

    step "Removing files..."
    rm -rf "$INSTALL_DIR" /usr/local/bin/paqet /tmp/paqet-build

    # Clean iptables rules
    if [[ -n "$port" ]]; then
        step "Removing iptables rules..."
        iptables -t raw -D PREROUTING -p tcp --dport "$port" -j NOTRACK 2>/dev/null || true
        iptables -t raw -D OUTPUT -p tcp --sport "$port" -j NOTRACK 2>/dev/null || true
        iptables -t mangle -D OUTPUT -p tcp --sport "$port" --tcp-flags RST RST -j DROP 2>/dev/null || true
        ok "iptables rules removed"
    fi

    if confirm "Remove configuration ($CONFIG_DIR)?"; then
        rm -rf "$CONFIG_DIR"
        ok "Config removed"
    fi

    ok "Uninstalled"
    echo ""
}

do_status() {
    banner
    if [[ -f "$INSTALL_DIR/$BINARY" ]]; then
        local ver
        ver=$("$INSTALL_DIR/$BINARY" version 2>/dev/null | head -1 || echo "unknown")
        echo -e "  Status    ${G}Installed${W}"
        echo -e "  Version   $ver"
    else
        echo -e "  Status    ${D}Not installed${W}"
        echo ""
        return
    fi

    if systemctl is-active --quiet paqet 2>/dev/null; then
        echo -e "  Service   ${G}Running ●${W}"
    else
        echo -e "  Service   ${R}Stopped${W}"
    fi

    if [[ -f "$CONFIG_DIR/server.yaml" ]]; then
        local listen_addr key iface
        listen_addr=$(sed -n '/^listen:/,/^[^ ]/{ s/.*addr:.*"\(.*\)".*/\1/p; }' "$CONFIG_DIR/server.yaml" 2>/dev/null | head -1)
        key=$(sed -n '/kcp:/,/^[^ ]/{ s/.*key:.*"\(.*\)".*/\1/p; }' "$CONFIG_DIR/server.yaml" 2>/dev/null | head -1)
        iface=$(sed -n 's/.*interface:.*"\(.*\)".*/\1/p' "$CONFIG_DIR/server.yaml" 2>/dev/null | head -1)
        [[ -n "$listen_addr" ]] && echo -e "  Listen    $listen_addr"
        [[ -n "$iface" ]] && echo -e "  Interface $iface"
        [[ -n "$key" ]] && echo -e "  Key       ${D}${key:0:8}...${W}"
    fi
    echo ""
}

show_help() {
    banner
    echo -e "  ${B}Usage:${W}"
    echo -e "    ${C}setup.sh [command] [options]${W}"
    echo ""
    echo -e "  ${B}Commands:${W}"
    dim "install      Install paqet server"
    dim "update       Update to latest"
    dim "uninstall    Remove paqet server"
    dim "status       Show status"
    dim "help         Show this help"
    echo ""
    echo -e "  ${B}Options:${W}"
    dim "--addr <ip:port>   Bind address (default: 0.0.0.0:8443)"
    dim "--key <secret>     Pre-shared key (auto-generated)"
    dim "--iface <name>     Network interface (auto-detected)"
    dim "--build            Build from source (default: download release)"
    dim "--yes              Skip confirmations"
    echo ""
    echo -e "  ${B}One-liner:${W}"
    echo -e "    ${C}curl -fsSL https://raw.githubusercontent.com/$REPO/master/setup.sh | sudo bash -s -- install${W}"
    echo ""
    echo -e "  ${B}Auto-configure:${W}"
    echo -e "    ${C}curl -fsSL https://raw.githubusercontent.com/$REPO/master/setup.sh | sudo bash -s -- install --addr 0.0.0.0:8443 --key YOUR_KEY --yes${W}"
    echo ""
}

show_menu() {
    banner
    local installed=0
    [[ -f "$INSTALL_DIR/$BINARY" ]] && installed=1

    if systemctl is-active --quiet paqet 2>/dev/null; then
        echo -e "  Status    ${G}Running ●${W}"
    elif [[ $installed -eq 1 ]]; then
        echo -e "  Status    ${C}Installed${W}"
    else
        echo -e "  Status    ${D}Not installed${W}"
    fi

    echo ""
    if [[ $installed -eq 1 ]]; then
        echo -e "  ${C}1${W}  Update"
        echo -e "  ${C}2${W}  Reinstall"
        echo -e "  ${C}3${W}  Uninstall"
        echo -e "  ${C}4${W}  Status"
        echo -e "  ${D}0${W}  ${D}Exit${W}"
    else
        echo -e "  ${C}1${W}  Install Server"
        echo -e "  ${C}2${W}  Status"
        echo -e "  ${C}3${W}  Help"
        echo -e "  ${D}0${W}  ${D}Exit${W}"
    fi

    echo ""
    echo -en "  ${D}Select${W} ${C}›${W} "
    read -r sel

    if [[ $installed -eq 1 ]]; then
        case "$sel" in
            1) do_update ;;
            2) do_install ;;
            3) do_uninstall ;;
            4) do_status ;;
            0) exit 0 ;;
            *) warn "Invalid" ;;
        esac
    else
        case "$sel" in
            1) do_install ;;
            2) do_status ;;
            3) show_help ;;
            0) exit 0 ;;
            *) warn "Invalid" ;;
        esac
    fi
}

# ── Parse Args ─────────────────────────────────────────────────

COMMAND="${1:-}"
ADDR="" KEY="" IFACE="" YES=0 BUILD=0
shift 2>/dev/null || true

while [[ $# -gt 0 ]]; do
    case "$1" in
        --addr) ADDR="$2"; shift 2 ;;
        --key)  KEY="$2"; shift 2 ;;
        --iface) IFACE="$2"; shift 2 ;;
        --build) BUILD=1; shift ;;
        --yes|-y) YES=1; shift ;;
        *) shift ;;
    esac
done

case "$COMMAND" in
    install)   do_install ;;
    update)    do_update ;;
    uninstall) do_uninstall ;;
    status)    do_status ;;
    help|-h|--help) show_help ;;
    "")        show_menu ;;
    *)         err "Unknown: $COMMAND"; show_help ;;
esac
