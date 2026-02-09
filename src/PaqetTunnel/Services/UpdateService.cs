using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PaqetTunnel.Services;

/// <summary>
/// Checks GitHub for updates by comparing local commit SHA with remote master.
/// Supports silent in-app updates with progress streaming.
/// </summary>
public sealed class UpdateService
{
    private const string API_URL = "https://api.github.com/repos/mewoZa/PaqetTunnel/commits/master";
    private const string COMMITS_URL = "https://api.github.com/repos/mewoZa/PaqetTunnel/commits?per_page=5";
    private static readonly string CommitFile = Path.Combine(AppPaths.DataDir, ".commit");
    private static readonly string LastCheckFile = Path.Combine(AppPaths.DataDir, ".last_update_check");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

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

    public static async Task<(bool Available, string CurrentSha, string RemoteSha, string Message)> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "PaqetTunnel");

            var json = await http.GetStringAsync(API_URL);
            var shaMatch = Regex.Match(json, "\"sha\"\\s*:\\s*\"([a-f0-9]{40})\"");
            if (!shaMatch.Success) return (false, "", "", "");

            var remoteSha = shaMatch.Groups[1].Value;
            var localSha = File.Exists(CommitFile) ? File.ReadAllText(CommitFile).Trim() : "";

            // Extract commit message
            var msgMatch = Regex.Match(json, "\"message\"\\s*:\\s*\"([^\"]+)\"");
            var message = msgMatch.Success ? msgMatch.Groups[1].Value : "";
            // Truncate to first line
            var nl = message.IndexOf("\\n");
            if (nl > 0) message = message[..nl];
            if (message.Length > 60) message = message[..57] + "...";

            // Save check time
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(LastCheckFile, DateTime.UtcNow.ToString("o"));

            if (string.IsNullOrEmpty(localSha) || remoteSha == localSha)
                return (false, localSha, remoteSha, "");

            Logger.Info($"Update available: {localSha[..7]} → {remoteSha[..7]} — {message}");
            return (true, localSha, remoteSha, message);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Update check failed: {ex.Message}");
            return (false, "", "", "");
        }
    }

    /// <summary>
    /// Run setup.ps1 update silently with progress reporting. Returns success.
    /// The script will stop the app, install, and relaunch it.
    /// </summary>
    public static async Task<(bool Success, string Message)> RunSilentUpdateAsync(Action<string> onProgress)
    {
        try
        {
            var setupScript = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "PaqetTunnel", "setup.ps1");

            if (!File.Exists(setupScript))
            {
                onProgress("Downloading setup script...");
                Logger.Info("setup.ps1 not found locally, downloading...");
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var content = await http.GetStringAsync(
                    "https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1");
                setupScript = Path.Combine(Path.GetTempPath(), "pt-setup.ps1");
                await File.WriteAllTextAsync(setupScript, content);
            }

            onProgress("Starting update...");
            Logger.Info("Running silent update via setup.ps1");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-ExecutionPolicy Bypass -File \"{setupScript}\" update -y -Silent -Launch",
                UseShellExecute = true,
                Verb = "runas"
            };

            var proc = Process.Start(psi);
            if (proc == null)
                return (false, "Failed to start updater");

            onProgress("Updating in background...");

            // Wait for the process in background — it will kill us and relaunch
            _ = Task.Run(async () =>
            {
                try
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode != 0)
                        Logger.Warn($"Update process exited with code {proc.ExitCode}");
                }
                catch { /* process may kill us first */ }
            });

            return (true, "Update started");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined UAC
            Logger.Info("Update cancelled — UAC declined");
            return (false, "Update cancelled");
        }
        catch (Exception ex)
        {
            Logger.Error("Update failed", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Legacy: opens elevated PowerShell window for update.</summary>
    public static async Task<bool> RunUpdateAsync()
    {
        var (success, _) = await RunSilentUpdateAsync(_ => { });
        return success;
    }
}
