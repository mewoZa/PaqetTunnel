using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

/// <summary>
/// Performance diagnostic and benchmarking engine.
/// Measures latency, throughput, and stability of the paqet tunnel.
/// Stores results as JSON for historical comparison.
/// </summary>
public sealed class DiagnosticService
{
    private static readonly string[] SpeedTestUrls = new[]
    {
        "http://speed.cloudflare.com/__down?bytes=1048576",    // 1MB Cloudflare
        "http://speedtest.tele2.net/1MB.zip",                  // 1MB Tele2
        "http://proof.ovh.net/files/1Mb.dat",                  // 1MB OVH
    };

    private readonly PaqetService _paqetService;

    public DiagnosticService(PaqetService paqetService)
    {
        _paqetService = paqetService;
    }

    /// <summary>
    /// Measure TCP connect latency to the paqet server (bypasses tunnel — direct connection).
    /// </summary>
    public async Task<LatencyResult> MeasureServerLatencyAsync(string serverHost, int serverPort, int samples = 10, CancellationToken ct = default)
    {
        Logger.Perf("latency", $"Starting server latency test to {serverHost}:{serverPort} ({samples} samples)");
        var times = new List<double>();
        int failed = 0;

        for (int i = 0; i < samples && !ct.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(serverHost, serverPort);
                if (await Task.WhenAny(connectTask, Task.Delay(5000, ct)) == connectTask)
                {
                    await connectTask; // Propagate exceptions
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    Logger.Debug($"Server latency sample {i + 1}: {sw.Elapsed.TotalMilliseconds:F1}ms");
                }
                else
                {
                    failed++;
                    Logger.Debug($"Server latency sample {i + 1}: timeout");
                }
            }
            catch
            {
                failed++;
            }

            if (i < samples - 1) await Task.Delay(200, ct);
        }

