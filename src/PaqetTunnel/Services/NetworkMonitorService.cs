using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Timers;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

/// <summary>
/// Monitors network speed using lightweight netstat -e calls.
/// Maintains a rolling history for graph rendering.
/// </summary>
public sealed class NetworkMonitorService : IDisposable
{
    private const int HISTORY_SIZE = 60;
    private readonly Timer _timer;
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastSampleTime;
    private readonly object _lock = new();

    public List<SpeedSnapshot> History { get; } = new(HISTORY_SIZE);
    public SpeedSnapshot Latest { get; private set; } = new(0, 0, 0, 0, DateTime.Now);

    public event Action? SpeedUpdated;

    public NetworkMonitorService()
    {
        _timer = new Timer(2000); // 2-second interval
        _timer.Elapsed += OnTick;
        _timer.AutoReset = true;
    }

    public void Start()
    {
        // Take initial sample
        SampleNetstat(out _lastBytesReceived, out _lastBytesSent);
        _lastSampleTime = DateTime.Now;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        try
        {
            if (!SampleNetstat(out var recv, out var sent)) return;

            var now = DateTime.Now;
            var elapsed = (now - _lastSampleTime).TotalSeconds;
            if (elapsed <= 0) return;

            var downloadSpeed = Math.Max(0, (recv - _lastBytesReceived) / elapsed);
            var uploadSpeed = Math.Max(0, (sent - _lastBytesSent) / elapsed);

            var snapshot = new SpeedSnapshot(recv, sent, downloadSpeed, uploadSpeed, now);

            lock (_lock)
            {
                Latest = snapshot;
                History.Add(snapshot);
                if (History.Count > HISTORY_SIZE)
                    History.RemoveAt(0);
            }

            _lastBytesReceived = recv;
            _lastBytesSent = sent;
            _lastSampleTime = now;

            SpeedUpdated?.Invoke();
        }
        catch { /* Swallow monitoring errors */ }
    }

    /// <summary>Parse netstat -e output for total bytes.</summary>
    private static bool SampleNetstat(out long received, out long sent)
    {
        received = 0;
        sent = 0;
        try
        {
            var psi = new ProcessStartInfo("netstat", "-e")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            var match = Regex.Match(output, @"Bytes\s+(\d+)\s+(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            received = long.Parse(match.Groups[1].Value);
            sent = long.Parse(match.Groups[2].Value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Format bytes/sec into human-readable string.</summary>
    public static string FormatSpeed(double bytesPerSec)
    {
        return bytesPerSec switch
        {
            < 1024 => $"{bytesPerSec:F0} B/s",
            < 1024 * 1024 => $"{bytesPerSec / 1024:F1} KB/s",
            < 1024 * 1024 * 1024 => $"{bytesPerSec / (1024 * 1024):F1} MB/s",
            _ => $"{bytesPerSec / (1024 * 1024 * 1024):F2} GB/s"
        };
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
