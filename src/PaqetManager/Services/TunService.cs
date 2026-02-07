using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PaqetManager.Services;

/// <summary>
/// Manages the WinTun TUN adapter via tun2socks.exe for full system traffic tunneling.
/// Architecture: System Traffic → TUN Adapter → tun2socks → paqet SOCKS5 :10800 → VPS
/// Requires admin privileges for adapter creation and routing changes.
/// </summary>
public sealed class TunService
{
    private const string TUN_ADAPTER_NAME = "PaqetTun";
    private const string TUN_IP = "10.0.85.2";
    private const string TUN_GATEWAY = "10.0.85.1";
    private const string TUN_SUBNET = "255.255.255.0";
    private const string TUN_CIDR = "10.0.85.2/24";
    private const string DNS_PRIMARY = "8.8.8.8";
    private const string DNS_SECONDARY = "8.8.4.4";
    private const int TUN_METRIC = 1;

    private Process? _tun2socksProcess;
    private string? _originalGateway;
    private string? _originalInterface;
    private string? _serverIp;

    public bool Tun2SocksExists() => File.Exists(AppPaths.Tun2SocksPath);
    public bool WintunExists() => File.Exists(AppPaths.WintunDllPath);
    public bool AllBinariesExist() => Tun2SocksExists() && WintunExists();

    /// <summary>Check if the tun2socks process is running.</summary>
    public bool IsRunning()
    {
        if (_tun2socksProcess != null && !_tun2socksProcess.HasExited)
            return true;

        try
        {
            var procs = Process.GetProcessesByName("tun2socks");
            var running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return running;
        }
        catch { return false; }
    }

