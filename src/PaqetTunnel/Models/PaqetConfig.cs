using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaqetTunnel.Models;

/// <summary>
/// Represents the paqet client.yaml configuration.
/// Handles the real paqet config format with nested sections.
/// </summary>
public sealed class PaqetConfig
{
    // ── Core ──
    public string Role { get; set; } = "client";
    public string ServerAddr { get; set; } = "";         // server.addr
    public string Key { get; set; } = "";                 // transport.kcp.key
    public string Interface { get; set; } = "";           // network.interface
    public string DeviceGuid { get; set; } = "";          // network.guid
    public string Ipv4Addr { get; set; } = "";            // network.ipv4.addr
    public string RouterMac { get; set; } = "";           // network.ipv4.router_mac
    public string SocksListen { get; set; } = "127.0.0.1:10800";
    public string Protocol { get; set; } = "";            // transport.protocol

    // ── Logging ──
    public string LogLevel { get; set; } = "info";       // log.level

    // ── KCP Transport ──
    public string KcpMode { get; set; } = "fast";        // transport.kcp.mode
    public string KcpBlock { get; set; } = "aes";        // transport.kcp.block
    public int Conn { get; set; } = 1;                    // transport.conn
    public int Mtu { get; set; } = 1350;                  // transport.kcp.mtu
    public int RcvWnd { get; set; } = 512;                // transport.kcp.rcvwnd
    public int SndWnd { get; set; } = 512;                // transport.kcp.sndwnd

    // ── KCP Manual Mode (only when mode="manual") ──
    public int Nodelay { get; set; } = -1;                // transport.kcp.nodelay (-1 = not set)
    public int Interval { get; set; } = -1;               // transport.kcp.interval
    public int Resend { get; set; } = -1;                 // transport.kcp.resend
    public int NoCongestion { get; set; } = -1;           // transport.kcp.nocongestion
    public string WDelay { get; set; } = "";              // transport.kcp.wdelay (bool as string)
    public string AckNodelay { get; set; } = "";          // transport.kcp.acknodelay

    // ── Buffers ──
    public int SmuxBuf { get; set; } = 4194304;          // transport.kcp.smuxbuf (4MB)
    public int StreamBuf { get; set; } = 2097152;        // transport.kcp.streambuf (2MB)
    public int TcpBuf { get; set; } = 8192;              // transport.tcpbuf
    public int UdpBuf { get; set; } = 4096;              // transport.udpbuf
    public int PcapSockBuf { get; set; } = 4194304;      // network.pcap.sockbuf (4MB)

    // ── TCP Flags ──
    public string LocalFlag { get; set; } = "PA";        // network.tcp.local_flag
    public string RemoteFlag { get; set; } = "PA";       // network.tcp.remote_flag

    // ── SOCKS5 Auth ──
    public string SocksUsername { get; set; } = "";       // socks5[].username
    public string SocksPassword { get; set; } = "";       // socks5[].password

    public string RawConfig { get; set; } = "";

    // ── Default values for comparison ──
    public static readonly string DefaultKcpMode = "fast";
    public static readonly string DefaultKcpBlock = "aes";
    public static readonly string DefaultLogLevel = "info";
    public static readonly int DefaultConn = 1;
    public static readonly int DefaultMtu = 1350;
    public static readonly int DefaultRcvWnd = 512;
    public static readonly int DefaultSndWnd = 512;
    public static readonly int DefaultSmuxBuf = 4194304;
    public static readonly int DefaultStreamBuf = 2097152;
    public static readonly int DefaultTcpBuf = 8192;
    public static readonly int DefaultUdpBuf = 4096;
    public static readonly int DefaultPcapSockBuf = 4194304;

