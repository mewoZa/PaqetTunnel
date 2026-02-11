using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
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

    /// <summary>BUG-07 fix: return a thread-safe copy of History under lock.</summary>
    public List<SpeedSnapshot> GetHistorySnapshot()
    {
        lock (_lock) { return new List<SpeedSnapshot>(History); }
    }

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

    private volatile bool _disposed; // R3-14 fix: prevent in-flight tick after Dispose

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (_disposed) return; // R3-14 fix
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

    /// <summary>BUG-24 fix: use .NET NetworkInterface API instead of spawning netstat.</summary>
    private static bool SampleNetstat(out long received, out long sent)
    {
        received = 0;
        sent = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.Name.Contains("PaqetTun", StringComparison.OrdinalIgnoreCase)) continue;
                var stats = ni.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            return received > 0 || sent > 0;
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

    public static string FormatBytes(double bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes:F0} B",
            < 1024 * 1024 => $"{bytes / 1024:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024 * 1024):F1} MB",
            _ => $"{bytes / (1024 * 1024 * 1024):F2} GB"
        };
    }

    public void Dispose()
    {
        _disposed = true; // R3-14 fix: signal OnTick to bail out
        _timer.Elapsed -= OnTick;
        _timer.Stop();
        _timer.Dispose();
    }
}
