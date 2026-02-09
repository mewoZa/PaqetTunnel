using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PaqetTunnel.Services;

/// <summary>
/// Manages Windows system proxy, port forwarding, firewall rules, and auto-start.
/// Saves and restores original proxy settings on app lifecycle.
/// </summary>
public sealed class ProxyService
{
    private const string INTERNET_SETTINGS_KEY = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string AUTOSTART_KEY = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AUTOSTART_NAME = "PaqetTunnel";
    private const int SOCKS_PORT = PaqetService.SOCKS_PORT;
    // LAN sharing uses a separate port to avoid portproxy conflict with paqet's SOCKS5 binding.
    // portproxy on the same port causes iphlpsvc to steal the port from paqet.
    public const int SHARING_PORT = SOCKS_PORT + 1;

    // Saved state for restore on shutdown
    private bool _hadProxyBefore;
    private string? _savedProxyServer;
    private bool _weSetProxy;

    // ── Startup / Shutdown ────────────────────────────────────────

    /// <summary>
    /// Call on app startup. Saves current proxy state and cleans up stale settings
    /// from previous crashes or old versions (port 1080).
    /// Pass saved settings so we preserve intentional portproxy rules.
    /// </summary>
    public void OnStartup(bool proxySharingWasEnabled = false)
    {
        try
        {
            _hadProxyBefore = IsSystemProxyEnabled();
            _savedProxyServer = GetCurrentProxyServer();
            _weSetProxy = false;

            // Clean stale proxy from old port or crashed session
            if (_hadProxyBefore && _savedProxyServer != null)
            {
                if (_savedProxyServer.Contains(":1080") || _savedProxyServer.Contains($":{SOCKS_PORT}"))
                {
                    Logger.Info($"Startup: clearing stale proxy setting: {_savedProxyServer}");
                    SetSystemProxy(false);
                    _hadProxyBefore = false;
                    _savedProxyServer = null;
                }
            }

            // Clean stale WinHTTP proxy
            try
            {
                var winhttp = PaqetService.RunCommand("netsh", "winhttp show proxy");
                if (winhttp.Contains(":1080") || winhttp.Contains($":{SOCKS_PORT}") || winhttp.Contains(":10808"))
                {
                    Logger.Info("Startup: clearing stale WinHTTP proxy");
                    PaqetService.RunCommand("netsh", "winhttp reset proxy");
                }
            }
            catch (Exception ex) { Logger.Debug($"WinHTTP check: {ex.Message}"); }

            // Clean stale portproxy rules — but preserve them if user had sharing enabled
            try
            {
                var portproxy = PaqetService.RunCommand("netsh", "interface portproxy show v4tov4");
                // Always clean old port 1080 rules
                if (portproxy.Contains("1080") && !portproxy.Contains("10800"))
                {
                    Logger.Info("Startup: clearing stale port 1080 portproxy rules");
                    try { PaqetService.RunCommand("netsh", "interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=1080"); } catch { }
                }
                // Always clean legacy same-port rules (caused iphlpsvc conflict)
                if (portproxy.Contains($"0.0.0.0") && portproxy.Contains($"{SOCKS_PORT}"))
                {
                    // Check if it's the old same-port rule (not the new sharing port)
                    var lines = portproxy.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("0.0.0.0") && line.Contains($"{SOCKS_PORT}") && !line.Contains($"{SHARING_PORT}"))
                        {
                            Logger.Info("Startup: removing legacy same-port portproxy rule (prevents port conflict)");
                            try { PaqetService.RunCommand("netsh", $"interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={SOCKS_PORT}"); } catch { }
                            break;
                        }
                    }
                }
                // Only clean sharing port rules if sharing was NOT intentionally enabled
                if (!proxySharingWasEnabled && portproxy.Contains($"{SHARING_PORT}"))
                {
                    Logger.Info("Startup: clearing stale sharing portproxy rules (sharing was not enabled)");
                    try { PaqetService.RunCommand("netsh", $"interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={SHARING_PORT}"); } catch { }
                }
                else if (proxySharingWasEnabled)
                {
                    Logger.Info("Startup: preserving sharing portproxy rules (sharing was enabled in settings)");
                }
            }
            catch (Exception ex) { Logger.Debug($"Portproxy cleanup: {ex.Message}"); }

