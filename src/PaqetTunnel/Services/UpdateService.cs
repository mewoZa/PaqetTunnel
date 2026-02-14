using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PaqetTunnel.Services;

/// <summary>
/// Checks GitHub for updates by comparing local commit SHA with remote master.
/// Supports silent in-app updates with progress streaming via log file polling.
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
            Logger.Info("Update check started");
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

            if (string.IsNullOrEmpty(localSha))
                return (true, localSha, remoteSha, "Version unknown — update recommended");

            // Compare SHAs: .commit may store short (7-char) or full (40-char) SHA
            if (remoteSha.StartsWith(localSha, StringComparison.OrdinalIgnoreCase) ||
                localSha.StartsWith(remoteSha, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"No update available (local={localSha[..7]}, remote={remoteSha[..7]})");
                return (false, localSha, remoteSha, "");
            }

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
    /// Run setup.ps1 update with progress reporting via log file polling.
    /// The script writes structured output that we capture for GUI progress.
    /// </summary>
    public static async Task<(bool Success, string Message)> RunSilentUpdateAsync(Action<string> onProgress)
    {
        string? logFile = null;
        string? wrapperScript = null;
        try
        {
            // Locate or download setup.ps1
            onProgress("Locating setup script...");
            var setupScript = await ResolveSetupScriptAsync(onProgress);
            if (setupScript == null)
                return (false, "Could not find or download setup.ps1");

            // Create temp log file for progress capture
            logFile = Path.Combine(Path.GetTempPath(), $"paqet-update-{Environment.ProcessId}.log");
            if (File.Exists(logFile)) File.Delete(logFile);
            File.WriteAllText(logFile, ""); // Create empty file

            // Create wrapper script that redirects all output to log file
            // This is necessary because Verb="runas" prevents stdout capture
            wrapperScript = Path.Combine(Path.GetTempPath(), $"pt-wrapper-{Environment.ProcessId}.ps1");
            var wrapperContent = $@"
$ErrorActionPreference = 'Continue'
$logPath = '{logFile.Replace("'", "''")}'
function Log($msg) {{ $msg | Out-File $logPath -Append -Encoding utf8 }}
try {{
    Log '[STEP] Starting update...'
    $output = & powershell -ExecutionPolicy Bypass -File '{setupScript.Replace("'", "''")}' update -y -Silent -Launch 2>&1
    foreach ($line in $output) {{
        $s = $line.ToString()
        if ($s -and $s.Trim()) {{ Log $s }}
    }}
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {{
        Log ""[ERR] Update script exited with code $LASTEXITCODE""
        exit $LASTEXITCODE
    }}
    Log '[OK] Update complete'
}} catch {{
    Log ""[ERR] $($_.Exception.Message)""
    exit 1
}}
";
            File.WriteAllText(wrapperScript, wrapperContent);

            onProgress("Starting update (admin required)...");
            Logger.Info($"Running update via wrapper: {wrapperScript}");
            Logger.Info($"Progress log: {logFile}");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{wrapperScript}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                Logger.Error("Failed to start updater process — Process.Start returned null");
                return (false, "Failed to start updater process");
            }

            Logger.Info($"Update process started (PID={proc.Id})");
            onProgress("Updating...");

            // Single background task handles both log polling and process exit
            var capturedLogFile = logFile;
            var capturedWrapperScript = wrapperScript;
            _ = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var lastLineCount = 0;
                try
                {
                    while (!proc.HasExited && !cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(500, cts.Token);
                        try
                        {
                            if (capturedLogFile == null) continue;
                            var lines = File.ReadAllLines(capturedLogFile);
                            for (int i = lastLineCount; i < lines.Length; i++)
                            {
                                var line = lines[i].Trim();
                                if (string.IsNullOrEmpty(line)) continue;
                                var display = ParseProgressLine(line);
                                if (!string.IsNullOrEmpty(display))
                                    onProgress(display);
                                Logger.Debug($"Update: {line}");
                            }
                            lastLineCount = lines.Length;
                        }
                        catch { /* file may be locked by writer */ }
                    }

                    // Process exited — read any remaining log lines
                    try
                    {
                        if (capturedLogFile != null && File.Exists(capturedLogFile))
                        {
                            var lines = File.ReadAllLines(capturedLogFile);
                            for (int i = lastLineCount; i < lines.Length; i++)
                            {
                                var line = lines[i].Trim();
                                if (string.IsNullOrEmpty(line)) continue;
                                var display = ParseProgressLine(line);
                                if (!string.IsNullOrEmpty(display))
                                    onProgress(display);
                            }
                        }
                    }
                    catch { }

                    if (proc.ExitCode != 0)
                    {
                        Logger.Warn($"Update process exited with code {proc.ExitCode}");
                        onProgress($"Update exited with code {proc.ExitCode}");
                    }
                    else
                    {
                        Logger.Info($"Update process completed successfully (PID={proc.Id})");
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Logger.Debug($"Update monitor error: {ex.Message}"); }
                finally
                {
                    proc.Dispose();
                    CleanupTempFiles(capturedLogFile, capturedWrapperScript);
                }
            });

            return (true, "Update started");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Logger.Info("Update cancelled — UAC declined");
            CleanupTempFiles(logFile, wrapperScript);
            return (false, "Update cancelled — admin permission required");
        }
        catch (Exception ex)
        {
            Logger.Error("Update failed", ex);
            CleanupTempFiles(logFile, wrapperScript);
            return (false, ex.Message);
        }
    }

    /// <summary>Locate setup.ps1 locally or download from GitHub.</summary>
    private static async Task<string?> ResolveSetupScriptAsync(Action<string> onProgress)
    {
        // Check source clone directory first
        var sourceDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "PaqetTunnel", "setup.ps1");
        if (File.Exists(sourceDir))
        {
            Logger.Info($"Using setup.ps1 from source: {sourceDir}");
            return sourceDir;
        }

        // Check install directory
        var installDir = Path.Combine(AppPaths.DataDir, "setup.ps1");
        if (File.Exists(installDir))
        {
            Logger.Info($"Using setup.ps1 from install dir: {installDir}");
            return installDir;
        }

        // Download from GitHub
        try
        {
            onProgress("Downloading setup script...");
            Logger.Info("setup.ps1 not found locally, downloading from GitHub...");
            Logger.Warn("Downloading setup.ps1 from GitHub — verify update source is trusted");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var content = await http.GetStringAsync(
                "https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1");
            var tempScript = Path.Combine(Path.GetTempPath(), "pt-setup.ps1");
            await File.WriteAllTextAsync(tempScript, content);
            Logger.Info($"Downloaded setup.ps1 to {tempScript}");
            return tempScript;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to download setup.ps1", ex);
            onProgress($"Failed to download setup script: {ex.Message}");
            return null;
        }
    }

    /// <summary>Parse a structured log line into a user-friendly progress message.</summary>
    private static string ParseProgressLine(string line)
    {
        if (line.StartsWith("[STEP] ")) return line[7..];
        if (line.StartsWith("[OK] ")) return "✓ " + line[5..];
        if (line.StartsWith("[WARN] ")) return "⚠ " + line[7..];
        if (line.StartsWith("[ERR] ")) return "✗ " + line[6..];
        // Pass through non-prefixed lines that look like progress
        if (line.Length > 2 && !line.StartsWith("[")) return line;
        return "";
    }

    private static void CleanupTempFiles(string? logFile, string? wrapperScript)
    {
        try { if (logFile != null && File.Exists(logFile)) { File.Delete(logFile); Logger.Debug($"Cleaned up log file: {logFile}"); } } catch { }
        try { if (wrapperScript != null && File.Exists(wrapperScript)) { File.Delete(wrapperScript); Logger.Debug($"Cleaned up wrapper: {wrapperScript}"); } } catch { }
    }

    /// <summary>Legacy: opens elevated PowerShell window for update.</summary>
    public static async Task<bool> RunUpdateAsync()
    {
        var (success, _) = await RunSilentUpdateAsync(_ => { });
        return success;
    }
}
