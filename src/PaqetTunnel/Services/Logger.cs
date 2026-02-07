using System;
using System.IO;

namespace PaqetTunnel.Services;

/// <summary>
/// Simple file-based logger. Writes to %LOCALAPPDATA%\PaqetTunnel\logs\.
/// Enable via AppSettings.DebugMode or by calling Initialize(true).
/// </summary>
public static class Logger
{
    private static bool _enabled;
    private static string _logPath = "";
    private static readonly object _lock = new();

    public static bool IsEnabled => _enabled;
    public static string LogPath => _logPath;
    public static string LogDir => Path.Combine(AppPaths.DataDir, "logs");

    public static void Initialize(bool enabled)
    {
        _enabled = enabled;
        if (!enabled) return;

        try
        {
            Directory.CreateDirectory(LogDir);
            _logPath = Path.Combine(LogDir, $"paqetmanager_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            Write("INFO", $"=== PaqetTunnel Debug Log Started ===");
            Write("INFO", $"Log file: {_logPath}");
            Write("INFO", $"DataDir: {AppPaths.DataDir}");
            Write("INFO", $"BinaryPath: {AppPaths.BinaryPath}");
            Write("INFO", $"ConfigPath: {AppPaths.PaqetConfigPath}");
            Write("INFO", $"Binary exists: {File.Exists(AppPaths.BinaryPath)}");
            Write("INFO", $"Config exists: {File.Exists(AppPaths.PaqetConfigPath)}");
        }
        catch (Exception ex)
        {
            _enabled = false;
            System.Diagnostics.Debug.WriteLine($"Logger init failed: {ex.Message}");
        }
    }

    public static void Debug(string message) => Write("DEBUG", message);
    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        Write("ERROR", msg);
    }

    private static void Write(string level, string message)
    {
        if (!_enabled || string.IsNullOrEmpty(_logPath)) return;
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}\n");
            }
            catch { /* Best-effort logging */ }
        }
    }

    /// <summary>Clean up old log files (keep last 10).</summary>
    public static void CleanOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var files = new DirectoryInfo(LogDir).GetFiles("paqetmanager_*.log");
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
