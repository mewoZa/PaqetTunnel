using System;
using System.IO;
using System.Text.Json;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

/// <summary>
/// Manages paqet YAML config and app-level JSON settings.
/// All paths come from AppPaths (centralized in %LOCALAPPDATA%\PaqetTunnel).
/// </summary>
public sealed class ConfigService
{
    public string PaqetConfigPath => AppPaths.PaqetConfigPath;
    public string PaqetDirectory => AppPaths.ConfigDir;

    // ── Paqet YAML Config ─────────────────────────────────────────

    public PaqetConfig ReadPaqetConfig()
    {
        if (!File.Exists(AppPaths.PaqetConfigPath))
            return new PaqetConfig();

        var yaml = File.ReadAllText(AppPaths.PaqetConfigPath);
        return PaqetConfig.FromYaml(yaml);
    }

    public void WritePaqetConfig(PaqetConfig config)
    {
        AppPaths.EnsureDirectories();
        File.WriteAllText(AppPaths.PaqetConfigPath, config.ToYaml());
    }

    public bool PaqetConfigExists() => File.Exists(AppPaths.PaqetConfigPath);

    /// <summary>Migrate config from old port 1080 to 10800 (Windows svchost conflict).</summary>
    public void MigrateConfigPort()
    {
        if (!PaqetConfigExists()) return;
        try
        {
            var yaml = File.ReadAllText(AppPaths.PaqetConfigPath);
            if (yaml.Contains(":1080") && !yaml.Contains(":10800"))
            {
                yaml = yaml.Replace("0.0.0.0:1080", "127.0.0.1:10800")
                           .Replace("127.0.0.1:1080", "127.0.0.1:10800");
                File.WriteAllText(AppPaths.PaqetConfigPath, yaml);
                Logger.Info("Migrated config SOCKS5 port from 1080 to 10800");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Config migration failed", ex);
        }
    }

    // ── App Settings (JSON) ───────────────────────────────────────

    public AppSettings ReadAppSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            // BUG-01 fix: decrypt protected password, or keep plaintext for backward compat
            if (!string.IsNullOrEmpty(settings.ServerSshPasswordProtected))
                settings.ServerSshPassword = CredentialHelper.Unprotect(settings.ServerSshPasswordProtected);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void WriteAppSettings(AppSettings settings)
    {
        try
        {
            AppPaths.EnsureDirectories();
            // BUG-01 fix: encrypt SSH password before persisting
            if (!string.IsNullOrEmpty(settings.ServerSshPassword))
                settings.ServerSshPasswordProtected = CredentialHelper.Protect(settings.ServerSshPassword);
            var savedPlain = settings.ServerSshPassword;
            settings.ServerSshPassword = ""; // Don't write plaintext to disk
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppPaths.SettingsPath, json);
            settings.ServerSshPassword = savedPlain; // Restore in-memory value
        }
        catch (Exception ex) { Logger.Error("Failed to save app settings", ex); }
    }
}
