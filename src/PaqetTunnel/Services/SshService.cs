using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Renci.SshNet;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

/// <summary>
/// Manages server-side paqet installation via SSH.
/// Auto-routes through SOCKS5 proxy when tunnel is active (avoids routing loops).
/// Supports both key-based and password authentication.
/// </summary>
public sealed class SshService
{
    private const string SETUP_URL = "https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh";
    private const string SOCKS_HOST = "127.0.0.1";
    private const int SOCKS_PORT = 10800;

    private static bool IsSocksRunning()
    {
        try
        {
            var props = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            foreach (var ep in props.GetActiveTcpListeners())
            {
                if (ep.Port == SOCKS_PORT && IPAddress.IsLoopback(ep.Address))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static ConnectionInfo BuildConnection(AppSettings settings)
    {
        var host = settings.ServerSshHost;
        var port = settings.ServerSshPort > 0 ? settings.ServerSshPort : 22;
        var user = string.IsNullOrEmpty(settings.ServerSshUser) ? "root" : settings.ServerSshUser;

        AuthenticationMethod auth;
        if (!string.IsNullOrEmpty(settings.ServerSshKeyPath) && File.Exists(settings.ServerSshKeyPath))
        {
            var keyFile = new PrivateKeyFile(settings.ServerSshKeyPath);
            auth = new PrivateKeyAuthenticationMethod(user, keyFile);
            Logger.Info($"SSH: key auth ({settings.ServerSshKeyPath})");
        }
        else if (!string.IsNullOrEmpty(settings.ServerSshPassword))
        {
            auth = new PasswordAuthenticationMethod(user, settings.ServerSshPassword);
            Logger.Info("SSH: password auth");
        }
        else
        {
            throw new InvalidOperationException("No SSH key or password configured. Set ServerSshKeyPath or ServerSshPassword.");
        }

        // Route through SOCKS5 proxy when tunnel is active to avoid routing loops
        var useSocks = IsSocksRunning();
        Logger.Info($"SSH: target={host}:{port} user={user} proxy={useSocks}");

        ConnectionInfo connInfo;
        if (useSocks)
        {
            connInfo = new ConnectionInfo(host, port, user,
                ProxyTypes.Socks5, SOCKS_HOST, SOCKS_PORT, string.Empty, string.Empty,
                auth);
        }
        else
        {
            connInfo = new ConnectionInfo(host, port, user, auth);
        }

        connInfo.Timeout = TimeSpan.FromSeconds(30);
        return connInfo;
    }

    private static SshClient CreateClient(AppSettings settings)
    {
        var connInfo = BuildConnection(settings);
        var client = new SshClient(connInfo);
        client.HostKeyReceived += (_, e) => e.CanTrust = true;
        return client;
    }

    /// <summary>
    /// Run a server management command via SSH.
    /// Valid commands: status, install, update, uninstall, logs, restart
    /// </summary>
    public async Task<(bool Success, string Output)> RunServerCommandAsync(
        AppSettings settings, string command, Action<string>? onProgress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(settings.ServerSshHost))
                    return (false, "Server SSH host not configured.\nSet in Settings > Server Management or settings.json.");

                onProgress?.Invoke($"Connecting to {settings.ServerSshUser}@{settings.ServerSshHost}...");

                using var client = CreateClient(settings);
                client.Connect();

                if (!client.IsConnected)
                    return (false, "SSH connection failed — check host, port, and credentials.");

                onProgress?.Invoke($"Connected. Running '{command}'...");

                string remoteCmd;
                switch (command.ToLower())
                {
                    case "status":
                        remoteCmd = "bash -c '"
                            + "echo \"=== Paqet Server Status ===\"; "
                            + "if [ -f /opt/paqet/paqet ]; then echo \"Binary: /opt/paqet/paqet\"; /opt/paqet/paqet --version 2>/dev/null || true; "
                            + "elif command -v paqet >/dev/null 2>&1; then echo \"Binary: $(which paqet)\"; paqet --version 2>/dev/null || true; "
                            + "else echo \"Binary: not found\"; fi; "
                            + "echo; "
                            + "if [ -f /etc/paqet/server.yaml ]; then echo \"Config: /etc/paqet/server.yaml\"; else echo \"Config: not found\"; fi; "
                            + "echo; "
                            + "systemctl is-active paqet 2>/dev/null && systemctl status paqet --no-pager -l 2>/dev/null | head -15 || echo \"Service: not running\";"
                            + "echo; "
                            + "if [ -f /opt/paqet/.version ]; then echo \"Version: $(cat /opt/paqet/.version)\"; fi"
                            + "'";
                        break;
                    case "install":
                        onProgress?.Invoke("Downloading and installing paqet server...");
                        remoteCmd = $"curl -fsSL {SETUP_URL} -o /tmp/paqet-setup.sh && bash /tmp/paqet-setup.sh install --yes";
                        break;
                    case "update":
                        onProgress?.Invoke("Updating paqet server...");
                        remoteCmd = $"curl -fsSL {SETUP_URL} -o /tmp/paqet-setup.sh && bash /tmp/paqet-setup.sh update --yes";
                        break;
                    case "uninstall":
                        onProgress?.Invoke("Uninstalling paqet server...");
                        remoteCmd = "bash -c '"
                            + "if [ -f /opt/paqet/setup.sh ]; then bash /opt/paqet/setup.sh uninstall --yes; "
                            + "else systemctl stop paqet 2>/dev/null; systemctl disable paqet 2>/dev/null; "
                            + "rm -f /etc/systemd/system/paqet.service; systemctl daemon-reload 2>/dev/null; "
                            + "rm -rf /opt/paqet /etc/paqet /usr/local/bin/paqet; echo \"Paqet uninstalled\"; fi"
                            + "'";
                        break;
                    case "restart":
                        // Background the restart so SSH can disconnect before paqet stops
                        remoteCmd = "nohup bash -c 'sleep 1 && systemctl restart paqet' >/dev/null 2>&1 & echo 'Restart scheduled (1s delay)'";
                        break;
                    case "logs":
                        remoteCmd = "journalctl -u paqet --no-pager -n 50";
                        break;
                    default:
                        return (false, $"Unknown server command: {command}\nValid: status, install, update, uninstall, restart, logs");
                }

                Logger.Info($"SSH: exec '{command}' on {settings.ServerSshHost}");
                var cmd = client.RunCommand(remoteCmd);

                var output = cmd.Result ?? "";
                if (!string.IsNullOrEmpty(cmd.Error))
                    output = (output + "\n" + cmd.Error).Trim();

                client.Disconnect();

                var success = cmd.ExitStatus == 0;
                Logger.Info($"SSH: '{command}' exit={cmd.ExitStatus}");
                return (success, output.Trim());
            }
            catch (Renci.SshNet.Common.SshAuthenticationException ex)
            {
                Logger.Error($"SSH auth failed for '{command}'", ex);
                return (false, $"Authentication failed: {ex.Message}\nCheck SSH key or password.");
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                Logger.Error($"SSH connection failed for '{command}'", ex);
                return (false, $"Connection failed: {ex.Message}\nCheck host, port, and network.");
            }
            catch (SocketException ex)
            {
                Logger.Error($"SSH socket error for '{command}'", ex);
                return (false, $"Network error: {ex.Message}\nCheck host address and port.");
            }
            catch (Exception ex)
            {
                Logger.Error($"SSH command '{command}' failed", ex);
                return (false, $"Error: {ex.Message}");
            }
        });
    }

    /// <summary>Test SSH connectivity and return server info.</summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(AppSettings settings)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(settings.ServerSshHost))
                    return (false, "Host not configured");

                using var client = CreateClient(settings);
                client.Connect();

                if (!client.IsConnected)
                    return (false, "Connection failed");

                var cmd = client.RunCommand("echo ok && uname -snr && uptime -p 2>/dev/null || uptime");
                client.Disconnect();

                return (true, $"Connected\n{cmd.Result.Trim()}");
            }
            catch (Renci.SshNet.Common.SshAuthenticationException ex)
            {
                return (false, $"Auth failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        });
    }
}