        var result = BuildLatencyResult(times, failed, samples);
        Logger.Perf("latency", "Server latency complete", new()
        {
            ["avg_ms"] = result.AvgMs.ToString("F1"),
            ["min_ms"] = result.MinMs.ToString("F1"),
            ["max_ms"] = result.MaxMs.ToString("F1"),
            ["p95_ms"] = result.P95Ms.ToString("F1"),
            ["jitter_ms"] = result.JitterMs.ToString("F1"),
            ["failed"] = result.FailedCount,
            ["samples"] = result.Samples
        });
        return result;
    }

    /// <summary>
    /// Measure HTTP request latency through the SOCKS5 proxy (full tunnel round-trip).
    /// </summary>
    public async Task<LatencyResult> MeasureProxyLatencyAsync(int samples = 10, CancellationToken ct = default)
    {
        Logger.Perf("latency", $"Starting proxy latency test ({samples} samples)");
        var times = new List<double>();
        int failed = 0;
        var proxy = new WebProxy($"socks5://127.0.0.1:{PaqetService.SOCKS_PORT}");

        // BUG-21 fix: reuse HttpClient/handler across samples to prevent socket exhaustion
        using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetTunnel-Diag/1.0");

        for (int i = 0; i < samples && !ct.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Head, "http://httpbin.org/status/200");
                var resp = await http.SendAsync(req, ct);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                Logger.Debug($"Proxy latency sample {i + 1}: {sw.Elapsed.TotalMilliseconds:F1}ms (HTTP {(int)resp.StatusCode})");
            }
            catch
            {
                failed++;
            }

            if (i < samples - 1) await Task.Delay(300, ct);
        }

        var result = BuildLatencyResult(times, failed, samples);
        Logger.Perf("latency", "Proxy latency complete", new()
        {
            ["avg_ms"] = result.AvgMs.ToString("F1"),
            ["min_ms"] = result.MinMs.ToString("F1"),
            ["p95_ms"] = result.P95Ms.ToString("F1"),
            ["jitter_ms"] = result.JitterMs.ToString("F1"),
            ["failed"] = result.FailedCount
        });
        return result;
    }

    /// <summary>
    /// Measure download speed through the SOCKS5 proxy.
    /// </summary>
    public async Task<SpeedResult> MeasureDownloadSpeedAsync(CancellationToken ct = default)
    {
        Logger.Perf("speed", "Starting download speed test");
        var proxy = new WebProxy($"socks5://127.0.0.1:{PaqetService.SOCKS_PORT}");

        foreach (var url in SpeedTestUrls)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetTunnel-Diag/1.0");

                var sw = Stopwatch.StartNew();
                var data = await http.GetByteArrayAsync(url, ct);
                sw.Stop();

                var bps = data.Length / sw.Elapsed.TotalSeconds;
                var result = new SpeedResult
                {
                    TotalBytes = data.Length,
                    DurationMs = sw.Elapsed.TotalMilliseconds,
                    BytesPerSecond = bps,
                    Formatted = NetworkMonitorService.FormatSpeed(bps),
                    Url = url
                };
                Logger.Perf("speed", "Download speed test complete", new()
                {
                    ["bytes"] = result.TotalBytes,
                    ["duration_ms"] = result.DurationMs.ToString("F0"),
                    ["speed"] = result.Formatted,
                    ["url"] = url
                });
                return result;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Speed test failed for {url}: {ex.Message}");
            }
        }

        Logger.Warn("All speed test URLs failed");
        return new SpeedResult { Formatted = "Failed" };
    }

    /// <summary>
    /// Run paqet's built-in ping command to test tunnel connectivity.
    /// </summary>
    public async Task<PingResult> RunPaqetPingAsync(CancellationToken ct = default)
    {
        Logger.Perf("ping", "Running paqet ping");
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var (success, output) = _paqetService.Ping();
            sw.Stop();
            var result = new PingResult
            {
                Success = success,
                Output = output,
                DurationMs = sw.Elapsed.TotalMilliseconds
            };
            Logger.Perf("ping", $"Paqet ping {(success ? "OK" : "FAIL")}", new()
            {
                ["duration_ms"] = result.DurationMs.ToString("F0"),
                ["output"] = output.Length > 100 ? output[..100] : output
            });
            return result;
        }, ct);
    }

    /// <summary>
    /// Collect system information snapshot.
    /// </summary>
    public SystemInfoSnapshot CollectSystemInfo()
    {
        Logger.Perf("system", "Collecting system info");
        var info = new SystemInfoSnapshot
        {
            OsVersion = $"{Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})"
        };

        // Get process info
        try
        {
            var procs = Process.GetProcessesByName("paqet_windows_amd64");
            try
            {
                var paqetProc = procs.FirstOrDefault();
                if (paqetProc != null)
                {
                    info.PaqetMemoryMb = paqetProc.WorkingSet64 / (1024 * 1024);
                    info.PaqetUptime = DateTime.Now - paqetProc.StartTime;
                }
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch { }

        try
        {
            var procs = Process.GetProcessesByName("tun2socks");
            try
            {
                var t2sProc = procs.FirstOrDefault();
                if (t2sProc != null) info.Tun2SocksMemoryMb = t2sProc.WorkingSet64 / (1024 * 1024);
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch { }

        // Check TUN adapter
        try
        {
            var output = PaqetService.RunCommand("netsh", "interface show interface name=\"PaqetTun\"", 3000);
            info.TunActive = output.Contains("Connected", StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        // Get network config from paqet config
        try
        {
            if (File.Exists(AppPaths.PaqetConfigPath))
            {
                var config = PaqetConfig.FromYaml(File.ReadAllText(AppPaths.PaqetConfigPath));
                info.Interface = config.Interface;
                info.LocalIp = ParseHost(config.Ipv4Addr);
            }
        }
        catch { }

        // Get default gateway
        try
        {
            var routeOutput = PaqetService.RunCommand("route", "print 0.0.0.0", 3000);
            foreach (var line in routeOutput.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("0.0.0.0")) continue;
                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[0] == "0.0.0.0" && parts[1] == "0.0.0.0")
                {
                    info.Gateway = parts[2];
                    break;
                }
            }
        }
        catch { }

        // Get DNS provider from settings
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(AppPaths.SettingsPath));
                info.DnsProvider = settings?.DnsProvider ?? "auto";
            }
        }
        catch { }

        Logger.Perf("system", "System info collected", new()
        {
            ["os"] = info.OsVersion,
            ["paqet_mem_mb"] = info.PaqetMemoryMb,
            ["tun_active"] = info.TunActive,
            ["uptime"] = info.PaqetUptime?.ToString(@"d\.hh\:mm\:ss") ?? "N/A"
        });
        return info;
    }

    /// <summary>
    /// Run the full diagnostic suite: latency, speed, ping, system info.
    /// Returns a complete report saved to disk.
    /// </summary>
    public async Task<DiagnosticReport> RunFullDiagnosticAsync(CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        Logger.Info("=== Full Diagnostic Suite Starting ===");

        var report = new DiagnosticReport();

        // Get config info
        try
        {
            if (File.Exists(AppPaths.PaqetConfigPath))
            {
                var config = PaqetConfig.FromYaml(File.ReadAllText(AppPaths.PaqetConfigPath));
                report.ServerAddr = config.ServerAddr;
                report.KcpMode = "fast"; // Read from config if we add mode field
            }
        }
        catch { }

        // Paqet version
        try
        {
            report.PaqetVersion = _paqetService.GetVersion() ?? "unknown";
        }
        catch { }

        // 1. System info (fast, sync)
        try { report.SystemInfo = CollectSystemInfo(); } catch (Exception ex) { Logger.Error("Diag: system info failed", ex); }

        // 2. Paqet ping
        try { report.PaqetPing = await RunPaqetPingAsync(ct); } catch (Exception ex) { Logger.Error("Diag: paqet ping failed", ex); }

        // 3. Server TCP latency (direct to server, not through proxy)
        if (!string.IsNullOrEmpty(report.ServerAddr))
        {
            try
            {
                var host = ParseHost(report.ServerAddr);
                var portStr = ParsePort(report.ServerAddr);
                if (int.TryParse(portStr, out var port))
                    report.ServerLatency = await MeasureServerLatencyAsync(host, port, 10, ct);
            }
            catch (Exception ex) { Logger.Error("Diag: server latency failed", ex); }
        }

        // 4. Proxy latency (through tunnel)
        try { report.ProxyLatency = await MeasureProxyLatencyAsync(10, ct); } catch (Exception ex) { Logger.Error("Diag: proxy latency failed", ex); }

        // 5. Public IP (through tunnel)
        try { report.PublicIp = await PaqetService.CheckTunnelConnectivityAsync(8000) ?? ""; } catch { }

        // 6. Download speed (through tunnel)
        try { report.DownloadSpeed = await MeasureDownloadSpeedAsync(ct); } catch (Exception ex) { Logger.Error("Diag: speed test failed", ex); }

        totalSw.Stop();
        report.DurationMs = totalSw.Elapsed.TotalMilliseconds;

        // Save report
        report.Save();
        DiagnosticReport.CleanOld();

        // Compare with previous
        var previous = DiagnosticReport.LoadAll(2);
        if (previous.Count >= 2)
        {
            var baseline = previous[1]; // Second most recent (before this one)
            var comparison = report.CompareTo(baseline);
            Logger.Perf("comparison", comparison.Summary());
        }

        Logger.Info($"=== Full Diagnostic Suite Complete ({totalSw.Elapsed.TotalSeconds:F1}s) ===");
        return report;
    }

    /// <summary>
    /// Quick connectivity check — just latency and IP, no speed test.
    /// </summary>
    public async Task<DiagnosticReport> RunQuickCheckAsync(CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        Logger.Info("=== Quick Diagnostic Check ===");

        var report = new DiagnosticReport();

        try
        {
            if (File.Exists(AppPaths.PaqetConfigPath))
            {
                var config = PaqetConfig.FromYaml(File.ReadAllText(AppPaths.PaqetConfigPath));
                report.ServerAddr = config.ServerAddr;
            }
        }
        catch { }

        try { report.PaqetVersion = _paqetService.GetVersion() ?? "unknown"; } catch { }
        try { report.SystemInfo = CollectSystemInfo(); } catch { }
        try { report.ProxyLatency = await MeasureProxyLatencyAsync(5, ct); } catch { }
        try { report.PublicIp = await PaqetService.CheckTunnelConnectivityAsync(5000) ?? ""; } catch { }

        totalSw.Stop();
        report.DurationMs = totalSw.Elapsed.TotalMilliseconds;
        report.Save();

        Logger.Info($"=== Quick Check Complete ({totalSw.Elapsed.TotalSeconds:F1}s) ===");
        return report;
    }

    /// <summary>Format a diagnostic report as a human-readable string.</summary>
    public static string FormatReport(DiagnosticReport report)
    {
        var lines = new List<string>
        {
            $"═══ Diagnostic Report ═══",
            $"Time:    {report.Timestamp:yyyy-MM-dd HH:mm:ss}",
            $"Paqet:   {report.PaqetVersion}",
            $"Server:  {report.ServerAddr}",
            $"IP:      {report.PublicIp}",
            $"Duration: {report.DurationMs / 1000:F1}s",
            ""
        };

        if (report.SystemInfo != null)
        {
            var si = report.SystemInfo;
            lines.Add("── System ──");
            lines.Add($"  OS:        {si.OsVersion}");
            lines.Add($"  Interface: {si.Interface}");
            lines.Add($"  TUN:       {(si.TunActive ? "Active" : "Inactive")}");
            lines.Add($"  Paqet Mem: {si.PaqetMemoryMb} MB");
            if (si.PaqetUptime.HasValue)
                lines.Add($"  Uptime:    {si.PaqetUptime.Value:d\\.hh\\:mm\\:ss}");
            lines.Add($"  DNS:       {si.DnsProvider}");
            lines.Add("");
        }

        if (report.PaqetPing != null)
        {
            lines.Add("── Ping ──");
            lines.Add($"  Status:  {(report.PaqetPing.Success ? "OK" : "FAIL")}");
            lines.Add($"  Time:    {report.PaqetPing.DurationMs:F0}ms");
            if (!string.IsNullOrWhiteSpace(report.PaqetPing.Output))
                lines.Add($"  Output:  {report.PaqetPing.Output.Trim().Split('\n')[0]}");
            lines.Add("");
        }

        if (report.ServerLatency != null)
        {
            var sl = report.ServerLatency;
            lines.Add("── Server Latency (direct TCP) ──");
            lines.Add($"  Avg:     {sl.AvgMs:F1}ms");
            lines.Add($"  Min/Max: {sl.MinMs:F1}/{sl.MaxMs:F1}ms");
            lines.Add($"  P50/P95: {sl.P50Ms:F1}/{sl.P95Ms:F1}ms");
            lines.Add($"  Jitter:  {sl.JitterMs:F1}ms");
            lines.Add($"  Failed:  {sl.FailedCount}/{sl.Samples}");
            lines.Add("");
        }

        if (report.ProxyLatency != null)
        {
            var pl = report.ProxyLatency;
            lines.Add("── Proxy Latency (through tunnel) ──");
            lines.Add($"  Avg:     {pl.AvgMs:F1}ms");
            lines.Add($"  Min/Max: {pl.MinMs:F1}/{pl.MaxMs:F1}ms");
            lines.Add($"  P50/P95: {pl.P50Ms:F1}/{pl.P95Ms:F1}ms");
            lines.Add($"  Jitter:  {pl.JitterMs:F1}ms");
            lines.Add($"  Failed:  {pl.FailedCount}/{pl.Samples}");
            lines.Add("");
        }

        if (report.DownloadSpeed != null && report.DownloadSpeed.BytesPerSecond > 0)
        {
            var ds = report.DownloadSpeed;
            lines.Add("── Download Speed ──");
            lines.Add($"  Speed:   {ds.Formatted}");
            lines.Add($"  Size:    {NetworkMonitorService.FormatBytes(ds.TotalBytes)}");
            lines.Add($"  Time:    {ds.DurationMs / 1000:F1}s");
            lines.Add("");
        }

        if (!string.IsNullOrEmpty(report.Error))
        {
            lines.Add($"── Error: {report.Error} ──");
            lines.Add("");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Format a comparison summary between two reports.</summary>
    public static string FormatComparison(DiagnosticReport current, DiagnosticReport baseline)
    {
        var comp = current.CompareTo(baseline);
        var lines = new List<string>
        {
            $"═══ Comparison ═══",
            $"Baseline: {baseline.Timestamp:yyyy-MM-dd HH:mm:ss}",
            $"Current:  {current.Timestamp:yyyy-MM-dd HH:mm:ss}",
            "",
            comp.Summary()
        };
        return string.Join("\n", lines);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static LatencyResult BuildLatencyResult(List<double> times, int failed, int totalSamples)
    {
        if (times.Count == 0)
            return new LatencyResult { Samples = totalSamples, FailedCount = failed };

        var sorted = times.OrderBy(t => t).ToList();
        var avg = sorted.Average();

        // Calculate jitter as average absolute deviation from mean
        var jitter = sorted.Average(t => Math.Abs(t - avg));

        return new LatencyResult
        {
            Samples = totalSamples,
            MinMs = sorted[0],
            MaxMs = sorted[^1],
            AvgMs = avg,
            P50Ms = Percentile(sorted, 50),
            P95Ms = Percentile(sorted, 95),
            JitterMs = jitter,
            FailedCount = failed,
            RawMs = times
        };
    }

    private static double Percentile(List<double> sorted, int percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    // R3-13 fix: IPv6-safe host/port parsing (handles "[::1]:8443" and "1.2.3.4:8443")
    private static string ParseHost(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return addr;
        if (addr.StartsWith('['))
        {
            var endBracket = addr.IndexOf(']');
            return endBracket > 0 ? addr[1..endBracket] : addr;
        }
        var lastColon = addr.LastIndexOf(':');
        return lastColon > 0 ? addr[..lastColon] : addr;
    }

    private static string ParsePort(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return "8443";
        if (addr.StartsWith('['))
        {
            var afterBracket = addr.IndexOf("]:");
            return afterBracket >= 0 ? addr[(afterBracket + 2)..] : "8443";
        }
        var lastColon = addr.LastIndexOf(':');
        return lastColon > 0 ? addr[(lastColon + 1)..] : "8443";
    }
}
