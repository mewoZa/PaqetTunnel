using System;
using System.Diagnostics;
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

    // Saved state for restore on shutdown
    private bool _hadProxyBefore;
    private string? _savedProxyServer;
    private bool _weSetProxy;

    // ── Startup / Shutdown ────────────────────────────────────────

    /// <summary>
    /// Call on app startup. Saves current proxy state and cleans up stale settings
    /// from previous crashes or old versions (port 1080).
    /// </summary>
    public void OnStartup()
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

            // Clean stale portproxy rules that conflict with our SOCKS port
            try
            {
                var portproxy = PaqetService.RunCommand("netsh", "interface portproxy show v4tov4");
                if (portproxy.Contains($"{SOCKS_PORT}") || portproxy.Contains("1080"))
                {
                    Logger.Info("Startup: clearing stale portproxy rules");
                    try { PaqetService.RunCommand("netsh", $"interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={SOCKS_PORT}"); } catch { }
                    try { PaqetService.RunCommand("netsh", "interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=1080"); } catch { }
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

    public bool IsProxySharingEnabled()
    {
        try
        {
            var output = PaqetService.RunCommand("netsh", "interface portproxy show v4tov4");
            return output.Contains("0.0.0.0") && output.Contains($"{SOCKS_PORT}");
        }
        catch { return false; }
    }

    public (bool Success, string Message) SetProxySharing(bool enable)
    {
        try
        {
            if (enable)
            {
                PaqetService.RunElevated("netsh",
                    $"interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport={SOCKS_PORT} connectaddress=127.0.0.1 connectport={SOCKS_PORT}");
                PaqetService.RunElevated("netsh",
                    $"advfirewall firewall add rule name=\"Paqet SOCKS5 Sharing\" dir=in action=allow protocol=TCP localport={SOCKS_PORT} profile=any");
                return (true, $"Proxy sharing enabled on :{SOCKS_PORT}");
            }
            else
            {
                PaqetService.RunElevated("netsh",
                    $"interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={SOCKS_PORT}");
                PaqetService.RunElevated("netsh",
                    "advfirewall firewall delete rule name=\"Paqet SOCKS5 Sharing\"");
                return (true, "Proxy sharing disabled.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SetProxySharing({enable}) failed", ex);
            return (false, $"Sharing change failed: {ex.Message}");
        }
    }

    // ── Auto Start ────────────────────────────────────────────────

    public bool IsAutoStartEnabled()
    {
        try
        {
            var output = PaqetService.RunCommand("reg", $"query \"{AUTOSTART_KEY}\" /v {AUTOSTART_NAME}");
            return output.Contains(AUTOSTART_NAME);
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
                RunReg($"add \"{AUTOSTART_KEY}\" /v {AUTOSTART_NAME} /t REG_SZ /d \"\\\"{exePath}\\\"\" /f");
            }
            else
            {
                RunReg($"delete \"{AUTOSTART_KEY}\" /v {AUTOSTART_NAME} /f");
            }
            return (true, enable ? "Auto-start enabled." : "Auto-start disabled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"SetAutoStart({enable}) failed", ex);
            return (false, $"Auto-start change failed: {ex.Message}");
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
