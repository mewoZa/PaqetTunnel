using System;
using System.Diagnostics;
using System.IO;

namespace PaqetManager;

/// <summary>
/// Centralized path management. All paqet files live under one app-owned folder.
/// Installed: %LOCALAPPDATA%\PaqetManager\
/// Layout:
///   bin\paqet_windows_amd64.exe   — tunnel binary
///   config\client.yaml            — tunnel config
///   settings.json                 — app settings
/// </summary>
public static class AppPaths
{
    public const string BINARY_NAME = "paqet_windows_amd64.exe";

    /// <summary>Root data folder: %LOCALAPPDATA%\PaqetManager</summary>
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaqetManager");

    /// <summary>Bin folder for the paqet binary.</summary>
    public static readonly string BinDir = Path.Combine(DataDir, "bin");

    /// <summary>Config folder for paqet YAML.</summary>
    public static readonly string ConfigDir = Path.Combine(DataDir, "config");

    /// <summary>Full path to the paqet binary.</summary>
    public static readonly string BinaryPath = Path.Combine(BinDir, BINARY_NAME);

    /// <summary>Full path to the paqet client config.</summary>
    public static readonly string PaqetConfigPath = Path.Combine(ConfigDir, "client.yaml");

    /// <summary>Full path to app settings JSON.</summary>
    public static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");

    /// <summary>Full path to our own executable.</summary>
    public static string ExePath => Process.GetCurrentProcess().MainModule?.FileName ?? "";

    /// <summary>Ensure all required directories exist.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BinDir);
        Directory.CreateDirectory(ConfigDir);
    }
}
