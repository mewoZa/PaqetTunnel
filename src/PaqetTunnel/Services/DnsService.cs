using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

/// <summary>
/// Manages DNS configuration for the tunnel. Provides smart DNS provider selection
/// with latency benchmarking, leak prevention, and forced DNS in both SOCKS5 and TUN modes.
/// </summary>
public static class DnsService
{
    // ── DNS Provider Presets ──────────────────────────────────────
    // Comprehensive list optimized for gaming, streaming, privacy, and security.

    public static readonly Dictionary<string, DnsPreset> Providers = new()
    {
        ["cloudflare"] = new("Cloudflare", "1.1.1.1", "1.0.0.1",
            "Fastest global DNS, best privacy, DoH/DoT support"),
        ["google"] = new("Google", "8.8.8.8", "8.8.4.4",
            "Ultra reliable, global coverage, DoH/DoT support"),
        ["quad9"] = new("Quad9", "9.9.9.9", "149.112.112.112",
            "Security focused, blocks malware domains, DoH/DoT"),
        ["opendns"] = new("OpenDNS", "208.67.222.222", "208.67.220.220",
            "Cisco owned, phishing protection, customizable"),
        ["adguard"] = new("AdGuard", "94.140.14.14", "94.140.15.15",
            "Ad/tracker blocking DNS, privacy focused"),
        ["adguard-family"] = new("AdGuard Family", "94.140.14.15", "94.140.15.16",
            "AdGuard + safe search + adult content blocking"),
        ["cloudflare-malware"] = new("Cloudflare Malware", "1.1.1.2", "1.0.0.2",
            "Cloudflare + malware blocking"),
        ["cloudflare-family"] = new("Cloudflare Family", "1.1.1.3", "1.0.0.3",
            "Cloudflare + malware + adult content blocking"),
        ["nextdns"] = new("NextDNS", "45.90.28.0", "45.90.30.0",
            "Customizable DNS firewall, analytics, ad blocking"),
        ["cleanbrowsing-security"] = new("CleanBrowsing Security", "185.228.168.9", "185.228.169.9",
            "Blocks malware, phishing — no adult filtering"),
        ["cleanbrowsing-family"] = new("CleanBrowsing Family", "185.228.168.168", "185.228.169.168",
            "Blocks adult content, malware, mixed content"),
        ["dns.sb"] = new("DNS.SB", "185.222.222.222", "45.11.45.11",
            "Privacy focused, no logging, DNSSEC validated"),
        ["comodo"] = new("Comodo Secure", "8.26.56.26", "8.20.247.20",
            "Blocks malware, spyware, and phishing sites"),
        ["verisign"] = new("Verisign", "64.6.64.6", "64.6.65.6",
            "Stable, no logging, privacy focused"),
        ["level3"] = new("Level3/Lumen", "4.2.2.1", "4.2.2.2",
            "Enterprise grade, ultra-low latency backbone"),
        ["controld"] = new("Control D", "76.76.2.0", "76.76.10.0",
            "Customizable DNS, ad blocking, geo-unblocking"),
        ["mullvad"] = new("Mullvad", "194.242.2.2", "193.19.108.2",
            "Privacy focused, no logging, ad/tracker blocking"),
    };

    /// <summary>
    /// Benchmark all DNS providers and return sorted by latency (fastest first).
    /// Runs benchmarks in parallel for speed. Uses direct network (bypasses tunnel).
    /// </summary>
    public static async Task<List<DnsBenchmarkResult>> BenchmarkAllAsync(string? localBindIp = null)
    {
        var testDomains = new[] { "google.com", "cloudflare.com", "microsoft.com" };

        // Run all benchmarks in parallel for speed
        var tasks = Providers.Select(async kvp =>
        {
            var latency = await BenchmarkDnsAsync(kvp.Value.Primary, testDomains, localBindIp);
            return new DnsBenchmarkResult(kvp.Key, kvp.Value.Name, kvp.Value.Primary, kvp.Value.Secondary, latency);
        });

        var results = await Task.WhenAll(tasks);
        return results.OrderBy(r => r.AvgLatencyMs).ToList();
    }

