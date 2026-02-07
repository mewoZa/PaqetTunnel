using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PaqetManager.Services;

/// <summary>
/// Manages the paqet binary process — start, stop, status, download from GitHub.
/// All paths come from AppPaths (centralized in %LOCALAPPDATA%\PaqetManager).
/// Paqet CLI uses cobra subcommands: run, version, iface, ping, dump, secret.
/// </summary>
public sealed class PaqetService
{
    private const string GITHUB_API = "https://api.github.com/repos/hanselime/paqet/releases/latest";
    private const int SOCKS_PORT = 1080;

    public string BinaryPath => AppPaths.BinaryPath;
    public string ConfigPath => AppPaths.PaqetConfigPath;
    public string PaqetDirectory => AppPaths.BinDir;

    /// <summary>Check if the paqet binary exists on disk.</summary>
    public bool BinaryExists() => File.Exists(AppPaths.BinaryPath);

    /// <summary>Check if paqet process is currently running via tasklist.</summary>
    public bool IsRunning()
    {
        try
        {
            var psi = new ProcessStartInfo("tasklist",
                $"/FI \"IMAGENAME eq {AppPaths.BINARY_NAME}\" /FO CSV /NH")
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
            return output.Contains(AppPaths.BINARY_NAME, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Check if SOCKS5 port is accepting connections.</summary>
    public static bool IsPortListening(int port = SOCKS_PORT)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(IPAddress.Loopback, port, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
            if (connected)
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Check if paqet is fully ready (process running + port listening).</summary>
    public bool IsReady() => IsRunning() && IsPortListening();

    /// <summary>Start the paqet tunnel using the 'run' subcommand.</summary>
    public (bool Success, string Message) Start()
    {
        if (!BinaryExists())
            return (false, "Paqet binary not found. Run setup first.");

        if (IsRunning())
            return (true, IsPortListening() ? "Already connected." : "Process running, waiting for port...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.BinaryPath,
                Arguments = $"run --config \"{AppPaths.PaqetConfigPath}\"",
                WorkingDirectory = AppPaths.BinDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi);
            if (proc == null) return (false, "Failed to start process.");

            var pid = proc.Id;

            // Wait up to 10s for process to bind to SOCKS5 port
            for (int i = 0; i < 20; i++)
            {
                System.Threading.Thread.Sleep(500);

                if (proc.HasExited)
                {
                    var stderr = proc.StandardError.ReadToEnd().Trim();
                    var exitMsg = string.IsNullOrEmpty(stderr) ? $"Exit code: {proc.ExitCode}" : stderr;
                    return (false, $"Process exited: {exitMsg}");
                }

                if (IsPortListening())
                    return (true, $"Connected (PID {pid}).");
            }

            if (IsRunning())
                return (true, $"Started (PID {pid}), port binding pending.");

            return (false, "Process failed to start.");
        }
        catch (Exception ex)
        {
            return (false, $"Start failed: {ex.Message}");
        }
    }

    /// <summary>Stop the paqet process forcefully.</summary>
    public (bool Success, string Message) Stop()
    {
        if (!IsRunning())
            return (true, "Not running.");

        try
        {
            RunCommand("taskkill", $"/IM {AppPaths.BINARY_NAME} /F");
            for (int i = 0; i < 10; i++)
            {
                System.Threading.Thread.Sleep(200);
                if (!IsRunning()) return (true, "Stopped.");
            }
            return (IsRunning() ? false : true, IsRunning() ? "Failed to stop." : "Stopped.");
        }
        catch (Exception ex)
        {
            return (false, $"Stop failed: {ex.Message}");
        }
    }

    /// <summary>Download the latest paqet binary from GitHub releases.</summary>
    public async Task<(bool Success, string Message)> DownloadLatestAsync(IProgress<string>? progress = null)
    {
        try
        {
            AppPaths.EnsureDirectories();
            progress?.Report("Fetching latest release info...");

            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetManager/1.0");
            http.Timeout = TimeSpan.FromSeconds(60);

            var json = await http.GetStringAsync(GITHUB_API);

            var downloadUrl = ExtractAssetUrl(json, "windows", "amd64", ".zip");
            if (string.IsNullOrEmpty(downloadUrl))
                downloadUrl = ExtractAssetUrl(json, "windows", "amd64", ".exe");

            if (string.IsNullOrEmpty(downloadUrl))
                return (false, "No Windows binary found in latest release.");

            progress?.Report($"Downloading: {Path.GetFileName(downloadUrl)}...");

            var bytes = await http.GetByteArrayAsync(downloadUrl);

            if (downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var zipPath = Path.Combine(AppPaths.BinDir, "paqet_latest.zip");
                await File.WriteAllBytesAsync(zipPath, bytes);

                progress?.Report("Extracting...");
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, AppPaths.BinDir, overwriteFiles: true);
                File.Delete(zipPath);
            }
            else
            {
                await File.WriteAllBytesAsync(AppPaths.BinaryPath, bytes);
            }

            progress?.Report("Download complete.");
            return (true, "Paqet binary downloaded successfully.");
        }
        catch (Exception ex)
        {
            return (false, $"Download failed: {ex.Message}");
        }
    }

    /// <summary>Check if config exists, create default if not.</summary>
    public void EnsureConfigExists()
    {
        if (File.Exists(AppPaths.PaqetConfigPath)) return;

        AppPaths.EnsureDirectories();
        var defaultConfig = new Models.PaqetConfig();
        File.WriteAllText(AppPaths.PaqetConfigPath, defaultConfig.ToYaml());
    }

    // ── Paqet CLI Commands ────────────────────────────────────────

    /// <summary>Get the paqet version string via 'version' subcommand.</summary>
    public string? GetVersion()
    {
        if (!BinaryExists()) return null;
        try
        {
            var output = RunCommand(AppPaths.BinaryPath, "version", timeout: 5000);
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Split(':', 2)[1].Trim();
            }
            return output?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Get full version info via 'version' subcommand.</summary>
    public string? GetFullVersionInfo()
    {
        if (!BinaryExists()) return null;
        try
        {
            return RunCommand(AppPaths.BinaryPath, "version", timeout: 5000)?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>List available network interfaces via 'iface' subcommand.</summary>
    public (bool Success, string Output) ListInterfaces()
    {
        if (!BinaryExists())
            return (false, "Paqet binary not found.");
        try
        {
            var output = RunCommand(AppPaths.BinaryPath, "iface", timeout: 10000);
            return (true, output?.Trim() ?? "");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to list interfaces: {ex.Message}");
        }
    }

    /// <summary>Generate a secure 32-byte key via 'secret' subcommand.</summary>
    public (bool Success, string Key) GenerateSecret()
    {
        if (!BinaryExists())
            return (false, "Paqet binary not found.");
        try
        {
            var output = RunCommand(AppPaths.BinaryPath, "secret", timeout: 5000);
            return (true, output?.Trim() ?? "");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to generate secret: {ex.Message}");
        }
    }

    /// <summary>Send a ping packet via 'ping' subcommand.</summary>
    public (bool Success, string Output) Ping(string? payload = null)
    {
        if (!BinaryExists())
            return (false, "Paqet binary not found.");
        try
        {
            var args = $"ping --config \"{AppPaths.PaqetConfigPath}\"";
            if (!string.IsNullOrEmpty(payload))
                args += $" --payload \"{payload}\"";

            var output = RunCommandWithStderr(AppPaths.BinaryPath, args, timeout: 15000);
            return (true, output?.Trim() ?? "Ping sent.");
        }
        catch (Exception ex)
        {
            return (false, $"Ping failed: {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static string? ExtractAssetUrl(string json, params string[] keywords)
    {
        const string marker = "\"browser_download_url\"";
        var idx = 0;
        while ((idx = json.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            idx += marker.Length;
            var colonIdx = json.IndexOf(':', idx);
            if (colonIdx < 0) break;
            var quoteStart = json.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) break;
            var quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) break;

            var url = json[(quoteStart + 1)..quoteEnd];
            if (keywords.All(k => url.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return url;
        }
        return null;
    }

    internal static string RunCommand(string fileName, string arguments, int timeout = 10000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process.");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(timeout);
        return output;
    }

    internal static string RunCommandWithStderr(string fileName, string arguments, int timeout = 10000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(timeout);
        return string.IsNullOrEmpty(stdout) ? stderr : stdout;
    }

    internal static void RunElevated(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(15000);
    }
}
