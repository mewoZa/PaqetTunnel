using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PaqetTunnel.Services;

/// <summary>
/// Simple file-based logger. Writes to %LOCALAPPDATA%\PaqetTunnel\logs\.
/// Enable via AppSettings.DebugMode or by calling Initialize(true).
/// </summary>
public static class Logger
{
    private static bool _initialized;
    private static bool _debugMode;
    private static string _logPath = "";
    private static readonly object _lock = new();
    private static readonly List<LogEntry> _buffer = new();
    private const int MaxBufferSize = 500;

    public static bool IsEnabled => _initialized;
    public static bool DebugEnabled => _debugMode;
    public static string LogPath => _logPath;
    public static string LogDir => Path.Combine(AppPaths.DataDir, "logs");

    /// <summary>Fired when a new log entry is written. UI can subscribe to display live logs.</summary>
    public static event Action<LogEntry>? LogAdded;

    /// <summary>
    /// Initialize logger. Always logs INFO/WARN/ERROR. Debug messages only when debugMode=true.
    /// </summary>
    public static void Initialize(bool debugMode)
    {
        _debugMode = debugMode;

        if (_initialized) return; // Already initialized, just update debug flag

        try
        {
            Directory.CreateDirectory(LogDir);
            _logPath = Path.Combine(LogDir, $"paqettunnel_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _initialized = true;
            Write("INFO", $"=== PaqetTunnel Log Started (debug={debugMode}) ===");
            Write("INFO", $"Log file: {_logPath}");
            Write("INFO", $"DataDir: {AppPaths.DataDir}");
            Write("INFO", $"BinaryPath: {AppPaths.BinaryPath}");
            Write("INFO", $"ConfigPath: {AppPaths.PaqetConfigPath}");
            Write("INFO", $"Binary exists: {File.Exists(AppPaths.BinaryPath)}");
            Write("INFO", $"Config exists: {File.Exists(AppPaths.PaqetConfigPath)}");
        }
        catch (Exception ex)
        {
            _initialized = false;
            System.Diagnostics.Debug.WriteLine($"Logger init failed: {ex.Message}");
        }
    }

    /// <summary>Update debug mode at runtime without reinitializing.</summary>
    public static void SetDebugMode(bool enabled) => _debugMode = enabled;

    public static void Debug(string message) { if (_debugMode) Write("DEBUG", message); }
    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        Write("ERROR", msg);
    }

    /// <summary>Log structured performance data for benchmarking and analysis.</summary>
    public static void Perf(string category, string message, Dictionary<string, object>? data = null)
    {
        var dataStr = "";
        if (data != null && data.Count > 0)
            dataStr = " | " + string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}"));
        Write("PERF", $"[{category}] {message}{dataStr}");
    }

    private static void Write(string level, string message)
    {
        if (!_initialized || string.IsNullOrEmpty(_logPath)) return;
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Level = level,
            Message = message,
            Formatted = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}"
        };
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, entry.Formatted + "\n");
                _buffer.Add(entry);
                if (_buffer.Count > MaxBufferSize)
                    _buffer.RemoveAt(0);
            }
            catch { /* Best-effort logging */ }
        }
        try { LogAdded?.Invoke(entry); } catch { }
    }

    /// <summary>Get recent log entries from in-memory buffer.</summary>
    public static List<LogEntry> GetRecentLogs(int count = 100)
    {
        lock (_lock)
        {
            var start = Math.Max(0, _buffer.Count - count);
            return _buffer.GetRange(start, _buffer.Count - start).ToList();
        }
    }

    /// <summary>Clear the in-memory log buffer.</summary>
    public static void ClearBuffer()
    {
        lock (_lock) { _buffer.Clear(); }
    }

    /// <summary>Clean up old log files (keep last 10).</summary>
    public static void CleanOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var files = new DirectoryInfo(LogDir).GetFiles("paqet*.log");
            if (files.Length <= 10) return;

            Array.Sort(files, (a, b) => b.CreationTime.CompareTo(a.CreationTime));
            for (int i = 10; i < files.Length; i++)
            {
                try { files[i].Delete(); } catch { }
            }
        }
        catch { }
    }
}

/// <summary>Single log entry for in-memory buffer and UI display.</summary>
public sealed class LogEntry
{
    public DateTime Time { get; init; }
    public string Level { get; init; } = "";
    public string Message { get; init; } = "";
    public string Formatted { get; init; } = "";
}
