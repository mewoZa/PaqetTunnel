using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PaqetTunnel.Services;

/// <summary>
/// Manages the paqet binary process — start, stop, status, download from GitHub.
/// All paths come from AppPaths (centralized in %LOCALAPPDATA%\PaqetTunnel).
/// Paqet CLI uses cobra subcommands: run, version, iface, ping, dump, secret.
/// </summary>
public sealed class PaqetService
{
    private const string GITHUB_API = "https://api.github.com/repos/hanselime/paqet/releases/latest";
    public const int SOCKS_PORT = 10800;

    private Process? _paqetProcess;
    private readonly object _processLock = new(); // BUG-06: synchronize _paqetProcess access

    public string BinaryPath => AppPaths.BinaryPath;
    public string ConfigPath => AppPaths.PaqetConfigPath;
    public string PaqetDirectory => AppPaths.BinDir;

    /// <summary>Check if the paqet binary exists on disk.</summary>
    public bool BinaryExists()
    {
        var exists = File.Exists(AppPaths.BinaryPath);
        Logger.Debug($"BinaryExists: {exists} — {AppPaths.BinaryPath}");
        return exists;
    }

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
            if (proc == null)
            {
                Logger.Warn("IsRunning: tasklist process failed to start");
                return false;
            }
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            // BUG-14 fix: parse CSV rows and exact-match image name to avoid partial matches
            var found = false;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim().Trim('"');
                var firstField = trimmed.Split('"')[0];
                if (firstField.Equals(AppPaths.BINARY_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            Logger.Debug($"IsRunning: {found} — tasklist output: {output.Trim().Replace("\r\n", " | ")}");
            return found;
        }
        catch (Exception ex)
        {
            Logger.Error("IsRunning: exception", ex);
            return false;
        }
    }

    /// <summary>Check if SOCKS5 port is accepting connections.</summary>
    public static bool IsPortListening(int port = SOCKS_PORT)
    {
        try
        {
            // Check active TCP listeners via IPGlobalProperties — no connection needed, no socket exhaustion
            var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners();
            var listening = listeners.Any(ep => ep.Port == port);
            Logger.Debug($"IsPortListening: port {port} {(listening ? "OPEN" : "not listening")} (via IPGlobalProperties, {listeners.Length} total listeners)");
            return listening;
        }
        catch (Exception ex)
        {
            Logger.Debug($"IsPortListening: IPGlobalProperties exception — {ex.Message}");
            // Fallback: try socket connect
            return IsPortListeningFallback(port);
        }
    }

