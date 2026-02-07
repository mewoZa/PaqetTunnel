using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PaqetTunnel.Services;

/// <summary>
/// Checks GitHub for updates by comparing local commit SHA with remote master.
/// Triggers setup.ps1 update when a new version is available.
/// </summary>
public sealed class UpdateService
{
    private const string API_URL = "https://api.github.com/repos/mewoZa/PaqetTunnel/commits/master";
    private static readonly string CommitFile = Path.Combine(AppPaths.DataDir, ".commit");
    private static readonly string LastCheckFile = Path.Combine(AppPaths.DataDir, ".last_update_check");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

    public static bool ShouldCheck()
    {
        try
        {
            if (!File.Exists(LastCheckFile)) return true;
            var last = DateTime.Parse(File.ReadAllText(LastCheckFile).Trim());
            return (DateTime.UtcNow - last) > CheckInterval;
        }
        catch { return true; }
    }

    public static async Task<(bool Available, string CurrentSha, string RemoteSha)> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "PaqetTunnel");

            var json = await http.GetStringAsync(API_URL);
            var match = Regex.Match(json, "\"sha\"\\s*:\\s*\"([a-f0-9]{40})\"");
            if (!match.Success) return (false, "", "");

            var remoteSha = match.Groups[1].Value;
            var localSha = File.Exists(CommitFile) ? File.ReadAllText(CommitFile).Trim() : "";

            // Save check time
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(LastCheckFile, DateTime.UtcNow.ToString("o"));

            if (string.IsNullOrEmpty(localSha) || remoteSha == localSha)
                return (false, localSha, remoteSha);

            Logger.Info($"Update available: {localSha[..7]} → {remoteSha[..7]}");
            return (true, localSha, remoteSha);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Update check failed: {ex.Message}");
            return (false, "", "");
        }
    }

    public static async Task<bool> RunUpdateAsync()
    {
        try
        {
            var setupScript = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "PaqetTunnel", "setup.ps1");

            if (!File.Exists(setupScript))
            {
                Logger.Info("setup.ps1 not found locally, downloading...");
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var content = await http.GetStringAsync(
                    "https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1");
                setupScript = Path.Combine(Path.GetTempPath(), "pt-setup.ps1");
                await File.WriteAllTextAsync(setupScript, content);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-ExecutionPolicy Bypass -File \"{setupScript}\" update -y",
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update failed", ex);
            return false;
        }
    }
}