    /// <summary>
    /// Benchmark a single DNS server by resolving multiple domains and returning average latency.
    /// Optionally binds to a specific local IP to bypass tunnel.
    /// Uses CancellationToken for proper timeout (ReceiveAsync ignores socket timeout).
    /// </summary>
    public static async Task<double> BenchmarkDnsAsync(string dnsServer, string[]? domains = null, string? localBindIp = null, int timeoutMs = 3000)
    {
        domains ??= new[] { "google.com", "cloudflare.com", "github.com" };
        var latencies = new List<double>();

        foreach (var domain in domains)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                var sw = Stopwatch.StartNew();
                using var udp = new UdpClient();

                // Bind to physical adapter to bypass tunnel routing
                if (!string.IsNullOrEmpty(localBindIp) && IPAddress.TryParse(localBindIp, out var bindAddr))
                    udp.Client.Bind(new IPEndPoint(bindAddr, 0));

                var endpoint = new IPEndPoint(IPAddress.Parse(dnsServer), 53);
                var query = BuildDnsQuery(domain);
                await udp.SendAsync(query, query.Length, endpoint);

                // ReceiveAsync with proper timeout via CancellationToken
                var receiveTask = udp.ReceiveAsync();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs, cts.Token));

                if (completed == receiveTask && receiveTask.IsCompletedSuccessfully)
                {
                    sw.Stop();
                    var result = receiveTask.Result;
                    if (result.Buffer.Length > 0)
                        latencies.Add(sw.Elapsed.TotalMilliseconds);
                    else
                        latencies.Add(9999);
                }
                else
                {
                    latencies.Add(9999); // Timeout
                }
            }
            catch
            {
                latencies.Add(9999);
            }
        }

        // Only average successful results; if all failed return 9999
        var good = latencies.Where(l => l < 9999).ToList();
        return good.Count > 0 ? good.Average() : 9999;
    }

    /// <summary>
    /// Auto-select the best DNS provider by benchmarking all and returning the fastest.
    /// Benchmarks bypass the tunnel to measure real network latency.
    /// </summary>
    public static async Task<(string ProviderId, string Primary, string Secondary)> AutoSelectAsync(string? localBindIp = null)
    {
        Logger.Info("DNS auto-selection: benchmarking all providers...");
        var results = await BenchmarkAllAsync(localBindIp);

        foreach (var r in results.Take(5))
            Logger.Info($"  DNS benchmark: {r.Name} ({r.Primary}) = {r.AvgLatencyMs:F1}ms");

        var best = results.First();
        Logger.Info($"DNS auto-selected: {best.Name} ({best.Primary}/{best.Secondary}) at {best.AvgLatencyMs:F1}ms");
        return (best.ProviderId, best.Primary, best.Secondary);
    }

    /// <summary>
    /// Resolve the DNS servers to use based on settings.
    /// </summary>
    public static (string Primary, string Secondary) Resolve(AppSettings settings)
    {
        if (settings.DnsProvider == "custom" &&
            !string.IsNullOrWhiteSpace(settings.CustomDnsPrimary))
        {
            return (settings.CustomDnsPrimary,
                    string.IsNullOrWhiteSpace(settings.CustomDnsSecondary) ? "1.1.1.1" : settings.CustomDnsSecondary);
        }

        if (settings.DnsProvider != "auto" && Providers.TryGetValue(settings.DnsProvider, out var preset))
            return (preset.Primary, preset.Secondary);

        // Default to Cloudflare (fastest globally)
        return ("1.1.1.1", "1.0.0.1");
    }

    /// <summary>
    /// Force DNS on a network adapter (both primary and secondary).
    /// </summary>
    public static void ForceAdapterDns(string adapterName, string primary, string secondary)
    {
        try
        {
            PaqetService.RunCommand("netsh",
                $"interface ip set dns \"{adapterName}\" static {primary}", timeout: 5000);
            PaqetService.RunCommand("netsh",
                $"interface ip add dns \"{adapterName}\" {secondary} index=2", timeout: 5000);
            Logger.Info($"DNS forced on {adapterName}: {primary}, {secondary}");
        }
        catch (Exception ex)
        {
            Logger.Debug($"ForceAdapterDns({adapterName}): {ex.Message}");
        }
    }

    /// <summary>
    /// Set DNS on ALL active adapters to prevent leaks.
    /// Returns list of adapters that were changed (for restore).
    /// </summary>
    public static List<(string Name, string? OriginalDns)> ForceAllAdaptersDns(
        string primary, string secondary, string? excludeAdapter = null)
    {
        var changed = new List<(string Name, string? OriginalDns)>();
        try
        {
            var output = PaqetService.RunCommand("powershell", "-NoProfile -Command \"Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Select-Object -ExpandProperty Name\"");
            foreach (var adapter in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = adapter.Trim();
                if (string.IsNullOrEmpty(name) || name == excludeAdapter) continue;

                try
                {
                    var originalDns = GetAdapterDns(name);
                    ForceAdapterDns(name, primary, secondary);
                    changed.Add((name, originalDns));
                }
                catch { }
            }
        }
        catch (Exception ex) { Logger.Debug($"ForceAllAdaptersDns: {ex.Message}"); }
        return changed;
    }

    /// <summary>Get the current DNS server for an adapter.</summary>
    public static string? GetAdapterDns(string adapterName)
    {
        try
        {
            var output = PaqetService.RunCommand("netsh",
                $"interface ip show dns \"{adapterName}\"", timeout: 5000);
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("DNS Servers") || line.Contains("Statically"))
                {
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        var ip = parts[^1].Trim();
                        if (IPAddress.TryParse(ip, out _)) return ip;
                    }
                }
                // Also check next line for IP after "Statically Configured" header
                var trimmed = line.Trim();
                if (IPAddress.TryParse(trimmed, out _)) return trimmed;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Restore DNS on an adapter to its original setting.</summary>
    public static void RestoreAdapterDns(string adapterName, string? originalDns)
    {
        try
        {
            if (!string.IsNullOrEmpty(originalDns))
            {
                PaqetService.RunCommand("netsh",
                    $"interface ip set dns \"{adapterName}\" static {originalDns}", timeout: 5000);
                Logger.Info($"Restored DNS on {adapterName} to {originalDns}");
            }
            else
            {
                PaqetService.RunCommand("netsh",
                    $"interface ip set dns \"{adapterName}\" dhcp", timeout: 5000);
                Logger.Info($"Restored DNS on {adapterName} to DHCP");
            }
        }
        catch (Exception ex) { Logger.Debug($"RestoreAdapterDns({adapterName}): {ex.Message}"); }
    }

    /// <summary>Flush DNS cache.</summary>
    public static void FlushCache()
    {
        try
        {
            PaqetService.RunCommand("ipconfig", "/flushdns", timeout: 5000);
            Logger.Debug("DNS cache flushed");
        }
        catch (Exception ex) { Logger.Debug($"FlushDns: {ex.Message}"); }
    }

    /// <summary>Build a minimal DNS query packet for A record lookup.</summary>
    private static byte[] BuildDnsQuery(string domain)
    {
        var rng = new Random();
        var id = (ushort)rng.Next(0, 65535);

        var parts = domain.Split('.');
        var queryLen = 12 + parts.Sum(p => p.Length + 1) + 1 + 4; // header + labels + null + type+class
        var packet = new byte[queryLen];

        // Header: ID, flags (recursion desired), 1 question
        packet[0] = (byte)(id >> 8); packet[1] = (byte)id;
        packet[2] = 0x01; packet[3] = 0x00; // RD=1
        packet[4] = 0x00; packet[5] = 0x01; // QDCOUNT=1

        // Question: domain name labels
        var offset = 12;
        foreach (var part in parts)
        {
            packet[offset++] = (byte)part.Length;
            foreach (var c in part) packet[offset++] = (byte)c;
        }
        packet[offset++] = 0x00; // null terminator

        // Type A (1), Class IN (1)
        packet[offset++] = 0x00; packet[offset++] = 0x01;
        packet[offset++] = 0x00; packet[offset] = 0x01;

        return packet;
    }
}

public record DnsPreset(string Name, string Primary, string Secondary, string Description);

public record DnsBenchmarkResult(
    string ProviderId, string Name, string Primary, string Secondary, double AvgLatencyMs);