            // Reserve SOCKS port to prevent ICS/SharedAccess from stealing it
            try
            {
                PaqetService.RunCommand("netsh", $"int ipv4 add excludedportrange protocol=tcp startport={SOCKS_PORT} numberofports=1");
            }
            catch { } // May already be reserved or in use — that's OK
            try
            {
                PaqetService.RunCommand("netsh", $"int ipv4 add excludedportrange protocol=udp startport={SOCKS_PORT} numberofports=1");
            }
            catch { }

            Logger.Info($"ProxyService.OnStartup: hadProxy={_hadProxyBefore}, saved={_savedProxyServer}");
        }
        catch (Exception ex)
        {
            Logger.Error("ProxyService.OnStartup failed", ex);
        }
    }

    /// <summary>
    /// Call on app shutdown. Restores original proxy settings if we changed them.
    /// </summary>
    public void OnShutdown()
    {
        try
        {
            if (_weSetProxy)
            {
                Logger.Info("Shutdown: restoring proxy settings (we set them)");
                if (_hadProxyBefore && _savedProxyServer != null)
                {
                    RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyEnable /t REG_DWORD /d 1 /f");
                    RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyServer /t REG_SZ /d \"{_savedProxyServer}\" /f");
                }
                else
                {
                    RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyEnable /t REG_DWORD /d 0 /f");
                }
                NotifyProxyChange();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ProxyService.OnShutdown failed", ex);
        }
    }

    // ── System Proxy ──────────────────────────────────────────────

    public bool IsSystemProxyEnabled()
    {
        try
        {
            var output = PaqetService.RunCommand("reg", $"query \"{INTERNET_SETTINGS_KEY}\" /v ProxyEnable");
            return output.Contains("0x1");
        }
        catch { return false; }
    }

    public string? GetCurrentProxyServer()
    {
        try
        {
            var output = PaqetService.RunCommand("reg", $"query \"{INTERNET_SETTINGS_KEY}\" /v ProxyServer");
            var idx = output.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return output[(idx + 6)..].Trim();
        }
        catch { }
        return null;
    }

    public (bool Success, string Message) SetSystemProxy(bool enable)
    {
        try
        {
            if (enable)
            {
                var addr = $"127.0.0.1:{SOCKS_PORT}";
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyEnable /t REG_DWORD /d 1 /f");
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyServer /t REG_SZ /d \"socks={addr}\" /f");
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyOverride /t REG_SZ /d \"localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.2?.*;172.30.*;172.31.*;192.168.*;<local>\" /f");
                _weSetProxy = true;
            }
            else
            {
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyEnable /t REG_DWORD /d 0 /f");
                // Clear proxy server value to prevent stale entries confusing WinHTTP/services
                try { RunReg($"delete \"{INTERNET_SETTINGS_KEY}\" /v ProxyServer /f"); } catch { }
                try { RunReg($"delete \"{INTERNET_SETTINGS_KEY}\" /v ProxyOverride /f"); } catch { }
                if (_weSetProxy) _weSetProxy = false;
            }

            NotifyProxyChange();
            Logger.Info($"SetSystemProxy({enable}): success");
            return (true, enable ? $"System proxy enabled (SOCKS5 :{SOCKS_PORT})" : "System proxy disabled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"SetSystemProxy({enable}) failed", ex);
            return (false, $"Proxy change failed: {ex.Message}");
        }
    }

    // ── Port Forwarding (for hotspot sharing) ─────────────────────

    /// <summary>
    /// Checks if portproxy rule actually exists (survives reboots? NO — portproxy is volatile).
    /// </summary>
    public bool IsPortproxyActive()
    {
        try
        {
            var output = PaqetService.RunCommand("netsh", "interface portproxy show v4tov4");
            return output.Contains("0.0.0.0") && output.Contains($"{SHARING_PORT}");
        }
        catch { return false; }
    }

    /// <summary>
    /// Re-creates portproxy + firewall rules if sharing was enabled in settings.
    /// Call after paqet starts and port is listening. Portproxy rules are volatile
    /// (cleared on reboot), so they must be re-created every app launch.
    /// </summary>
    public void RestoreSharingIfEnabled()
    {
        try
        {
            if (IsPortproxyActive())
            {
                Logger.Debug("Sharing portproxy already active, skipping restore");
                return;
            }

            Logger.Info("Restoring LAN sharing (portproxy rule was lost after reboot)...");
            var result = SetProxySharing(true);
            Logger.Info($"Sharing restore: {result.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to restore sharing", ex);
        }
    }

    public (bool Success, string Message) SetProxySharing(bool enable)
    {
        try
        {
            // Clean up legacy rules that used the same port as paqet (caused iphlpsvc conflict)
            try
            {
                var existing = PaqetService.RunCommand("netsh", "interface portproxy show v4tov4");
                if (existing.Contains($"0.0.0.0") && existing.Contains($"{SOCKS_PORT}") && !existing.Contains($"{SHARING_PORT}"))
                {
                    Logger.Info("Removing legacy portproxy rule on same port as SOCKS5");
                    PaqetService.RunAdmin("netsh",
                        $"interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={SOCKS_PORT}");
                }
            }
            catch { }

            if (enable)
            {
                // Delete existing rule first to avoid duplicates
                try { PaqetService.RunAdmin("netsh",
                    $"interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={SHARING_PORT}"); } catch { }

                PaqetService.RunAdmin("netsh",
                    $"interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport={SHARING_PORT} connectaddress=127.0.0.1 connectport={SOCKS_PORT}");

                // Delete existing firewall rules first to avoid accumulation
                try { PaqetService.RunAdmin("netsh",
                    "advfirewall firewall delete rule name=\"Paqet SOCKS5 Sharing\""); } catch { }

                PaqetService.RunAdmin("netsh",
                    $"advfirewall firewall add rule name=\"Paqet SOCKS5 Sharing\" dir=in action=allow protocol=TCP localport={SHARING_PORT} profile=any");

                Logger.Info($"LAN sharing enabled: 0.0.0.0:{SHARING_PORT} → 127.0.0.1:{SOCKS_PORT}");
                return (true, $"LAN sharing enabled on :{SHARING_PORT} → :{SOCKS_PORT}");
            }
            else
            {
                try { PaqetService.RunAdmin("netsh",
                    $"interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={SHARING_PORT}"); } catch { }
                try { PaqetService.RunAdmin("netsh",
                    "advfirewall firewall delete rule name=\"Paqet SOCKS5 Sharing\""); } catch { }
                Logger.Info("LAN sharing disabled");
                return (true, "LAN sharing disabled.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SetProxySharing({enable}) failed", ex);
            return (false, $"Sharing change failed: {ex.Message}");
        }
    }

    // ── Auto Start (Scheduled Task with Highest RunLevel) ────────
    // Uses Task Scheduler instead of HKCU\Run so the elevated app
    // can start at logon without a UAC prompt.

    private const string TASK_NAME = "PaqetTunnel";

    public bool IsAutoStartEnabled()
    {
        try
        {
            var output = PaqetService.RunCommand("schtasks", $"/query /tn \"{TASK_NAME}\" /fo CSV /nh");
            return output.Contains(TASK_NAME);
        }
        catch { return false; }
    }

    public (bool Success, string Message) SetAutoStart(bool enable)
    {
        try
        {
            if (enable)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath))
                    return (false, "Cannot determine executable path.");

                var user = Environment.UserName;

                // Remove old registry Run entry if present (legacy cleanup)
                try { RunReg($"delete \"{AUTOSTART_KEY}\" /v {AUTOSTART_NAME} /f"); } catch { }

                // Use PowerShell to create the scheduled task (handles spaces in paths correctly)
                var psScript =
                    $"$a = New-ScheduledTaskAction -Execute '{exePath}';" +
                    $"$t = New-ScheduledTaskTrigger -AtLogOn -User '{user}';" +
                    $"$p = New-ScheduledTaskPrincipal -UserId '{user}' -LogonType Interactive -RunLevel Highest;" +
                    $"$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds 0);" +
                    $"Register-ScheduledTask -TaskName '{TASK_NAME}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
                PaqetService.RunCommand("powershell", $"-NoProfile -Command \"{psScript}\"");
            }
            else
            {
                PaqetService.RunCommand("powershell",
                    $"-NoProfile -Command \"Unregister-ScheduledTask -TaskName '{TASK_NAME}' -Confirm:$false -ErrorAction SilentlyContinue\"");
                // Also clean legacy registry entry
                try { RunReg($"delete \"{AUTOSTART_KEY}\" /v {AUTOSTART_NAME} /f"); } catch { }
            }
            return (true, enable ? "Auto-start enabled." : "Auto-start disabled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"SetAutoStart({enable}) failed", ex);
            return (false, $"Auto-start change failed: {ex.Message}");
        }
    }

    // ── Start Before Logon (SYSTEM-level Scheduled Task at boot) ──
    // Runs the paqet tunnel binary as SYSTEM at system startup, before
    // any user logs in. This provides VPN connectivity from boot.

    private const string BOOT_TASK_NAME = "PaqetTunnelService";

    public bool IsStartBeforeLogonEnabled()
    {
        try
        {
            var output = PaqetService.RunCommand("schtasks", $"/query /tn \"{BOOT_TASK_NAME}\" /fo CSV /nh");
            return output.Contains(BOOT_TASK_NAME);
        }
        catch { return false; }
    }

    public (bool Success, string Message) SetStartBeforeLogon(bool enable)
    {
        try
        {
            if (enable)
            {
                var binaryPath = AppPaths.BinaryPath;
                if (!File.Exists(binaryPath))
                    return (false, "Paqet binary not found. Run setup first.");

                var configPath = AppPaths.PaqetConfigPath;
                if (!File.Exists(configPath))
                    return (false, "Paqet config not found. Connect at least once first.");

                var psScript =
                    $"$a = New-ScheduledTaskAction -Execute '{binaryPath}' -Argument 'run --config \"\"{configPath}\"\"' -WorkingDirectory '{AppPaths.BinDir}';" +
                    $"$t = New-ScheduledTaskTrigger -AtStartup;" +
                    $"$p = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest;" +
                    $"$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds 0) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1);" +
                    $"Register-ScheduledTask -TaskName '{BOOT_TASK_NAME}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
                PaqetService.RunCommand("powershell", $"-NoProfile -Command \"{psScript}\"");
            }
            else
            {
                PaqetService.RunCommand("powershell",
                    $"-NoProfile -Command \"Unregister-ScheduledTask -TaskName '{BOOT_TASK_NAME}' -Confirm:$false -ErrorAction SilentlyContinue\"");
            }
            return (true, enable ? "Boot-level tunnel enabled." : "Boot-level tunnel disabled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"SetStartBeforeLogon({enable}) failed", ex);
            return (false, $"Boot-level tunnel change failed: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static void RunReg(string arguments)
    {
        PaqetService.RunCommand("reg", arguments);
    }

    private static void NotifyProxyChange()
    {
        try
        {
            const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
            const int INTERNET_OPTION_REFRESH = 37;
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
