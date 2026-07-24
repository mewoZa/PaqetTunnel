using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace PaqetTunnel.Services;

/// <summary>
/// Detects the local Windows NIC settings required by paqet:
/// interface name, Npcap device GUID, local IPv4, and gateway MAC.
/// These are always the LOCAL machine's network — never the remote server's eth0/ens3.
/// </summary>
public static class NetworkDetectionService
{
    public sealed class NetworkInfo
    {
        public string InterfaceName { get; set; } = "";
        public string DeviceGuid { get; set; } = "";
        public string LocalIpv4 { get; set; } = "";
        public string RouterMac { get; set; } = "";
        public string GatewayIp { get; set; } = "";

        public bool HasInterface => !string.IsNullOrWhiteSpace(InterfaceName);
        public bool HasGuid => !string.IsNullOrWhiteSpace(DeviceGuid);
        public bool HasIpv4 => !string.IsNullOrWhiteSpace(LocalIpv4);
        public bool HasRouterMac => !string.IsNullOrWhiteSpace(RouterMac)
            && RouterMac != "00:00:00:00:00:00";
    }

    /// <summary>Detect the default-route physical interface and related fields.</summary>
    public static NetworkInfo Detect()
    {
        var info = new NetworkInfo();
        try
        {
            var ni = FindDefaultInterface();
            if (ni == null)
            {
                Logger.Warn("NetworkDetection: no suitable interface found");
                return info;
            }

            info.InterfaceName = ni.Name;
            info.DeviceGuid = FormatNpcapGuid(ni.Id);
            info.LocalIpv4 = GetIpv4(ni) ?? "";
            info.GatewayIp = GetGatewayIp(ni) ?? "";

            // Resolve gateway MAC via ARP (ping first to populate table)
            if (!string.IsNullOrEmpty(info.GatewayIp))
                info.RouterMac = ResolveMac(info.GatewayIp) ?? "";

            Logger.Info($"NetworkDetection: iface={info.InterfaceName}, ip={info.LocalIpv4}, " +
                        $"gw={info.GatewayIp}, mac={info.RouterMac}, guid={info.DeviceGuid}");
        }
        catch (Exception ex)
        {
            Logger.Error("NetworkDetection failed", ex);
        }
        return info;
    }

    /// <summary>
    /// Fill missing network fields on a config from live detection.
    /// Never overwrites non-empty user values unless <paramref name="force"/> is true.
    /// </summary>
    public static bool ApplyToConfig(Models.PaqetConfig config, bool force = false)
    {
        var net = Detect();
        var changed = false;

        // Replace empty or mistaken Linux server NIC names (eth0/ens3) with local Windows NIC
        if (net.HasInterface && (force || string.IsNullOrWhiteSpace(config.Interface)
                || IsLikelyWrongServerIface(config.Interface)))
        {
            config.Interface = net.InterfaceName;
            changed = true;
        }

        if ((force || string.IsNullOrWhiteSpace(config.DeviceGuid)) && net.HasGuid)
        {
            config.DeviceGuid = net.DeviceGuid;
            changed = true;
        }

        // Ipv4Addr is "ip:port" — client uses port 0
        if ((force || string.IsNullOrWhiteSpace(config.Ipv4Addr) || config.Ipv4Addr.StartsWith("0.0.0.0"))
            && net.HasIpv4)
        {
            config.Ipv4Addr = $"{net.LocalIpv4}:0";
            changed = true;
        }

        if ((force || string.IsNullOrWhiteSpace(config.RouterMac)
                || config.RouterMac == "00:00:00:00:00:00")
            && net.HasRouterMac)
        {
            config.RouterMac = net.RouterMac;
            changed = true;
        }

        if (changed)
            Logger.Info($"NetworkDetection applied: iface={config.Interface}, ipv4={config.Ipv4Addr}, " +
                        $"mac={config.RouterMac}, guid={config.DeviceGuid}");

        return changed;
    }

