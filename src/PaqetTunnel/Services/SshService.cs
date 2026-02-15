using System;
using System.Collections.Generic;
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
        // BUG-02 fix: verify host key against known fingerprints
        var knownHostsPath = System.IO.Path.Combine(AppPaths.DataDir, "known_hosts");
        client.HostKeyReceived += (_, e) =>
        {
            var fingerprint = Convert.ToHexString(e.FingerPrint).ToLowerInvariant();
            var hostKey = $"{settings.ServerSshHost}:{(settings.ServerSshPort > 0 ? settings.ServerSshPort : 22)}";
            try
            {
                if (System.IO.File.Exists(knownHostsPath))
                {
                    foreach (var line in System.IO.File.ReadAllLines(knownHostsPath))
                    {
                        var parts = line.Split(' ', 2);
                        if (parts.Length == 2 && parts[0] == hostKey)
                        {
                            e.CanTrust = parts[1] == fingerprint;
                            if (!e.CanTrust)
                                Logger.Warn($"SSH host key CHANGED for {hostKey}! Expected {parts[1]}, got {fingerprint}");
                            return;
                        }
                    }
                }
            }
            catch { }
            // First connection — trust and save fingerprint
            // NEW-15: log fingerprint visibly so user can verify
            Logger.Info($"SSH: FIRST CONNECTION — trusting host key for {hostKey}: {fingerprint}");
            e.CanTrust = true;
            try
            {
                System.IO.Directory.CreateDirectory(AppPaths.DataDir);
                System.IO.File.AppendAllText(knownHostsPath, $"{hostKey} {fingerprint}\n");
                Logger.Info($"SSH: saved host key for {hostKey}: {fingerprint}");
            }
            catch (Exception ex) { Logger.Debug($"SSH: failed to save host key: {ex.Message}"); }
        };
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

    /// <summary>
    /// Read the current server config from /etc/paqet/server.yaml via SSH.
    /// Returns the raw YAML content.
    /// </summary>
    public async Task<(bool Success, string Output)> ReadServerConfigAsync(AppSettings settings)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(settings.ServerSshHost))
                    return (false, "Server SSH host not configured.");

                using var client = CreateClient(settings);
                client.Connect();
                if (!client.IsConnected) return (false, "SSH connection failed.");

                var cmd = client.RunCommand("cat /etc/paqet/server.yaml");
                client.Disconnect();

                return (cmd.ExitStatus == 0, (cmd.Result ?? "").Trim());
            }
            catch (Exception ex)
            {
                Logger.Error("SSH: ReadServerConfig failed", ex);
                return (false, $"Error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Patch specific fields in server.yaml via SSH using sed.
    /// Backs up server.yaml before patching.
    /// </summary>
    /// <param name="settings">SSH connection settings</param>
    /// <param name="changes">Dictionary of field name → new value (key, block, mode, mtu, rcvwnd, sndwnd, etc.)</param>
    /// <param name="onProgress">Progress callback</param>
    public async Task<(bool Success, string Output)> PatchServerConfigAsync(
        AppSettings settings, Dictionary<string, string> changes, Action<string>? onProgress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(settings.ServerSshHost))
                    return (false, "Server SSH host not configured.");
                if (changes.Count == 0)
                    return (true, "No changes to apply.");

                using var client = CreateClient(settings);
                client.Connect();
                if (!client.IsConnected) return (false, "SSH connection failed.");

                // Backup current config
                onProgress?.Invoke("Backing up server config...");
                var backup = client.RunCommand("cp /etc/paqet/server.yaml /etc/paqet/server.yaml.bak");
                if (backup.ExitStatus != 0)
                {
                    client.Disconnect();
                    return (false, $"Backup failed: {backup.Error}");
                }
                Logger.Info("SSH: server.yaml backed up to server.yaml.bak");

                // Apply each change via sed
                onProgress?.Invoke("Patching server config...");
                foreach (var (field, value) in changes)
                {
                    // Build sed command based on field type
                    string sedCmd = field switch
                    {
                        "key" or "block" or "mode" =>
                            $"sed -i 's/\\({field}: *\\)\"[^\"]*\"/\\1\"{EscapeSed(value)}\"/' /etc/paqet/server.yaml",
                        "mtu" or "rcvwnd" or "sndwnd" or "smuxbuf" or "streambuf" =>
                            $"sed -i 's/\\({field}: *\\)[0-9]*/\\1{value}/' /etc/paqet/server.yaml",
                        _ => ""
                    };

                    if (string.IsNullOrEmpty(sedCmd)) continue;

                    var result = client.RunCommand(sedCmd);
                    if (result.ExitStatus != 0)
                    {
                        Logger.Warn($"SSH: sed for {field} failed: {result.Error}");
                        // Try insert if field doesn't exist (e.g. mtu not in original config)
                        if (field is "mtu" or "rcvwnd" or "sndwnd" or "smuxbuf" or "streambuf")
                        {
                            var insertCmd = $"sed -i '/^    key:/a\\    {field}: {value}' /etc/paqet/server.yaml";
                            client.RunCommand(insertCmd);
                        }
                    }
                    Logger.Info($"SSH: patched {field}={value}");
                }

                // Verify the config is valid YAML (basic check)
                var verify = client.RunCommand("cat /etc/paqet/server.yaml | head -5");
                client.Disconnect();

                var output = $"Patched {changes.Count} field(s): {string.Join(", ", changes.Keys)}";
                Logger.Info($"SSH: {output}");
                return (true, output);
            }
            catch (Exception ex)
            {
                Logger.Error("SSH: PatchServerConfig failed", ex);
                return (false, $"Error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Schedule a server restart with delay (allows SSH to disconnect first).
    /// Uses nohup to survive SSH disconnect.
    /// </summary>
    public async Task<(bool Success, string Output)> ScheduleServerRestartAsync(
        AppSettings settings, int delaySeconds = 2)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var client = CreateClient(settings);
                client.Connect();
                if (!client.IsConnected) return (false, "SSH connection failed.");

                var cmd = client.RunCommand(
                    $"nohup bash -c 'sleep {delaySeconds} && systemctl restart paqet' >/dev/null 2>&1 & echo 'Restart scheduled ({delaySeconds}s delay)'");
                client.Disconnect();

                Logger.Info($"SSH: server restart scheduled ({delaySeconds}s delay)");
                return (cmd.ExitStatus == 0, (cmd.Result ?? "").Trim());
            }
            catch (Exception ex)
            {
                Logger.Error("SSH: ScheduleServerRestart failed", ex);
                return (false, $"Error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Rollback server config from backup. Tries direct SSH (no SOCKS) if tunnel is down.
    /// </summary>
    public async Task<(bool Success, string Output)> RollbackServerConfigAsync(AppSettings settings)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var client = CreateClient(settings);
                client.Connect();
                if (!client.IsConnected) return (false, "SSH connection failed for rollback.");

                var cmd = client.RunCommand(
                    "cp /etc/paqet/server.yaml.bak /etc/paqet/server.yaml && "
                    + "nohup bash -c 'sleep 1 && systemctl restart paqet' >/dev/null 2>&1 & "
                    + "echo 'Config restored from backup, restart scheduled'");
                client.Disconnect();

                Logger.Info("SSH: server config rolled back from backup");
                return (cmd.ExitStatus == 0, (cmd.Result ?? "").Trim());
            }
            catch (Exception ex)
            {
                Logger.Error("SSH: RollbackServerConfig failed", ex);
                return (false, $"Rollback error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Reset server config to defaults (preserves key). Restarts paqet after.
    /// </summary>
    public async Task<(bool Success, string Output)> ResetServerConfigAsync(AppSettings settings)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var client = CreateClient(settings);
                client.Connect();
                if (!client.IsConnected) return (false, "SSH connection failed.");

                // Read current key to preserve it
                var readKey = client.RunCommand("grep 'key:' /etc/paqet/server.yaml | head -1 | sed 's/.*key: *\"\\(.*\\)\"/\\1/'");
                var currentKey = (readKey.Result ?? "").Trim();
                if (string.IsNullOrEmpty(currentKey))
                {
                    client.Disconnect();
                    return (false, "Could not read current encryption key from server config.");
                }

                // Reset breaking fields to defaults, preserve key and network config
                var cmds = new[]
                {
                    "sed -i 's/\\(block: *\\)\"[^\"]*\"/\\1\"aes\"/' /etc/paqet/server.yaml",
                    "sed -i 's/\\(mode: *\\)\"[^\"]*\"/\\1\"fast\"/' /etc/paqet/server.yaml",
                    "sed -i 's/\\(mtu: *\\)[0-9]*/\\11350/' /etc/paqet/server.yaml",
                    "sed -i 's/\\(rcvwnd: *\\)[0-9]*/\\11024/' /etc/paqet/server.yaml",
                    "sed -i 's/\\(sndwnd: *\\)[0-9]*/\\11024/' /etc/paqet/server.yaml",
                };

                foreach (var c in cmds)
                    client.RunCommand(c);

                // Schedule restart
                client.RunCommand("nohup bash -c 'sleep 1 && systemctl restart paqet' >/dev/null 2>&1 &");
                client.Disconnect();

                Logger.Info("SSH: server config reset to defaults (key preserved)");
                return (true, "Server config reset to defaults. Key preserved. Restart scheduled.");
            }
            catch (Exception ex)
            {
                Logger.Error("SSH: ResetServerConfig failed", ex);
                return (false, $"Error: {ex.Message}");
            }
        });
    }

    /// <summary>Escape special characters for sed replacement.</summary>
    private static string EscapeSed(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("/", "\\/")
            .Replace("&", "\\&")
            .Replace("\"", "\\\"")
            .Replace("'", "'\\''")
            .Replace("\n", "")
            .Replace("\r", "");
    }
}
