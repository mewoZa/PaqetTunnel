using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

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
    private const int TUN_METRIC = 1;

    private Process? _tun2socksProcess;
    private readonly object _tunProcessLock = new(); // NEW-06: synchronize _tun2socksProcess access
    private string? _originalGateway;
    private string? _originalInterface;
    private string? _serverIp;
    private string _dnsPrimary = "1.1.1.1";
    private string _dnsSecondary = "1.0.0.1";
    private List<(string Name, string? OriginalDns)> _changedAdapters = new();

    public bool Tun2SocksExists() => File.Exists(AppPaths.Tun2SocksPath);
    public bool WintunExists() => File.Exists(AppPaths.WintunDllPath);
    public bool AllBinariesExist() => Tun2SocksExists() && WintunExists();

    /// <summary>Check if the tun2socks process is running.</summary>
    public bool IsRunning()
    {
        lock (_tunProcessLock) // NEW-06 fix
        {
            if (_tun2socksProcess != null && !_tun2socksProcess.HasExited)
                return true;
        }

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
    /// Start the full system tunnel. Requires paqet SOCKS5 to be running on port 10800.
    /// Steps: 1) Start tun2socks  2) Configure TUN adapter IP  3) Set routes  4) Set DNS
    /// </summary>
    public async Task<(bool Success, string Message)> StartAsync(string paqetServerIp, AppSettings? settings = null)
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

        // Resolve DNS servers from settings
        if (settings != null)
        {
            var (p, s) = DnsService.Resolve(settings);
            _dnsPrimary = p;
            _dnsSecondary = s;
        }
        Logger.Info($"DNS servers: {_dnsPrimary}, {_dnsSecondary}");

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
            _changedAdapters.Clear();

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
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetTunnel/1.0");
            http.Timeout = TimeSpan.FromSeconds(120);

            // Download tun2socks
            if (!Tun2SocksExists())
            {
                progress?.Report("Downloading tun2socks...");
                // R2-23 fix: version constants centralized for easy updates
                const string tun2socksVersion = "v2.6.0";
                var tun2socksUrl = $"https://github.com/xjasonlyu/tun2socks/releases/download/{tun2socksVersion}/tun2socks-windows-amd64.zip";
                Logger.Info($"Downloading tun2socks {tun2socksVersion} from {tun2socksUrl}");

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
                // R2-23 fix: version constant centralized for easy updates
                const string wintunVersion = "0.14.1";
                var wintunUrl = $"https://www.wintun.net/builds/wintun-{wintunVersion}.zip";
                Logger.Info($"Downloading wintun v{wintunVersion} from {wintunUrl}");

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

            var proc = Process.Start(psi);
            if (proc == null)
                return (false, "Failed to start tun2socks.");

            // Async read to prevent buffer deadlock
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) Logger.Debug($"[tun2socks stdout] {e.Data}");
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) Logger.Debug($"[tun2socks stderr] {e.Data}");
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // NEW-09: allow thread pool to process other work during wait
            Task.Delay(1000).GetAwaiter().GetResult();

            if (proc.HasExited)
            {
                Logger.Error($"tun2socks exited with code {proc.ExitCode}");
                return (false, $"tun2socks exited immediately (code {proc.ExitCode}). Needs admin privileges.");
            }

            lock (_tunProcessLock) { _tun2socksProcess = proc; } // NEW-06 fix

            Logger.Info($"tun2socks started PID {proc.Id}");
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
            lock (_tunProcessLock) // NEW-06 fix
            {
                if (_tun2socksProcess != null && !_tun2socksProcess.HasExited)
                {
                    Logger.Info($"Killing tun2socks PID {_tun2socksProcess.Id}");
                    _tun2socksProcess.Kill(entireProcessTree: true);
                    _tun2socksProcess.WaitForExit(3000);
                    _tun2socksProcess.Dispose();
                }
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
            // Set IP address on TUN adapter — do NOT set gateway here, because netsh
            // auto-adds a persistent 0.0.0.0/0 route that conflicts with split routing
            // and persists after adapter removal, causing stale routes on reboot.
            var output = RunNetsh($"interface ip set address \"{TUN_ADAPTER_NAME}\" static {TUN_IP} {TUN_SUBNET}");
            Logger.Debug($"netsh set address: {output}");

            // Disable IPv6 on TUN adapter — browsers prefer IPv6 and our tunnel only handles IPv4,
            // causing connections to hang when IPv6 is enabled but has no routes through the tunnel.
            try
            {
                RunPowerShell($"Disable-NetAdapterBinding -Name '{TUN_ADAPTER_NAME}' -ComponentId ms_tcpip6");
                Logger.Info("Disabled IPv6 on TUN adapter");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not disable IPv6 on TUN: {ex.Message}");
            }

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
        var addedRoutes = new List<string>(); // BUG-16 fix: track routes for rollback
        try
        {
            // Clean up any stale persistent default route from previous crashed TUN sessions.
            // Windows auto-adds persistent 0.0.0.0/0 via TUN gateway when netsh sets a gateway,
            // which survives adapter removal and causes routing failures on next startup.
            try { RunRoute($"delete 0.0.0.0 mask 0.0.0.0 {TUN_GATEWAY}"); } catch { }
            try { PaqetService.RunCommand("route", $"-p delete 0.0.0.0 mask 0.0.0.0 {TUN_GATEWAY}", timeout: 5000); } catch { }

            // NEW-01 fix: route VPN server IP directly through original gateway to prevent routing loop
            if (!string.IsNullOrEmpty(serverIp) && !string.IsNullOrEmpty(_originalGateway))
            {
                RunRoute($"add {serverIp} mask 255.255.255.255 {_originalGateway} metric 1");
                addedRoutes.Add($"delete {serverIp} mask 255.255.255.255 {_originalGateway}");
                Logger.Info($"Added direct route for VPN server {serverIp} via {_originalGateway}");
            }

            if (!string.IsNullOrEmpty(_originalGateway))
            {
                var lanRoutes = new[]
                {
                    $"add 10.0.0.0 mask 255.0.0.0 {_originalGateway} metric 5",
                    $"add 172.16.0.0 mask 255.240.0.0 {_originalGateway} metric 5",
                    $"add 192.168.0.0 mask 255.255.0.0 {_originalGateway} metric 5",
                    $"add 169.254.0.0 mask 255.255.0.0 {_originalGateway} metric 5",
                };
                foreach (var route in lanRoutes)
                {
                    RunRoute(route);
                    addedRoutes.Add(route.Replace("add ", "delete "));
                }
                Logger.Info("Added LAN exclusion routes (10/8, 172.16/12, 192.168/16, 169.254/16)");
            }

            // Get TUN interface index
            var tunIndex = GetInterfaceIndex(TUN_ADAPTER_NAME);
            if (tunIndex <= 0)
            {
                Logger.Warn("Could not find TUN interface index, using gateway-based routing");
                RunRoute($"add 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC}");
                addedRoutes.Add($"delete 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
                RunRoute($"add 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC}");
                addedRoutes.Add($"delete 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
            }
            else
            {
                Logger.Info($"TUN interface index: {tunIndex}");
                RunRoute($"add 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC} if {tunIndex}");
                addedRoutes.Add($"delete 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
                RunRoute($"add 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY} metric {TUN_METRIC} if {tunIndex}");
                addedRoutes.Add($"delete 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
            }

            Logger.Info("Routes set for full system tunnel");
            return (true, "Routes configured.");
        }
        catch (Exception ex)
        {
            // BUG-16 fix: rollback partially added routes on failure
            Logger.Error("SetRoutes exception — rolling back", ex);
            foreach (var rollback in addedRoutes)
            {
                try { RunRoute(rollback); } catch { }
            }
            return (false, $"Route setup failed: {ex.Message}");
        }
    }

    private (bool Success, string Message) RemoveRoutes(string serverIp)
    {
        try
        {
            // NEW-02 fix: remove VPN server direct route
            if (!string.IsNullOrEmpty(serverIp) && !string.IsNullOrEmpty(_originalGateway))
            {
                try { RunRoute($"delete {serverIp} mask 255.255.255.255 {_originalGateway}"); } catch { }
                Logger.Info($"Removed direct route for VPN server {serverIp}");
            }

            RunRoute($"delete 0.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
            RunRoute($"delete 128.0.0.0 mask 128.0.0.0 {TUN_GATEWAY}");
            // Also clean any stale persistent default route
            try { RunRoute($"delete 0.0.0.0 mask 0.0.0.0 {TUN_GATEWAY}"); } catch { }
            try { PaqetService.RunCommand("route", $"-p delete 0.0.0.0 mask 0.0.0.0 {TUN_GATEWAY}", timeout: 5000); } catch { }
            // Remove LAN exclusion routes
            if (!string.IsNullOrEmpty(_originalGateway))
            {
                RunRoute($"delete 10.0.0.0 mask 255.0.0.0 {_originalGateway}");
                RunRoute($"delete 172.16.0.0 mask 255.240.0.0 {_originalGateway}");
                RunRoute($"delete 192.168.0.0 mask 255.255.0.0 {_originalGateway}");
                RunRoute($"delete 169.254.0.0 mask 255.255.0.0 {_originalGateway}");
            }
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
            // Set DNS on TUN adapter
            DnsService.ForceAdapterDns(TUN_ADAPTER_NAME, _dnsPrimary, _dnsSecondary);

            // Flush DNS cache so stale entries from ISP DNS don't leak
            DnsService.FlushCache();

            // DNS leak prevention: force DNS on ALL active adapters (not just physical)
            // This prevents any app from using ISP DNS regardless of which adapter it uses
            _changedAdapters = DnsService.ForceAllAdaptersDns(_dnsPrimary, _dnsSecondary, excludeAdapter: TUN_ADAPTER_NAME);
            Logger.Info($"DNS leak prevention: forced DNS on {_changedAdapters.Count} adapters");
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
            // Restore TUN adapter DNS
            try { RunNetsh($"interface ip set dns \"{TUN_ADAPTER_NAME}\" dhcp"); } catch { }

            // Restore ALL adapters we changed
            foreach (var (name, originalDns) in _changedAdapters)
            {
                DnsService.RestoreAdapterDns(name, originalDns);
            }
            _changedAdapters.Clear();

            // Flush DNS cache to clear tunnel DNS entries
            DnsService.FlushCache();
        }
        catch { /* Best effort */ }
    }

    /// <summary>Flush the Windows DNS resolver cache.</summary>
    public static void FlushDnsCache() => DnsService.FlushCache();

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

    private static string RunPowerShell(string command)
    {
        return PaqetService.RunCommand("powershell", $"-NoProfile -Command \"{command}\"", timeout: 15000);
    }
}
