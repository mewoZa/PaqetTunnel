using System;
using System.IO;
using System.Threading.Tasks;
using Renci.SshNet;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

/// <summary>
/// Manages server-side paqet installation via SSH.
/// Supports both key-based and password authentication.
/// Runs setup.sh commands: install, update, uninstall, status, logs, restart.
/// </summary>
public sealed class SshService
{
    private const string SETUP_URL = "https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.sh";

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
            Logger.Info($"SSH: using key auth ({settings.ServerSshKeyPath})");
        }
        else if (!string.IsNullOrEmpty(settings.ServerSshPassword))
        {
            auth = new PasswordAuthenticationMethod(user, settings.ServerSshPassword);
            Logger.Info("SSH: using password auth");
        }
        else
        {
            throw new InvalidOperationException("No SSH key or password configured");
        }

        return new ConnectionInfo(host, port, user, auth)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
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
                    return (false, "Server SSH host not configured");

                var connInfo = BuildConnection(settings);
                onProgress?.Invoke($"Connecting to {settings.ServerSshHost}...");

                using var client = new SshClient(connInfo);
                client.HostKeyReceived += (_, e) => e.CanTrust = true;
                client.Connect();

                if (!client.IsConnected)
                    return (false, "Failed to connect");

                onProgress?.Invoke("Connected. Running command...");

                string remoteCmd;
                switch (command.ToLower())
                {
                    case "status":
                        remoteCmd = "bash -c 'if [ -f /opt/paqet/setup.sh ]; then bash /opt/paqet/setup.sh status; else echo \"paqet not installed\"; fi'";
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
                        remoteCmd = "bash -c 'if [ -f /opt/paqet/setup.sh ]; then bash /opt/paqet/setup.sh uninstall --yes; else echo \"paqet not installed\"; fi'";
                        break;
                    case "restart":
                        remoteCmd = "systemctl restart paqet";
                        break;
                    case "logs":
                        remoteCmd = "journalctl -u paqet --no-pager -n 50";
                        break;
                    default:
                        return (false, $"Unknown server command: {command}");
                }

                Logger.Info($"SSH: executing '{command}' on {settings.ServerSshHost}");
                var cmd = client.RunCommand(remoteCmd);

                var output = cmd.Result;
                if (!string.IsNullOrEmpty(cmd.Error))
                    output += "\n" + cmd.Error;

                client.Disconnect();

                var success = cmd.ExitStatus == 0;
                Logger.Info($"SSH: '{command}' exit={cmd.ExitStatus}");
                return (success, output.Trim());
            }
            catch (Exception ex)
            {
                Logger.Error($"SSH command '{command}' failed", ex);
                return (false, ex.Message);
            }
        });
    }

    /// <summary>Test SSH connectivity.</summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(AppSettings settings)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(settings.ServerSshHost))
                    return (false, "Host not configured");

                var connInfo = BuildConnection(settings);
                using var client = new SshClient(connInfo);
                client.HostKeyReceived += (_, e) => e.CanTrust = true;
                client.Connect();

                if (!client.IsConnected)
                    return (false, "Connection failed");

                var cmd = client.RunCommand("echo ok && uname -r");
                client.Disconnect();

                return (true, $"Connected — {cmd.Result.Trim()}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }
}