    public static readonly string[] ValidKcpModes = ["normal", "fast", "fast2", "fast3", "manual"];
    public static readonly string[] ValidCiphers = ["aes", "aes-128", "aes-128-gcm", "aes-192", "salsa20", "blowfish", "twofish", "cast5", "3des", "tea", "xtea", "xor", "sm4", "none"];
    public static readonly string[] ValidLogLevels = ["none", "debug", "info", "warn", "error", "fatal"];
    public static readonly string[] ValidTcpFlags = ["PA", "S", "A", "SA", "FA", "R", "P", "F"];

    /// <summary>Get server host (without port) from ServerAddr. Handles IPv6 [::1]:port format.</summary>
    public string ServerHost
    {
        get
        {
            if (string.IsNullOrEmpty(ServerAddr)) return ServerAddr;
            // R3-20 fix: handle bracketed IPv6 like "[::1]:8443"
            if (ServerAddr.StartsWith('['))
            {
                var endBracket = ServerAddr.IndexOf(']');
                return endBracket > 0 ? ServerAddr[1..endBracket] : ServerAddr;
            }
            var lastColon = ServerAddr.LastIndexOf(':');
            return lastColon > 0 ? ServerAddr[..lastColon] : ServerAddr;
        }
    }

    /// <summary>Get server port from ServerAddr. Handles IPv6 [::1]:port format.</summary>
    public int ServerPort
    {
        get
        {
            if (string.IsNullOrEmpty(ServerAddr)) return 443;
            if (ServerAddr.StartsWith('['))
            {
                var afterBracket = ServerAddr.IndexOf("]:");
                return afterBracket >= 0 && int.TryParse(ServerAddr[(afterBracket + 2)..], out var p6) ? p6 : 443;
            }
            var lastColon = ServerAddr.LastIndexOf(':');
            return lastColon > 0 && int.TryParse(ServerAddr[(lastColon + 1)..], out var p) ? p : 443;
        }
    }

