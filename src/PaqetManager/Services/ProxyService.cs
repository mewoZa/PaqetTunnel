using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PaqetManager.Services;

/// <summary>
/// Manages Windows system proxy, port forwarding, firewall rules, and auto-start.
/// </summary>
public sealed class ProxyService
{
    private const string INTERNET_SETTINGS_KEY = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string AUTOSTART_KEY = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AUTOSTART_NAME = "PaqetManager";

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

    public (bool Success, string Message) SetSystemProxy(bool enable, string proxyAddress = "127.0.0.1:1080")
    {
        try
        {
            if (enable)
            {
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyEnable /t REG_DWORD /d 1 /f");
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyServer /t REG_SZ /d \"socks={proxyAddress}\" /f");
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyOverride /t REG_SZ /d \"localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.2?.*;172.30.*;172.31.*;192.168.*;<local>\" /f");
            }
            else
            {
                RunReg($"add \"{INTERNET_SETTINGS_KEY}\" /v ProxyEnable /t REG_DWORD /d 0 /f");
            }

            // Notify WinINet of the change
            NotifyProxyChange();
            return (true, enable ? "System proxy enabled." : "System proxy disabled.");
        }
        catch (Exception ex)
        {
            return (false, $"Proxy change failed: {ex.Message}");
        }
    }

    // ── Port Forwarding (for hotspot sharing) ─────────────────────

    public bool IsProxySharingEnabled()
    {
        try
        {
            var output = PaqetService.RunCommand("netsh", "interface portproxy show v4tov4");
            return output.Contains("0.0.0.0") && output.Contains("1080");
        }
        catch { return false; }
    }

    public (bool Success, string Message) SetProxySharing(bool enable)
    {
        try
        {
            if (enable)
            {
                // Add port forwarding rule (requires admin)
                PaqetService.RunElevated("netsh",
                    "interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=1080 connectaddress=127.0.0.1 connectport=1080");

                // Add firewall rule
                PaqetService.RunElevated("netsh",
                    "advfirewall firewall add rule name=\"Paqet SOCKS5 Sharing\" dir=in action=allow protocol=TCP localport=1080 profile=any");

                return (true, "Proxy sharing enabled. Other devices can use SOCKS5 on this IP:1080.");
            }
            else
            {
                PaqetService.RunElevated("netsh",
                    "interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=1080");

                PaqetService.RunElevated("netsh",
                    "advfirewall firewall delete rule name=\"Paqet SOCKS5 Sharing\"");

                return (true, "Proxy sharing disabled.");
            }
        }
        catch (Exception ex)
        {
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
            return (false, $"Auto-start change failed: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static void RunReg(string arguments)
    {
        PaqetService.RunCommand("reg", arguments);
    }

    /// <summary>
    /// Notify Internet Explorer / WinINet that proxy settings changed.
    /// This makes browsers pick up the new proxy immediately.
    /// </summary>
    private static void NotifyProxyChange()
    {
        try
        {
            const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
            const int INTERNET_OPTION_REFRESH = 37;
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { /* Best-effort */ }
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