    private static bool IsPortListeningFallback(int port)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.LingerState = new LingerOption(true, 0);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            var ep = new IPEndPoint(IPAddress.Loopback, port);
            var result = socket.BeginConnect(ep, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(500);
            if (connected)
            {
                socket.EndConnect(result);
                return socket.Connected;
            }
            return false;
        }
        catch { return false; }
        finally
        {
            try { socket?.Close(); } catch { }
        }
    }

    /// <summary>Check if paqet is fully ready (process running + port listening).</summary>
    public bool IsReady() => IsRunning() && IsPortListening();

    /// <summary>
    /// Verify actual tunnel connectivity by making a SOCKS5 request through the tunnel.
    /// Returns the public IP seen through the tunnel, or null on failure.
    /// </summary>
    private static readonly string[] IpCheckEndpoints = new[]
    {
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
        "https://checkip.amazonaws.com",
    };

    public static async Task<string?> CheckTunnelConnectivityAsync(int timeoutMs = 5000)
    {
        var proxy = new WebProxy($"socks5://127.0.0.1:{SOCKS_PORT}");
        using var handler = new System.Net.Http.HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetTunnel/1.0");

        foreach (var endpoint in IpCheckEndpoints)
        {
            try
            {
                var ip = await http.GetStringAsync(endpoint);
                var trimmed = ip?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed.Length <= 45)
                {
                    Logger.Debug($"CheckTunnelConnectivity OK via {endpoint}: {trimmed}");
                    return trimmed;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"CheckTunnelConnectivity ({endpoint}): {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>Start the paqet tunnel using the 'run' subcommand. Retries on port bind failure.</summary>
    public (bool Success, string Message) Start()
    {
        Logger.Info("Start() called");

        if (!BinaryExists())
        {
            Logger.Warn("Start: binary not found");
            return (false, "Paqet binary not found. Run setup first.");
        }

        if (IsRunning())
        {
            var portUp = IsPortListening();
            Logger.Info($"Start: already running, port listening: {portUp}");
            return (true, portUp ? "Already connected." : "Process running, waiting for port...");
        }

        // Read and validate config before starting
        try
        {
            if (File.Exists(AppPaths.PaqetConfigPath))
            {
                var configContent = File.ReadAllText(AppPaths.PaqetConfigPath);
                Logger.Debug($"Config content:\n{configContent}");

                // Validate critical config fields
                var config = Models.PaqetConfig.FromYaml(configContent);
                if (string.IsNullOrEmpty(config.ServerAddr))
                    Logger.Warn("Config validation: server.addr is EMPTY — tunnel will fail to connect");
                if (string.IsNullOrEmpty(config.Key))
                    Logger.Warn("Config validation: transport.kcp.key is EMPTY — tunnel will fail to authenticate");
                if (string.IsNullOrEmpty(config.Interface))
                    Logger.Warn("Config validation: network.interface is EMPTY — tunnel may fail");
                if (string.IsNullOrEmpty(config.Ipv4Addr))
                    Logger.Warn("Config validation: network.ipv4.addr is EMPTY — tunnel may fail");
                if (string.IsNullOrEmpty(config.RouterMac))
                    Logger.Warn("Config validation: network.ipv4.router_mac is EMPTY — tunnel will fail");
                Logger.Info($"Config: server={config.ServerAddr}, iface={config.Interface}, socks={config.SocksListen}");
            }
            else
            {
                Logger.Warn($"Config file NOT FOUND: {AppPaths.PaqetConfigPath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to read config for logging", ex);
        }

        // Retry up to 3 times on bind failure (ICS ephemeral port conflict)
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var result = StartOnce(attempt);
            if (result.Success)
                return result;

            // Check if it's a bind failure (port conflict with ICS/svchost)
            if (result.Message.Contains("failed to listen", StringComparison.OrdinalIgnoreCase)
                || result.Message.Contains("access a socket", StringComparison.OrdinalIgnoreCase)
                || result.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn($"Bind failure on attempt {attempt}/3, retrying in 2s...");
                Stop(); // kill the failed process
                Thread.Sleep(2000);
                continue;
            }

            return result; // non-retryable error
        }

        return (false, "Failed to bind SOCKS5 port after 3 attempts. ICS may be conflicting.");
    }

    private (bool Success, string Message) StartOnce(int attempt)
    {
        try
        {
            var arguments = $"run --config \"{AppPaths.PaqetConfigPath}\"";
            Logger.Info($"Starting (attempt {attempt}): {AppPaths.BinaryPath} {arguments}");
            Logger.Info($"WorkingDir: {AppPaths.BinDir}");

            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.BinaryPath,
                Arguments = arguments,
                WorkingDirectory = AppPaths.BinDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                Logger.Error("Start: Process.Start returned null");
                return (false, "Failed to start process.");
            }

            lock (_processLock) { _paqetProcess = proc; }
            var pid = proc.Id;
            Logger.Info($"Process started with PID {pid}");

            // Read stdout/stderr asynchronously to prevent buffer deadlock
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    stdoutBuilder.AppendLine(e.Data);
                    Logger.Debug($"[paqet stdout] {e.Data}");
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    stderrBuilder.AppendLine(e.Data);
                    Logger.Debug($"[paqet stderr] {e.Data}");
                }
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Wait up to 15s for process to bind to SOCKS5 port
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(500);

                if (proc.HasExited)
                {
                    var stderr = stderrBuilder.ToString().Trim();
                    var stdout = stdoutBuilder.ToString().Trim();
                    Logger.Error($"Process exited prematurely! ExitCode={proc.ExitCode}");
                    Logger.Error($"  stdout: {stdout}");
                    Logger.Error($"  stderr: {stderr}");
                    var exitMsg = !string.IsNullOrEmpty(stderr) ? stderr
                        : !string.IsNullOrEmpty(stdout) ? stdout
                        : $"Exit code: {proc.ExitCode}";
                    return (false, $"Process exited: {exitMsg}");
                }

                // Check for bind failure in stdout (paqet logs bind errors to stdout)
                var currentOutput = stdoutBuilder.ToString();
                if (currentOutput.Contains("failed to listen", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn($"Bind failure detected in stdout: {currentOutput.Trim()}");
                    return (false, currentOutput.Trim());
                }

                if (IsPortListening())
                {
                    Logger.Info($"Port {SOCKS_PORT} is now listening! Connected in {(i + 1) * 500}ms");
                    return (true, $"Connected (PID {pid}).");
                }

                if (i % 4 == 3)
                    Logger.Debug($"Waiting for port... attempt {i + 1}/30, process alive: {!proc.HasExited}");
            }

            // Process is alive but port not ready after 15s
            var running = IsRunning();
            var stderrFinal = stderrBuilder.ToString().Trim();
            var stdoutFinal = stdoutBuilder.ToString().Trim();
            Logger.Warn($"Port not ready after 15s. Process alive: {running}");
            Logger.Warn($"  stdout so far: {stdoutFinal}");
            Logger.Warn($"  stderr so far: {stderrFinal}");

            if (running)
            {
                var detail = !string.IsNullOrEmpty(stderrFinal) ? $" ({stderrFinal})" : "";
                return (true, $"Started (PID {pid}), port binding pending.{detail}");
            }

            return (false, $"Process failed to start. stderr: {stderrFinal}");
        }
        catch (Exception ex)
        {
            Logger.Error("Start: exception", ex);
            return (false, $"Start failed: {ex.Message}");
        }
    }

    /// <summary>Stop the paqet process forcefully.</summary>
    public (bool Success, string Message) Stop()
    {
        Logger.Info("Stop() called");

        // BUG-06 fix: synchronize _paqetProcess access
        lock (_processLock)
        {
            if (_paqetProcess != null)
            {
                try
                {
                    if (!_paqetProcess.HasExited)
                    {
                        Logger.Info($"Killing tracked paqet process PID {_paqetProcess.Id}");
                        _paqetProcess.Kill(entireProcessTree: true);
                        _paqetProcess.WaitForExit(3000);
                    }
                    _paqetProcess.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Tracked process cleanup: {ex.Message}");
                }
                _paqetProcess = null;
            }
        }

        if (!IsRunning())
        {
            Logger.Info("Stop: not running");
            return (true, "Not running.");
        }

        try
        {
            // Kill using Process API for reliability (taskkill can fail across sessions)
            var binaryNameNoExt = Path.GetFileNameWithoutExtension(AppPaths.BINARY_NAME);
            Logger.Info($"Killing processes named: {binaryNameNoExt}");
            var processes = Process.GetProcessesByName(binaryNameNoExt);
            Logger.Info($"Found {processes.Length} matching processes");

            foreach (var proc in processes)
            {
                try
                {
                    Logger.Debug($"Killing PID {proc.Id}");
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                    Logger.Debug($"PID {proc.Id} killed: {proc.HasExited}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to kill PID {proc.Id}", ex);
                }
                finally
                {
                    proc.Dispose();
                }
            }

            // Fallback: try taskkill if Process.Kill didn't work
            if (IsRunning())
            {
                Logger.Warn("Process.Kill didn't work, trying taskkill...");
                RunCommand("taskkill", $"/IM {AppPaths.BINARY_NAME} /F");
            }

            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(200);
                if (!IsRunning())
                {
                    Logger.Info("Stop: process terminated");
                    return (true, "Stopped.");
                }
            }
            var stillRunning = IsRunning();
            Logger.Warn($"Stop: after 2s, still running: {stillRunning}");
            return (stillRunning ? false : true, stillRunning ? "Failed to stop." : "Stopped.");
        }
        catch (Exception ex)
        {
            Logger.Error("Stop: exception", ex);
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
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetTunnel/1.0");
            http.Timeout = TimeSpan.FromSeconds(60);

            var json = await http.GetStringAsync(GITHUB_API);

            var downloadUrl = ExtractAssetUrl(json, "windows", "amd64", ".zip");
            if (string.IsNullOrEmpty(downloadUrl))
                downloadUrl = ExtractAssetUrl(json, "windows", "amd64", ".exe");

            if (string.IsNullOrEmpty(downloadUrl))
                return (false, "No Windows binary found in latest release.");

            progress?.Report($"Downloading: {Path.GetFileName(downloadUrl)}...");

            var bytes = await http.GetByteArrayAsync(downloadUrl);

            // BUG-03 fix: validate download integrity
            if (bytes == null || bytes.Length < 1024)
                return (false, $"Downloaded file too small ({bytes?.Length ?? 0} bytes) — possible corruption.");
            Logger.Info($"Downloaded {bytes.Length} bytes from {downloadUrl}");

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

    // BUG-04/BUG-10 fix: read stdout/stderr asynchronously to prevent deadlock
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
        var stdout = new StringBuilder();
        proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (s, e) => { }; // drain stderr to prevent buffer deadlock
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit(timeout);
        return stdout.ToString();
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
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit(timeout);
        var stdoutStr = stdout.ToString();
        var stderrStr = stderr.ToString();
        return string.IsNullOrEmpty(stdoutStr) ? stderrStr : stdoutStr;
    }

    internal static void RunElevated(string fileName, string arguments)
    {
        // BUG-23 fix: only allow known system commands to be elevated
        var baseName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var allowedCmds = new[] { "netsh", "reg", "net", "route", "ipconfig", "powershell", "schtasks", "sc" };
        if (!allowedCmds.Any(c => baseName == c))
        {
            Logger.Warn($"RunElevated: blocked non-system command: {fileName}");
            throw new InvalidOperationException($"Elevation not allowed for: {fileName}");
        }
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

    /// <summary>
    /// Runs a command that needs admin privileges. Tries non-elevated first (works if
    /// the app is already running elevated via schtasks HIGHEST), falls back to RunElevated (UAC).
    /// </summary>
    internal static void RunAdmin(string fileName, string arguments)
    {
        try
        {
            var output = RunCommandWithStderr(fileName, arguments, 10000);
            if (output.Contains("requires elevation", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Run as administrator", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Debug($"RunAdmin: non-elevated failed, escalating: {output.Trim()}");
                RunElevated(fileName, arguments);
            }
        }
        catch
        {
            RunElevated(fileName, arguments);
        }
    }
}