    /// <summary>Parse a paqet YAML config file.</summary>
    public static PaqetConfig FromYaml(string yaml)
    {
        var config = new PaqetConfig { RawConfig = yaml };
        var sectionPath = new List<string>();

        foreach (var rawLine in yaml.Split('\n'))
        {
            var trimmed = rawLine.TrimEnd();
            if (string.IsNullOrEmpty(trimmed) || trimmed.TrimStart().StartsWith('#'))
                continue;

            // Calculate indent level (supports 2-space and 4-space indentation)
            var indent = trimmed.Length - trimmed.TrimStart().Length;
            var line = trimmed.TrimStart();

            // Strip list marker
            if (line.StartsWith("- "))
                line = line[2..];

            // Adjust section stack based on indent — detect indentation step from first indented line
            var level = indent > 0 && sectionPath.Count > 0 ? indent / Math.Max(2, indent / sectionPath.Count) : indent / 2;
            while (sectionPath.Count > level)
                sectionPath.RemoveAt(sectionPath.Count - 1);

            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = line[..colonIdx].Trim();
            var valuePart = line[(colonIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(valuePart))
            {
                // Section header — push onto path
                sectionPath.Add(key);
                continue;
            }

            var value = valuePart.Trim('"').Trim('\'');
            var fullKey = sectionPath.Count > 0 ? string.Join(".", sectionPath) + "." + key : key;

            switch (fullKey)
            {
                case "role": config.Role = value; break;
                case "server.addr": config.ServerAddr = value; break;
                case "transport.kcp.key": config.Key = value; break;
                case "network.interface": config.Interface = value; break;
                case "network.guid": config.DeviceGuid = value; break;
                case "network.ipv4.addr": config.Ipv4Addr = value; break;
                case "network.ipv4.router_mac": config.RouterMac = value; break;
                case "socks5.listen": config.SocksListen = value; break;
                case "transport.protocol": config.Protocol = value; break;
                case "transport.kcp.mode": config.KcpMode = value; break;
                // Logging
                case "log.level": config.LogLevel = value; break;
                // KCP transport
                case "transport.kcp.block": config.KcpBlock = value; break;
                case "transport.conn": if (int.TryParse(value, out var conn)) config.Conn = conn; break;
                case "transport.kcp.mtu": if (int.TryParse(value, out var mtu)) config.Mtu = mtu; break;
                case "transport.kcp.rcvwnd": if (int.TryParse(value, out var rw)) config.RcvWnd = rw; break;
                case "transport.kcp.sndwnd": if (int.TryParse(value, out var sw)) config.SndWnd = sw; break;
                // KCP manual mode
                case "transport.kcp.nodelay": if (int.TryParse(value, out var nd)) config.Nodelay = nd; break;
                case "transport.kcp.interval": if (int.TryParse(value, out var iv)) config.Interval = iv; break;
                case "transport.kcp.resend": if (int.TryParse(value, out var rs)) config.Resend = rs; break;
                case "transport.kcp.nocongestion": if (int.TryParse(value, out var nc)) config.NoCongestion = nc; break;
                case "transport.kcp.wdelay": config.WDelay = value; break;
                case "transport.kcp.acknodelay": config.AckNodelay = value; break;
                // Buffers
                case "transport.kcp.smuxbuf": if (int.TryParse(value, out var sb)) config.SmuxBuf = sb; break;
                case "transport.kcp.streambuf": if (int.TryParse(value, out var stb)) config.StreamBuf = stb; break;
                case "transport.tcpbuf": if (int.TryParse(value, out var tb)) config.TcpBuf = tb; break;
                case "transport.udpbuf": if (int.TryParse(value, out var ub)) config.UdpBuf = ub; break;
                case "network.pcap.sockbuf": if (int.TryParse(value, out var ps)) config.PcapSockBuf = ps; break;
                // TCP flags (stored as arrays in YAML, we parse first element)
                case "network.tcp.local_flag": config.LocalFlag = ParseFlagValue(value); break;
                case "network.tcp.remote_flag": config.RemoteFlag = ParseFlagValue(value); break;
                // SOCKS5 auth
                case "socks5.username": config.SocksUsername = value; break;
                case "socks5.password": config.SocksPassword = value; break;
            }
        }

        return config;
    }

    /// <summary>Parse a YAML array value like ["PA"] or bare value like PA.</summary>
    private static string ParseFlagValue(string value)
    {
        // Handle ["PA"] or ["PA", "S"] format — take first element
        var m = Regex.Match(value, "\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : value.Trim('[', ']', ' ');
    }

    /// <summary>
    /// Serialize config back to YAML. If RawConfig exists, do targeted replacements
    /// to preserve structure. Otherwise generate fresh.
    /// </summary>
    public string ToYaml()
    {
        if (!string.IsNullOrEmpty(RawConfig))
            return UpdateRawConfig();

        var lines = new List<string>
        {
            $"role: \"{Role}\"",
            "log:",
            $"  level: \"{LogLevel}\"",
            "",
            "socks5:",
            $"  - listen: \"{SocksListen}\""
        };
        if (!string.IsNullOrEmpty(SocksUsername))
            lines.Add($"    username: \"{SocksUsername}\"");
        if (!string.IsNullOrEmpty(SocksPassword))
            lines.Add($"    password: \"{SocksPassword}\"");

        lines.AddRange([
            "",
            "network:",
            $"  interface: \"{Interface}\"",
            $"  guid: \"{DeviceGuid}\"",
            "  ipv4:",
            $"    addr: \"{Ipv4Addr}\"",
            $"    router_mac: \"{RouterMac}\"",
            "  tcp:",
            $"    local_flag: [\"{LocalFlag}\"]",
            $"    remote_flag: [\"{RemoteFlag}\"]"
        ]);
        if (PcapSockBuf != DefaultPcapSockBuf)
        {
            lines.Add("  pcap:");
            lines.Add($"    sockbuf: {PcapSockBuf}");
        }

        lines.AddRange([
            "",
            "server:",
            $"  addr: \"{ServerAddr}\"",
            "",
            "transport:",
            "  protocol: \"kcp\""
        ]);
        if (Conn != DefaultConn) lines.Add($"  conn: {Conn}");
        if (TcpBuf != DefaultTcpBuf) lines.Add($"  tcpbuf: {TcpBuf}");
        if (UdpBuf != DefaultUdpBuf) lines.Add($"  udpbuf: {UdpBuf}");

        lines.AddRange([
            "  kcp:",
            $"    mode: \"{KcpMode}\"",
            $"    block: \"{KcpBlock}\"",
            $"    key: \"{Key}\""
        ]);
        if (Mtu != DefaultMtu) lines.Add($"    mtu: {Mtu}");
        if (RcvWnd != DefaultRcvWnd) lines.Add($"    rcvwnd: {RcvWnd}");
        if (SndWnd != DefaultSndWnd) lines.Add($"    sndwnd: {SndWnd}");
        if (SmuxBuf != DefaultSmuxBuf) lines.Add($"    smuxbuf: {SmuxBuf}");
        if (StreamBuf != DefaultStreamBuf) lines.Add($"    streambuf: {StreamBuf}");
        // Manual mode params
        if (Nodelay >= 0) lines.Add($"    nodelay: {Nodelay}");
        if (Interval >= 0) lines.Add($"    interval: {Interval}");
        if (Resend >= 0) lines.Add($"    resend: {Resend}");
        if (NoCongestion >= 0) lines.Add($"    nocongestion: {NoCongestion}");
        if (!string.IsNullOrEmpty(WDelay)) lines.Add($"    wdelay: {WDelay}");
        if (!string.IsNullOrEmpty(AckNodelay)) lines.Add($"    acknodelay: {AckNodelay}");

        lines.Add("");
        return string.Join("\n", lines);
    }

    /// <summary>Update values in the raw config string, preserving unknown fields.</summary>
    private string UpdateRawConfig()
    {
        var result = RawConfig;
        // Core fields
        result = ReplaceYamlValue(result, "addr", ServerAddr, "server");
        result = ReplaceYamlValue(result, "key", Key, "kcp");
        result = ReplaceYamlValue(result, "interface", Interface, "network");
        result = ReplaceYamlValue(result, "listen", SocksListen, "socks5");
        result = ReplaceYamlValue(result, "guid", DeviceGuid, "network");
        result = ReplaceYamlValue(result, "addr", Ipv4Addr, "ipv4");
        result = ReplaceYamlValue(result, "router_mac", RouterMac, "ipv4");
        // KCP settings
        result = ReplaceOrInsertYamlValue(result, "mode", KcpMode, "kcp");
        result = ReplaceOrInsertYamlValue(result, "block", KcpBlock, "kcp");
        result = ReplaceOrInsertYamlValue(result, "level", LogLevel, "log");
        // Numeric KCP settings
        if (Conn != DefaultConn) result = ReplaceOrInsertYamlNumeric(result, "conn", Conn, "transport");
        if (Mtu != DefaultMtu) result = ReplaceOrInsertYamlNumeric(result, "mtu", Mtu, "kcp");
        if (RcvWnd != DefaultRcvWnd) result = ReplaceOrInsertYamlNumeric(result, "rcvwnd", RcvWnd, "kcp");
        if (SndWnd != DefaultSndWnd) result = ReplaceOrInsertYamlNumeric(result, "sndwnd", SndWnd, "kcp");
        // Buffers
        if (SmuxBuf != DefaultSmuxBuf) result = ReplaceOrInsertYamlNumeric(result, "smuxbuf", SmuxBuf, "kcp");
        if (StreamBuf != DefaultStreamBuf) result = ReplaceOrInsertYamlNumeric(result, "streambuf", StreamBuf, "kcp");
        if (TcpBuf != DefaultTcpBuf) result = ReplaceOrInsertYamlNumeric(result, "tcpbuf", TcpBuf, "transport");
        if (UdpBuf != DefaultUdpBuf) result = ReplaceOrInsertYamlNumeric(result, "udpbuf", UdpBuf, "transport");
        if (PcapSockBuf != DefaultPcapSockBuf) result = ReplaceOrInsertYamlNumeric(result, "sockbuf", PcapSockBuf, "pcap");
        // Manual mode params
        if (Nodelay >= 0) result = ReplaceOrInsertYamlNumeric(result, "nodelay", Nodelay, "kcp");
        if (Interval >= 0) result = ReplaceOrInsertYamlNumeric(result, "interval", Interval, "kcp");
        if (Resend >= 0) result = ReplaceOrInsertYamlNumeric(result, "resend", Resend, "kcp");
        if (NoCongestion >= 0) result = ReplaceOrInsertYamlNumeric(result, "nocongestion", NoCongestion, "kcp");
        // SOCKS5 auth
        if (!string.IsNullOrEmpty(SocksUsername)) result = ReplaceOrInsertYamlValue(result, "username", SocksUsername, "socks5");
        if (!string.IsNullOrEmpty(SocksPassword)) result = ReplaceOrInsertYamlValue(result, "password", SocksPassword, "socks5");
        return result;
    }

    /// <summary>Replace a YAML value in context of a parent section (first occurrence only).</summary>
    private static string ReplaceYamlValue(string yaml, string key, string newValue, string parentSection)
    {
        var pattern = $@"({Regex.Escape(key)}\s*:\s*)" + "\"[^\"]*\"";
        var sectionIdx = yaml.IndexOf(parentSection + ":", StringComparison.Ordinal);
        if (sectionIdx < 0)
        {
            var m = Regex.Match(yaml, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!m.Success) return yaml;
            return yaml[..m.Index] + Regex.Replace(m.Value, pattern, $"$1\"{newValue}\"", RegexOptions.None, TimeSpan.FromSeconds(1)) + yaml[(m.Index + m.Length)..];
        }

        var afterSection = yaml[(sectionIdx + parentSection.Length + 1)..];
        var nextSectionMatch = Regex.Match(afterSection, @"\n[a-zA-Z_][a-zA-Z0-9_]*\s*:", RegexOptions.None, TimeSpan.FromSeconds(1));
        var searchEnd = nextSectionMatch.Success
            ? sectionIdx + parentSection.Length + 1 + nextSectionMatch.Index
            : yaml.Length;

        var region = yaml[sectionIdx..searchEnd];
        var match = Regex.Match(region, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) return yaml;

        var replaced = region[..match.Index]
            + Regex.Replace(match.Value, pattern, $"$1\"{newValue}\"", RegexOptions.None, TimeSpan.FromSeconds(1))
            + region[(match.Index + match.Length)..];
        return yaml[..sectionIdx] + replaced + yaml[searchEnd..];
    }

    /// <summary>Replace a quoted YAML value, or insert if missing.</summary>
    private static string ReplaceOrInsertYamlValue(string yaml, string key, string newValue, string parentSection)
    {
        var pattern = $@"({Regex.Escape(key)}\s*:\s*)" + "\"[^\"]*\"";
        var sectionIdx = yaml.IndexOf(parentSection + ":", StringComparison.Ordinal);
        if (sectionIdx < 0) return yaml;

        var afterSection = yaml[(sectionIdx + parentSection.Length + 1)..];
        var nextSectionMatch = Regex.Match(afterSection, @"\n[a-zA-Z_][a-zA-Z0-9_]*\s*:", RegexOptions.None, TimeSpan.FromSeconds(1));
        var searchEnd = nextSectionMatch.Success
            ? sectionIdx + parentSection.Length + 1 + nextSectionMatch.Index
            : yaml.Length;

        var region = yaml[sectionIdx..searchEnd];
        var match = Regex.Match(region, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            var replaced = region[..match.Index]
                + Regex.Replace(match.Value, pattern, $"$1\"{newValue}\"", RegexOptions.None, TimeSpan.FromSeconds(1))
                + region[(match.Index + match.Length)..];
            return yaml[..sectionIdx] + replaced + yaml[searchEnd..];
        }

        // Insert after the parent section header line
        var headerEnd = yaml.IndexOf('\n', sectionIdx);
        if (headerEnd < 0) headerEnd = yaml.Length;
        // Detect indent from existing children
        var childMatch = Regex.Match(yaml[(headerEnd)..searchEnd], @"\n(\s+)\w");
        var indent = childMatch.Success ? childMatch.Groups[1].Value : "    ";
        var insertion = $"\n{indent}{key}: \"{newValue}\"";
        return yaml[..(headerEnd)] + insertion + yaml[headerEnd..];
    }

    /// <summary>Replace or insert a numeric YAML value.</summary>
    private static string ReplaceOrInsertYamlNumeric(string yaml, string key, int newValue, string parentSection)
    {
        var pattern = $@"({Regex.Escape(key)}\s*:\s*)\d+";
        var sectionIdx = yaml.IndexOf(parentSection + ":", StringComparison.Ordinal);
        if (sectionIdx < 0) return yaml;

        var afterSection = yaml[(sectionIdx + parentSection.Length + 1)..];
        var nextSectionMatch = Regex.Match(afterSection, @"\n[a-zA-Z_][a-zA-Z0-9_]*\s*:", RegexOptions.None, TimeSpan.FromSeconds(1));
        var searchEnd = nextSectionMatch.Success
            ? sectionIdx + parentSection.Length + 1 + nextSectionMatch.Index
            : yaml.Length;

        var region = yaml[sectionIdx..searchEnd];
        var match = Regex.Match(region, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            var replaced = region[..match.Index]
                + Regex.Replace(match.Value, pattern, $"$1{newValue}", RegexOptions.None, TimeSpan.FromSeconds(1))
                + region[(match.Index + match.Length)..];
            return yaml[..sectionIdx] + replaced + yaml[searchEnd..];
        }

        var headerEnd = yaml.IndexOf('\n', sectionIdx);
        if (headerEnd < 0) headerEnd = yaml.Length;
        var childMatch = Regex.Match(yaml[(headerEnd)..searchEnd], @"\n(\s+)\w");
        var indent = childMatch.Success ? childMatch.Groups[1].Value : "    ";
        return yaml[..(headerEnd)] + $"\n{indent}{key}: {newValue}" + yaml[headerEnd..];
    }
}

/// <summary>
/// Snapshot of the current network speed.
/// </summary>
public sealed record SpeedSnapshot(
    long BytesReceived,
    long BytesSent,
    double DownloadSpeed,
    double UploadSpeed,
    DateTime Timestamp);

/// <summary>
/// App-level settings persisted in %APPDATA%.
/// </summary>
public sealed class AppSettings
{
    public bool AutoStart { get; set; }
    public bool StartBeforeLogon { get; set; }
    public bool SystemProxyEnabled { get; set; }
    public bool ProxySharingEnabled { get; set; }
    public bool AutoConnectOnLaunch { get; set; }
    public bool StartMinimized { get; set; }
    public bool DebugMode { get; set; }
    public bool FullSystemTunnel { get; set; }
    public string Theme { get; set; } = "dark";
    public string DnsProvider { get; set; } = "auto";
    public string CustomDnsPrimary { get; set; } = "";
    public string CustomDnsSecondary { get; set; } = "";
    public string PaqetBinaryPath { get; set; } = "";
    public string PaqetConfigPath { get; set; } = "";

    // Server SSH management
    public string ServerSshHost { get; set; } = "";
    public int ServerSshPort { get; set; } = 22;
    public string ServerSshUser { get; set; } = "root";
    public string ServerSshKeyPath { get; set; } = "";
    public string ServerSshPassword { get; set; } = "";
    /// <summary>DPAPI-encrypted form of ServerSshPassword for safe on-disk storage.</summary>
    public string ServerSshPasswordProtected { get; set; } = "";
}
