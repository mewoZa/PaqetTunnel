using System;
using System.IO;
using System.Text.Json;
using PaqetManager.Models;

namespace PaqetManager.Services;

/// <summary>
/// Manages paqet YAML config and app-level JSON settings.
/// All paths come from AppPaths (centralized in %LOCALAPPDATA%\PaqetManager).
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

    // ── App Settings (JSON) ───────────────────────────────────────

    public AppSettings ReadAppSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppPaths.SettingsPath, json);
        }
        catch { /* Best-effort */ }
    }
}
