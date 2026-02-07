#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════
# Paqet Tunnel — Linux Server Setup
# Usage: curl -fsSL https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh | sudo bash
#    Or: ./setup.sh [install|update|uninstall|status] [options]
#
# Options:
#   --addr <ip:port>   Bind address (default: 0.0.0.0:8443)
#   --key <secret>     Pre-shared key (auto-generated if omitted)
#   --yes              Skip confirmations
# ═══════════════════════════════════════════════════════════════════
set -euo pipefail

VERSION="1.0.0"
REPO="mewoZa/PaqetTunnel"
REPO_URL="https://github.com/$REPO.git"
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
            apt-get install -y -qq git curl wget >/dev/null 2>&1
            ;;
        centos|rhel|rocky|alma|fedora)
            if has_cmd dnf; then
                dnf install -y -q git curl wget >/dev/null 2>&1
            else
                yum install -y -q git curl wget >/dev/null 2>&1
            fi
            ;;
        arch|manjaro)
            pacman -Sy --noconfirm git curl wget >/dev/null 2>&1
            ;;
        *)
            warn "Unknown distro ($os). Ensure git, curl, wget are installed."
            ;;
    esac
    ok "System packages ready"
}

ensure_go() {
    if has_cmd go; then
        local ver
        ver=$(go version 2>/dev/null | grep -oP 'go\K[0-9.]+')
        ok "Go $ver"
        return
    fi

    step "Installing Go..."
    local arch
    arch=$(detect_arch)
    local go_ver="1.24.0"
    local url="https://go.dev/dl/go${go_ver}.linux-${arch}.tar.gz"

    curl -fsSL "$url" -o /tmp/go.tar.gz
    rm -rf /usr/local/go
    tar -C /usr/local -xzf /tmp/go.tar.gz
    rm -f /tmp/go.tar.gz

    export PATH="/usr/local/go/bin:$PATH"
    echo 'export PATH=/usr/local/go/bin:$PATH' > /etc/profile.d/go.sh

    if has_cmd go; then
        ok "Go $(go version | grep -oP 'go\K[0-9.]+') installed"
    else
        err "Go installation failed"
        exit 1
    fi
}

# ── Build ──────────────────────────────────────────────────────

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

    step "Building paqet..."
    cd "$src/paqet"
    local arch
    arch=$(detect_arch)
    CGO_ENABLED=0 GOOS=linux GOARCH="$arch" \
        go build -trimpath -ldflags='-s -w' -o "$src/paqet" ./cmd/paqet 2>&1

    if [[ ! -f "$src/paqet" ]]; then
        err "Build failed"
        exit 1
    fi
    ok "paqet built ($(du -h "$src/paqet" | cut -f1))"
    echo "$src/paqet"
}

# ── Commands ───────────────────────────────────────────────────

do_install() {
    banner
    [[ $EUID -ne 0 ]] && { err "Run as root: sudo $0 install"; exit 1; }

    ensure_packages
    ensure_go
    echo ""

    local built_bin
    built_bin=$(build_paqet)
    echo ""

    # Install binary
    step "Installing to $INSTALL_DIR..."
    mkdir -p "$INSTALL_DIR" "$CONFIG_DIR"
    cp "$built_bin" "$INSTALL_DIR/$BINARY"
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

    # Write config
    cat > "$CONFIG_DIR/server.yaml" <<EOF
role: "server"
log:
  level: "info"
server:
  addr: "$addr"
  key: "$secret"
EOF
    chmod 600 "$CONFIG_DIR/server.yaml"
    ok "Config created"

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
    local port="${addr##*:}"
    step "Configuring firewall (UDP $port)..."
    if has_cmd ufw; then
        ufw allow "$port/udp" >/dev/null 2>&1 && ok "UFW rule added"
    elif has_cmd firewall-cmd; then
        firewall-cmd --permanent --add-port="$port/udp" >/dev/null 2>&1
        firewall-cmd --reload >/dev/null 2>&1 && ok "firewalld rule added"
    else
        warn "No firewall detected. Ensure UDP $port is open."
    fi

    # Save version
    echo "$VERSION" > "$INSTALL_DIR/.version"

    echo ""
    line
    ok "Server running!"
    echo ""
    echo -e "  ${B}Client configuration:${W}"
    dim "Server:  $addr"
    dim "Key:     $secret"
    echo ""
    dim "Service: systemctl status paqet"
    dim "Logs:    journalctl -u paqet -f"
    echo ""
}

do_update() {
    banner
    [[ $EUID -ne 0 ]] && { err "Run as root: sudo $0 update"; exit 1; }

    ensure_go
    echo ""

    local built_bin
    built_bin=$(build_paqet)
    echo ""

    step "Updating..."
    systemctl stop paqet 2>/dev/null || true
    cp "$built_bin" "$INSTALL_DIR/$BINARY"
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

    step "Stopping service..."
    systemctl stop paqet 2>/dev/null || true
    systemctl disable paqet 2>/dev/null || true
    rm -f /etc/systemd/system/paqet.service
    systemctl daemon-reload

    step "Removing files..."
    rm -rf "$INSTALL_DIR" /usr/local/bin/paqet /tmp/paqet-build

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
        local addr
        addr=$(grep -oP 'addr:\s*"\K[^"]+' "$CONFIG_DIR/server.yaml" 2>/dev/null || echo "—")
        echo -e "  Listen    $addr"
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
    dim "--yes              Skip confirmations"
    echo ""
    echo -e "  ${B}One-liner:${W}"
    echo -e "    ${C}curl -fsSL https://raw.githubusercontent.com/$REPO/master/setup.sh | sudo bash${W}"
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
ADDR="" KEY="" YES=0
shift 2>/dev/null || true

while [[ $# -gt 0 ]]; do
    case "$1" in
        --addr) ADDR="$2"; shift 2 ;;
        --key)  KEY="$2"; shift 2 ;;
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
