using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaqetTunnel.Models;

/// <summary>
/// Structured diagnostic report for performance analysis and historical comparison.
/// Stored as JSON in %LOCALAPPDATA%\PaqetTunnel\diagnostics\.
/// </summary>
public sealed class DiagnosticReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double DurationMs { get; set; }
    public string PaqetVersion { get; set; } = "";
    public string ServerAddr { get; set; } = "";
    public string PublicIp { get; set; } = "";
    public string KcpMode { get; set; } = "fast";

    public LatencyResult? ServerLatency { get; set; }
    public LatencyResult? ProxyLatency { get; set; }
    public SpeedResult? DownloadSpeed { get; set; }
    public PingResult? PaqetPing { get; set; }
    public SystemInfoSnapshot? SystemInfo { get; set; }
    public string? Error { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Save()
    {
        try
        {
            var dir = AppPaths.DiagnosticsDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"diag_{Timestamp:yyyyMMdd_HHmmss}_{Id}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
            Services.Logger.Info($"Diagnostic report saved: {path}");
        }
        catch (Exception ex)
        {
            Services.Logger.Error("Failed to save diagnostic report", ex);
        }
    }

    public static List<DiagnosticReport> LoadAll(int limit = 50)
    {
        var dir = AppPaths.DiagnosticsDir;
        if (!Directory.Exists(dir)) return new();

        return Directory.GetFiles(dir, "diag_*.json")
            .OrderByDescending(f => f)
            .Take(limit)
            .Select(f =>
            {
                try { return JsonSerializer.Deserialize<DiagnosticReport>(File.ReadAllText(f), JsonOpts); }
                catch { return null; }
            })
            .Where(r => r != null)
            .Cast<DiagnosticReport>()
            .ToList();
    }

    public static DiagnosticReport? LoadLatest()
    {
        var dir = AppPaths.DiagnosticsDir;
        if (!Directory.Exists(dir)) return null;

        var latest = Directory.GetFiles(dir, "diag_*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (latest == null) return null;

        try { return JsonSerializer.Deserialize<DiagnosticReport>(File.ReadAllText(latest), JsonOpts); }
        catch { return null; }
    }

    /// <summary>Compare this report against a baseline, returning deltas.</summary>
    public ComparisonResult CompareTo(DiagnosticReport baseline)
    {
        var result = new ComparisonResult { BaselineTimestamp = baseline.Timestamp, CurrentTimestamp = Timestamp };

        if (ServerLatency != null && baseline.ServerLatency != null)
        {
            result.ServerLatencyDeltaMs = ServerLatency.AvgMs - baseline.ServerLatency.AvgMs;
            result.ServerLatencyPctChange = baseline.ServerLatency.AvgMs > 0
                ? (ServerLatency.AvgMs - baseline.ServerLatency.AvgMs) / baseline.ServerLatency.AvgMs * 100 : 0;
        }
        if (ProxyLatency != null && baseline.ProxyLatency != null)
        {
            result.ProxyLatencyDeltaMs = ProxyLatency.AvgMs - baseline.ProxyLatency.AvgMs;
            result.ProxyLatencyPctChange = baseline.ProxyLatency.AvgMs > 0
                ? (ProxyLatency.AvgMs - baseline.ProxyLatency.AvgMs) / baseline.ProxyLatency.AvgMs * 100 : 0;
        }
        if (DownloadSpeed != null && baseline.DownloadSpeed != null)
        {
            result.SpeedDeltaBps = DownloadSpeed.BytesPerSecond - baseline.DownloadSpeed.BytesPerSecond;
            result.SpeedPctChange = baseline.DownloadSpeed.BytesPerSecond > 0
                ? (DownloadSpeed.BytesPerSecond - baseline.DownloadSpeed.BytesPerSecond) / baseline.DownloadSpeed.BytesPerSecond * 100 : 0;
        }
        return result;
    }

    /// <summary>Clean old diagnostic files, keeping the most recent N.</summary>
    public static void CleanOld(int keep = 100)
    {
        try
        {
            var dir = AppPaths.DiagnosticsDir;
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "diag_*.json").OrderByDescending(f => f).ToArray();
            for (int i = keep; i < files.Length; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }
        catch { }
    }
}

public sealed class LatencyResult
{
    public int Samples { get; set; }
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public double AvgMs { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double JitterMs { get; set; }
    public int FailedCount { get; set; }
    public List<double> RawMs { get; set; } = new();
}

public sealed class SpeedResult
{
    public long TotalBytes { get; set; }
    public double DurationMs { get; set; }
    public double BytesPerSecond { get; set; }
    public string Formatted { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class PingResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public double DurationMs { get; set; }
}

public sealed class SystemInfoSnapshot
{
    public string OsVersion { get; set; } = "";
    public string Interface { get; set; } = "";
    public string LocalIp { get; set; } = "";
    public string Gateway { get; set; } = "";
    public bool TunActive { get; set; }
    public long PaqetMemoryMb { get; set; }
    public long Tun2SocksMemoryMb { get; set; }
    public TimeSpan? PaqetUptime { get; set; }
    public string DnsProvider { get; set; } = "";
}

public sealed class ComparisonResult
{
    public DateTime BaselineTimestamp { get; set; }
    public DateTime CurrentTimestamp { get; set; }
    public double ServerLatencyDeltaMs { get; set; }
    public double ServerLatencyPctChange { get; set; }
    public double ProxyLatencyDeltaMs { get; set; }
    public double ProxyLatencyPctChange { get; set; }
    public double SpeedDeltaBps { get; set; }
    public double SpeedPctChange { get; set; }

    public string Summary()
    {
        var parts = new List<string>();
        if (ServerLatencyDeltaMs != 0)
            parts.Add($"Server latency: {ServerLatencyDeltaMs:+0.0;-0.0}ms ({ServerLatencyPctChange:+0.0;-0.0}%)");
        if (ProxyLatencyDeltaMs != 0)
            parts.Add($"Proxy latency: {ProxyLatencyDeltaMs:+0.0;-0.0}ms ({ProxyLatencyPctChange:+0.0;-0.0}%)");
        if (SpeedDeltaBps != 0)
            parts.Add($"Speed: {SpeedDeltaBps / 1024:+0.0;-0.0} KB/s ({SpeedPctChange:+0.0;-0.0}%)");
        return parts.Count > 0 ? string.Join(" | ", parts) : "No significant changes";
    }
}