    /// <summary>
    /// True if the value looks like a Linux server NIC name (common user mistake
    /// when configuring the Windows client — see GitHub issue #1).
    /// </summary>
    public static bool IsLikelyWrongServerIface(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim().ToLowerInvariant();
        return n is "eth0" or "eth1" or "ens3" or "ens5" or "ens33" or "enp0s3"
            or "enp0s8" or "venet0" or "tun0" or "wg0"
            || Regex.IsMatch(n, @"^(eth|ens|enp|eno)\d+$");
    }

    private static NetworkInterface? FindDefaultInterface()
    {
        NetworkInterface? fallback = null;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel) continue;
            if (ni.Name.Contains("Wintun", StringComparison.OrdinalIgnoreCase)) continue;
            if (ni.Name.Contains("PaqetTun", StringComparison.OrdinalIgnoreCase)) continue;
            var props = ni.GetIPProperties();
            var hasGw = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(g.Address)
                && !g.Address.Equals(IPAddress.Any));
            var hasIp = props.UnicastAddresses.Any(a =>
                a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(a.Address));

            if (hasGw && hasIp)
                return ni;
            if (hasIp && fallback == null)
                fallback = ni;
        }
        return fallback;
    }

    private static string? GetIpv4(NetworkInterface ni)
    {
        return ni.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(a.Address))
            ?.Address.ToString();
    }

    private static string? GetGatewayIp(NetworkInterface ni)
    {
        return ni.GetIPProperties().GatewayAddresses
            .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(g.Address)
                && !g.Address.Equals(IPAddress.Any))
            ?.Address.ToString();
    }

    /// <summary>Format Windows adapter Id as Npcap device path.</summary>
    private static string FormatNpcapGuid(string adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId)) return "";
        // Id is typically "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}"
        var guid = adapterId.Trim();
        if (!guid.StartsWith('{')) guid = "{" + guid.Trim('{', '}') + "}";
        return $@"\Device\NPF_{guid}";
    }

    private static string? ResolveMac(string gatewayIp)
    {
        try
        {
            // Populate ARP cache
            using var ping = new Ping();
            try { ping.Send(gatewayIp, 500); } catch { /* ignore */ }

            // Prefer Get-NetNeighbor (accurate, works on Win10+)
            try
            {
                var ps = PaqetService.RunCommand("powershell",
                    $"-NoProfile -Command \"(Get-NetNeighbor -IPAddress {gatewayIp} -ErrorAction SilentlyContinue | Select-Object -First 1).LinkLayerAddress\"",
                    timeout: 8000);
                var mac = NormalizeMac(ps);
                if (mac != null) return mac;
            }
            catch { /* fall through */ }

            // Fallback: arp -a
            var arp = PaqetService.RunCommand("arp", $"-a {gatewayIp}", timeout: 5000);
            if (!string.IsNullOrEmpty(arp))
            {
                // Lines look like:  192.168.1.1          00-11-22-33-44-55     dynamic
                var m = Regex.Match(arp, Regex.Escape(gatewayIp) + @"\s+([0-9a-fA-F\-]{17})",
                    RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                if (m.Success)
                {
                    var mac = NormalizeMac(m.Groups[1].Value);
                    if (mac != null) return mac;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ResolveMac({gatewayIp}): {ex.Message}");
        }
        return null;
    }

    private static string? NormalizeMac(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Trim().Replace('-', ':').Replace('.', ':').ToLowerInvariant();
        // Collapse double separators
        cleaned = Regex.Replace(cleaned, ":+", ":");
        if (!Regex.IsMatch(cleaned, @"^([0-9a-f]{2}:){5}[0-9a-f]{2}$"))
            return null;
        if (cleaned is "00:00:00:00:00:00" or "ff:ff:ff:ff:ff:ff")
            return null;
        return cleaned;
    }
}
