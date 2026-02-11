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
    public string Role { get; set; } = "client";
    public string ServerAddr { get; set; } = "";         // server.addr = "host:port"
    public string Key { get; set; } = "";                 // transport.kcp.key
    public string Interface { get; set; } = "";           // network.interface
    public string DeviceGuid { get; set; } = "";          // network.guid
    public string Ipv4Addr { get; set; } = "";            // network.ipv4.addr
    public string RouterMac { get; set; } = "";           // network.ipv4.router_mac
    public string SocksListen { get; set; } = "127.0.0.1:10800";
    public string Protocol { get; set; } = "";               // transport.protocol
    public string KcpMode { get; set; } = "";                // transport.kcp.mode
    public string RawConfig { get; set; } = "";

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
            }
        }

        return config;
    }

    /// <summary>
    /// Serialize config back to YAML. If RawConfig exists, do targeted replacements
    /// to preserve structure. Otherwise generate fresh.
    /// </summary>
    public string ToYaml()
    {
        if (!string.IsNullOrEmpty(RawConfig))
            return UpdateRawConfig();

        return string.Join("\n",
            $"role: \"{Role}\"",
            "log:",
            "  level: \"info\"",
            "",
            "socks5:",
            $"  - listen: \"{SocksListen}\"",
            "",
            "network:",
            $"  interface: \"{Interface}\"",
            $"  guid: \"{DeviceGuid}\"",
            "  ipv4:",
            $"    addr: \"{Ipv4Addr}\"",
            $"    router_mac: \"{RouterMac}\"",
            "  tcp:",
            "    local_flag: [\"PA\"]",
            "    remote_flag: [\"PA\"]",
            "",
            "server:",
            $"  addr: \"{ServerAddr}\"",
            "",
            "transport:",
            "  protocol: \"kcp\"",
            "  kcp:",
            "    mode: \"fast\"",
            "    block: \"aes\"",
            $"    key: \"{Key}\"",
            "");
    }

    /// <summary>Update values in the raw config string, preserving unknown fields.</summary>
    private string UpdateRawConfig()
    {
        var result = RawConfig;
        result = ReplaceYamlValue(result, "addr", ServerAddr, "server");
        result = ReplaceYamlValue(result, "key", Key, "kcp");
        result = ReplaceYamlValue(result, "interface", Interface, "network");
        result = ReplaceYamlValue(result, "listen", SocksListen, "socks5");
        // BUG-12 fix: persist guid, ipv4.addr, router_mac too
        result = ReplaceYamlValue(result, "guid", DeviceGuid, "network");
        result = ReplaceYamlValue(result, "addr", Ipv4Addr, "ipv4");
        result = ReplaceYamlValue(result, "router_mac", RouterMac, "ipv4");
        return result;
    }

    /// <summary>Replace a YAML value in context of a parent section (first occurrence only).</summary>
    private static string ReplaceYamlValue(string yaml, string key, string newValue, string parentSection)
    {
        var pattern = $@"({Regex.Escape(key)}\s*:\s*)" + "\"[^\"]*\"";
        var sectionIdx = yaml.IndexOf(parentSection + ":", StringComparison.Ordinal);
        if (sectionIdx < 0)
        {
            // Fallback: replace first occurrence anywhere
            var m = Regex.Match(yaml, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!m.Success) return yaml;
            return yaml[..m.Index] + Regex.Replace(m.Value, pattern, $"$1\"{newValue}\"", RegexOptions.None, TimeSpan.FromSeconds(1)) + yaml[(m.Index + m.Length)..];
        }

        // NEW-08 fix: find next top-level section to bound the search region
        // A top-level section is a line starting with a non-space char followed by ':'
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