    /// <summary>Check if the TUN adapter exists and has an IP assigned.</summary>
    public bool IsTunAdapterUp()
    {
        try
        {
            var iface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Equals(TUN_ADAPTER_NAME, StringComparison.OrdinalIgnoreCase)
                    || n.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase));
            return iface?.OperationalStatus == OperationalStatus.Up;
        }
        catch { return false; }
    }

    /// <summary>
    /// Start the full system tunnel. Requires paqet SOCKS5 to be running on port 1080.
    /// Steps: 1) Start tun2socks  2) Configure TUN adapter IP  3) Set routes  4) Set DNS
    /// </summary>
    public async Task<(bool Success, string Message)> StartAsync(string paqetServerIp)
    {
        Logger.Info("TunService.StartAsync called");

        if (!AllBinariesExist())
        {
            var missing = !Tun2SocksExists() ? "tun2socks.exe" : "wintun.dll";
            Logger.Warn($"TUN start: {missing} not found");
            return (false, $"{missing} not found. Run setup first.");
        }

        if (!PaqetService.IsPortListening(10800))
        {
            Logger.Warn("TUN start: SOCKS5 port 10800 not listening");
            return (false, "Paqet SOCKS5 not running. Start paqet first.");
        }

        if (IsRunning())
        {
            Logger.Info("TUN: already running");
            return (true, "TUN tunnel already running.");
        }

        _serverIp = paqetServerIp;

        try
        {
            // Save current default gateway before we change routes
            _originalGateway = GetDefaultGateway();
            _originalInterface = GetDefaultInterfaceName();
            Logger.Info($"Original gateway: {_originalGateway}, interface: {_originalInterface}");

            // Step 1: Start tun2socks
            Logger.Info("Starting tun2socks...");
            var startResult = StartTun2Socks();
            if (!startResult.Success)
                return startResult;

            // Wait for TUN adapter to appear
            Logger.Info("Waiting for TUN adapter...");
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                if (IsTunAdapterUp())
                {
                    Logger.Info($"TUN adapter up after {(i + 1) * 500}ms");
                    break;
                }
                if (i == 19)
                {
                    Logger.Warn("TUN adapter did not come up in 10s");
                    StopTun2Socks();
                    return (false, "TUN adapter failed to initialize.");
                }
            }

            // Step 2: Configure TUN adapter IP
            Logger.Info("Configuring TUN adapter...");
            var configResult = ConfigureTunAdapter();
            if (!configResult.Success)
            {
                StopTun2Socks();
                return configResult;
            }

            // Step 3: Set routes
            Logger.Info("Setting routes...");
            var routeResult = SetRoutes(paqetServerIp);
            if (!routeResult.Success)
            {
                RemoveRoutes(paqetServerIp);
                StopTun2Socks();
                return routeResult;
            }

            // Step 4: Set DNS
            Logger.Info("Setting DNS...");
            SetDns();

            Logger.Info("TUN tunnel fully started");
            return (true, "Full system tunnel active.");
        }
        catch (Exception ex)
        {
            Logger.Error("TUN start exception", ex);
            await StopAsync();
            return (false, $"TUN start failed: {ex.Message}");
        }
    }

    /// <summary>Stop the TUN tunnel, restore routes and DNS.</summary>
    public async Task<(bool Success, string Message)> StopAsync()
    {
        Logger.Info("TunService.StopAsync called");
        var errors = new StringBuilder();

        try
        {
            // Remove routes first
            if (!string.IsNullOrEmpty(_serverIp))
            {
                var routeResult = RemoveRoutes(_serverIp);
                if (!routeResult.Success) errors.AppendLine(routeResult.Message);
            }

            // Restore DNS
            RestoreDns();

            // Stop tun2socks
            StopTun2Socks();

            // Wait for adapter to disappear
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(300);
                if (!IsTunAdapterUp()) break;
            }

            _serverIp = null;
            _originalGateway = null;
            _originalInterface = null;

            Logger.Info("TUN tunnel stopped");
            return errors.Length == 0
                ? (true, "TUN tunnel stopped.")
                : (true, $"Stopped with warnings: {errors}");
        }
        catch (Exception ex)
        {
            Logger.Error("TUN stop exception", ex);
            return (false, $"TUN stop failed: {ex.Message}");
        }
    }

    /// <summary>Download tun2socks.exe and wintun.dll from their GitHub/official sources.</summary>
    public async Task<(bool Success, string Message)> DownloadBinariesAsync(IProgress<string>? progress = null)
    {
        try
        {
            AppPaths.EnsureDirectories();
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetManager/1.0");
            http.Timeout = TimeSpan.FromSeconds(120);

            // Download tun2socks
            if (!Tun2SocksExists())
            {
                progress?.Report("Downloading tun2socks...");
                const string tun2socksUrl = "https://github.com/xjasonlyu/tun2socks/releases/download/v2.6.0/tun2socks-windows-amd64.zip";
                Logger.Info($"Downloading tun2socks from {tun2socksUrl}");

                var bytes = await http.GetByteArrayAsync(tun2socksUrl);
                var zipPath = Path.Combine(AppPaths.BinDir, "tun2socks_latest.zip");
                await File.WriteAllBytesAsync(zipPath, bytes);

                ZipFile.ExtractToDirectory(zipPath, AppPaths.BinDir, overwriteFiles: true);
                File.Delete(zipPath);

                // Zip contains tun2socks-windows-amd64.exe — rename to tun2socks.exe
                var extractedName = Path.Combine(AppPaths.BinDir, "tun2socks-windows-amd64.exe");
                if (File.Exists(extractedName) && !Tun2SocksExists())
                    File.Move(extractedName, AppPaths.Tun2SocksPath);

                if (!Tun2SocksExists())
                    return (false, "tun2socks binary not found after extraction.");

                Logger.Info("tun2socks downloaded");
            }

            // Download wintun.dll
            if (!WintunExists())
            {
                progress?.Report("Downloading wintun.dll...");
                const string wintunUrl = "https://www.wintun.net/builds/wintun-0.14.1.zip";
                Logger.Info($"Downloading wintun from {wintunUrl}");

                var bytes = await http.GetByteArrayAsync(wintunUrl);
                var zipPath = Path.Combine(AppPaths.BinDir, "wintun_latest.zip");
                await File.WriteAllBytesAsync(zipPath, bytes);

                // Extract to temp, copy the correct architecture DLL
                var extractDir = Path.Combine(AppPaths.BinDir, "wintun_extract");
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                // wintun.zip has: wintun/bin/amd64/wintun.dll
                var dllPath = Path.Combine(extractDir, "wintun", "bin", "amd64", "wintun.dll");
                if (File.Exists(dllPath))
                {
                    File.Copy(dllPath, AppPaths.WintunDllPath, overwrite: true);
                }
                else
                {
                    // Try flat structure
                    var altPath = Directory.GetFiles(extractDir, "wintun.dll", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (altPath != null)
                        File.Copy(altPath, AppPaths.WintunDllPath, overwrite: true);
                }

                // Cleanup
                try { Directory.Delete(extractDir, recursive: true); } catch { }
                try { File.Delete(zipPath); } catch { }

                if (!WintunExists())
                    return (false, "wintun.dll not found after extraction.");

                Logger.Info("wintun.dll downloaded");
            }

            progress?.Report("TUN binaries ready.");
            return (true, "TUN binaries downloaded successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error("TUN binary download failed", ex);
            return (false, $"Download failed: {ex.Message}");
        }
    }

    // ── Private Helpers ───────────────────────────────────────────

    private (bool Success, string Message) StartTun2Socks()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.Tun2SocksPath,
                Arguments = $"-device tun://{TUN_ADAPTER_NAME} -proxy socks5://127.0.0.1:10800",
                WorkingDirectory = AppPaths.BinDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Logger.Info($"tun2socks cmd: {psi.FileName} {psi.Arguments}");

            _tun2socksProcess = Process.Start(psi);
            if (_tun2socksProcess == null)
                return (false, "Failed to start tun2socks.");

            // Async read to prevent buffer deadlock
            _tun2socksProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) Logger.Debug($"[tun2socks stdout] {e.Data}");
            };
            _tun2socksProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) Logger.Debug($"[tun2socks stderr] {e.Data}");
            };
            _tun2socksProcess.BeginOutputReadLine();
            _tun2socksProcess.BeginErrorReadLine();

            Thread.Sleep(1000);

            if (_tun2socksProcess.HasExited)
            {
                Logger.Error($"tun2socks exited with code {_tun2socksProcess.ExitCode}");
                return (false, $"tun2socks exited immediately (code {_tun2socksProcess.ExitCode}). Needs admin privileges.");
            }

            Logger.Info($"tun2socks started PID {_tun2socksProcess.Id}");
            return (true, "tun2socks started.");
        }
        catch (Exception ex)
        {
            Logger.Error("tun2socks start exception", ex);
            return (false, $"tun2socks start failed: {ex.Message}");
        }
    }

    private void StopTun2Socks()
    {
        try
        {
            if (_tun2socksProcess != null && !_tun2socksProcess.HasExited)
            {
                Logger.Info($"Killing tun2socks PID {_tun2socksProcess.Id}");
                _tun2socksProcess.Kill(entireProcessTree: true);
                _tun2socksProcess.WaitForExit(3000);
                _tun2socksProcess.Dispose();
                _tun2socksProcess = null;
            }

            // Kill any remaining tun2socks processes
            foreach (var proc in Process.GetProcessesByName("tun2socks"))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000);
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("StopTun2Socks exception", ex);
        }
    }

    private (bool Success, string Message) ConfigureTunAdapter()
    {
        try
        {
            // Set IP address on TUN adapter
            var output = RunNetsh($"interface ip set address \"{TUN_ADAPTER_NAME}\" static {TUN_IP} {TUN_SUBNET} {TUN_GATEWAY}");
            Logger.Debug($"netsh set address: {output}");
            return (true, "TUN adapter configured.");
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigureTunAdapter exception", ex);
            return (false, $"TUN config failed: {ex.Message}");
        }
    }

    private (bool Success, string Message) SetRoutes(string serverIp)
    {
        try
        {
            // Route paqet server traffic through original gateway (prevent circular routing)
            if (!string.IsNullOrEmpty(_originalGateway))
            {
                RunRoute($"add {serverIp} mask 255.255.255.255 {_originalGateway} metric 5");
                Logger.Info($"Added server route: {serverIp} via {_originalGateway}");
            }

            // Get TUN interface index
            var tunIndex = GetInterfaceIndex(TUN_ADAPTER_NAME);
            if (tunIndex <= 0)
            {
                Logger.Warn("Could not find TUN interface index, using gateway-based routing");
                RunRoute($"add 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC}");
                RunRoute($"add 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC}");
            }
            else
            {
                Logger.Info($"TUN interface index: {tunIndex}");
                // Split default route into two halves to override existing default route
                RunRoute($"add 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC} if {tunIndex}");
                RunRoute($"add 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC} if {tunIndex}");
            }

            Logger.Info("Routes set for full system tunnel");
            return (true, "Routes configured.");
        }
        catch (Exception ex)
        {
            Logger.Error("SetRoutes exception", ex);
            return (false, $"Route setup failed: {ex.Message}");
        }
    }

    private (bool Success, string Message) RemoveRoutes(string serverIp)
    {
        try
        {
            RunRoute($"delete 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
            RunRoute($"delete 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
            RunRoute($"delete {serverIp} mask 255.255.255.255");
            Logger.Info("Routes removed");
            return (true, "Routes removed.");
        }
        catch (Exception ex)
        {
            Logger.Error("RemoveRoutes exception", ex);
            return (false, $"Route removal failed: {ex.Message}");
        }
    }

    private void SetDns()
    {
        try
        {
            RunNetsh($"interface ip set dns \"{TUN_ADAPTER_NAME}\" static {DNS_PRIMARY}");
            RunNetsh($"interface ip add dns \"{TUN_ADAPTER_NAME}\" {DNS_SECONDARY} index=2");
            Logger.Info($"DNS set to {DNS_PRIMARY}, {DNS_SECONDARY}");
        }
        catch (Exception ex)
        {
            Logger.Error("SetDns exception", ex);
        }
    }

    private void RestoreDns()
    {
        try
        {
            RunNetsh($"interface ip set dns \"{TUN_ADAPTER_NAME}\" dhcp");
        }
        catch { /* Best effort */ }
    }

    private static string? GetDefaultGateway()
    {
        try
        {
            var output = PaqetService.RunCommand("route", "print 0.0.0.0");
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("0.0.0.0") && trimmed.Contains("0.0.0.0"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && IPAddress.TryParse(parts[2], out _))
                        return parts[2];
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetDefaultGateway exception", ex);
        }
        return null;
    }

    private static string? GetDefaultInterfaceName()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    !ni.Name.Contains("Wintun", StringComparison.OrdinalIgnoreCase) &&
                    ni.GetIPProperties().GatewayAddresses.Count > 0)
                {
                    return ni.Name;
                }
            }
        }
        catch { }
        return null;
    }

    private static int GetInterfaceIndex(string name)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    var ipProps = ni.GetIPProperties().GetIPv4Properties();
                    return ipProps?.Index ?? -1;
                }
            }
        }
        catch { }
        return -1;
    }

    private static string RunNetsh(string arguments)
    {
        return PaqetService.RunCommand("netsh", arguments, timeout: 10000);
    }

    private static string RunRoute(string arguments)
    {
        return PaqetService.RunCommand("route", arguments, timeout: 5000);
    }
}
