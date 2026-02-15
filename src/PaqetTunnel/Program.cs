using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PaqetTunnel.Services;

namespace PaqetTunnel;

/// <summary>
/// Custom entry point that supports CLI diagnostics mode.
/// Built as Exe subsystem so stdout works from SSH/pipes.
/// FreeConsole() is called immediately in GUI mode to hide the console.
/// </summary>
public static class Program
{
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--"))
        {
            RunCli(args);
            return;
        }

        // GUI mode — detach from console immediately to avoid flash
        FreeConsole();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static void RunCli(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("PaqetTunnel Diagnostics");
        Console.WriteLine("=======================");

        AppPaths.EnsureDirectories();
        Logger.Initialize(true);

        var command = args[0].ToLower().TrimStart('-');

        try
        {
            switch (command)
            {
                case "diag":
                    RunFullDiag().GetAwaiter().GetResult();
                    break;
                case "dns":
                    RunDnsBenchmark().GetAwaiter().GetResult();
                    break;
                case "ping":
                    RunPing().GetAwaiter().GetResult();
                    break;
                case "speed":
                    RunSpeed().GetAwaiter().GetResult();
                    break;
                case "info":
                    RunInfo();
                    break;
                case "check":
                    RunUpdateCheck().GetAwaiter().GetResult();
                    break;
                case "update":
                    RunUpdate().GetAwaiter().GetResult();
                    break;
                case "server":
                    RunServerCommand(args.Skip(1).ToArray()).GetAwaiter().GetResult();
                    break;
                case "help":
                case "h":
                    ShowHelp();
                    break;
                default:
                    Console.WriteLine($"Unknown command: --{command}");
                    ShowHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
        }

        Console.WriteLine();
        FreeConsole();
    }

    private static async Task RunFullDiag()
    {
        Console.WriteLine("\n[1/4] DNS Benchmark...");
        await RunDnsBenchmark();
        Console.WriteLine("\n[2/4] Connectivity...");
        await RunPing();
        Console.WriteLine("\n[3/4] Speed Test...");
        await RunSpeed();
        Console.WriteLine("\n[4/4] System Info...");
        RunInfo();
        Console.WriteLine("\n[OK] Full diagnostic complete.");
    }

    private static async Task RunDnsBenchmark()
    {
        Console.WriteLine();

        var configService = new ConfigService();
        var config = configService.ReadPaqetConfig();
        string? localIp = null;
        if (config != null)
        {
            var parts = config.Ipv4Addr?.Split(':');
            if (parts?.Length > 0 && !string.IsNullOrEmpty(parts[0])) localIp = parts[0];
        }

        Console.WriteLine($"{"#",-3} {"Provider",-24} {"Latency",8}  {"Server",-15}");
        Console.WriteLine(new string('-', 56));

        var results = await DnsService.BenchmarkAllAsync(localIp);
        int rank = 1;
        foreach (var r in results)
        {
            var latStr = r.AvgLatencyMs >= 9999 ? "timeout" : $"{r.AvgLatencyMs:F0}ms";
            var color = r.AvgLatencyMs < 50 ? ConsoleColor.Green :
                        r.AvgLatencyMs < 200 ? ConsoleColor.Yellow :
                        r.AvgLatencyMs < 9999 ? ConsoleColor.White : ConsoleColor.DarkGray;
            var marker = rank == 1 ? " * FASTEST" : "";

            Console.ForegroundColor = color;
            Console.WriteLine($"{rank,-3} {r.Name,-24} {latStr,8}  {r.Primary,-15}{marker}");
            Console.ResetColor();
            rank++;
        }

        var good = results.Where(r => r.AvgLatencyMs < 9999).ToList();
        Console.WriteLine($"\n{good.Count}/{results.Count} providers reachable");
    }

    private static async Task RunPing()
    {
        var configService = new ConfigService();
        var config = configService.ReadPaqetConfig();
        if (config == null)
        {
            Console.WriteLine("No config found.");
            return;
        }

        var serverAddr = config.ServerAddr ?? "";
        var host = serverAddr.Split(':')[0];

        // SOCKS5 proxy check
        Console.Write("SOCKS5 proxy (127.0.0.1:10800): ");
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync("127.0.0.1", 10800);
            if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask && tcp.Connected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("listening [OK]");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("not listening [FAIL]");
            }
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("not listening [FAIL]");
        }
        Console.ResetColor();

        // HTTP connectivity through tunnel (SOCKS5 proxy)
        Console.WriteLine($"\nTunnel connectivity to {host}:");
        var testUrls = new[]
        {
            ("HTTP via tunnel", "http://httpbin.org/ip"),
            ("HTTPS via tunnel", "https://api.ipify.org?format=json"),
        };

        foreach (var (name, url) in testUrls)
        {
            Console.Write($"  {name}: ");
            try
            {
                var handler = new System.Net.Http.HttpClientHandler
                {
                    Proxy = new System.Net.WebProxy("socks5://127.0.0.1:10800"),
                    UseProxy = true
                };
                using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var resp = await http.GetStringAsync(url);
                sw.Stop();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{sw.ElapsedMilliseconds}ms - {resp.Trim().Substring(0, Math.Min(resp.Trim().Length, 60))}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                var msg = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"failed - {msg}");
            }
            Console.ResetColor();
        }

        // ICMP ping to server
        Console.WriteLine($"\nICMP ping to {host}:");
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            for (int i = 0; i < 5; i++)
            {
                var reply = await ping.SendPingAsync(host, 3000);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  [{i + 1}] {reply.RoundtripTime}ms");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [{i + 1}] {reply.Status}");
                }
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ICMP failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static async Task RunSpeed()
    {
        var urls = new[]
        {
            ("Cloudflare 10MB", "https://speed.cloudflare.com/__down?bytes=10000000"),
            ("Cloudflare 1MB", "https://speed.cloudflare.com/__down?bytes=1000000"),
        };

        // Test through SOCKS5 proxy (tunnel)
        Console.WriteLine("Through tunnel (SOCKS5):");
        foreach (var (name, url) in urls)
        {
            Console.Write($"  {name}: ");
            try
            {
                var handler = new System.Net.Http.HttpClientHandler
                {
                    Proxy = new System.Net.WebProxy("socks5://127.0.0.1:10800"),
                    UseProxy = true
                };
                using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var data = await http.GetByteArrayAsync(url);
                sw.Stop();

                double mbps = (data.Length * 8.0 / 1_000_000) / sw.Elapsed.TotalSeconds;
                Console.ForegroundColor = mbps > 10 ? ConsoleColor.Green : mbps > 2 ? ConsoleColor.Yellow : ConsoleColor.Red;
                Console.WriteLine($"{mbps:F1} Mbps ({data.Length / 1024}KB in {sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            Console.ResetColor();
        }

        // Test direct (no proxy)
        Console.WriteLine("\nDirect (no tunnel):");
        foreach (var (name, url) in urls)
        {
            Console.Write($"  {name}: ");
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var data = await http.GetByteArrayAsync(url);
                sw.Stop();

                double mbps = (data.Length * 8.0 / 1_000_000) / sw.Elapsed.TotalSeconds;
                Console.ForegroundColor = mbps > 10 ? ConsoleColor.Green : mbps > 2 ? ConsoleColor.Yellow : ConsoleColor.Red;
                Console.WriteLine($"{mbps:F1} Mbps ({data.Length / 1024}KB in {sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            Console.ResetColor();
        }
    }

    private static void RunInfo()
    {
        var configService = new ConfigService();
        var config = configService.ReadPaqetConfig();
        var settings = configService.ReadAppSettings();

        Console.WriteLine($"\n  Install:    {AppPaths.DataDir}");
        Console.WriteLine($"  Binary:     {(System.IO.File.Exists(AppPaths.BinaryPath) ? "[OK]" : "[--]")} {AppPaths.BinaryPath}");
        Console.WriteLine($"  Config:     {(System.IO.File.Exists(AppPaths.PaqetConfigPath) ? "[OK]" : "[--]")} {AppPaths.PaqetConfigPath}");
        Console.WriteLine($"  Tun2socks:  {(System.IO.File.Exists(AppPaths.Tun2SocksPath) ? "[OK]" : "[--]")} {AppPaths.Tun2SocksPath}");
        Console.WriteLine($"  WinTun:     {(System.IO.File.Exists(AppPaths.WintunDllPath) ? "[OK]" : "[--]")} {AppPaths.WintunDllPath}");

        if (config != null)
        {
            Console.WriteLine($"\n  Server:     {config.ServerAddr}");
            Console.WriteLine($"  Interface:  {config.Interface}");
            Console.WriteLine($"  Local IP:   {config.Ipv4Addr}");
            Console.WriteLine($"  SOCKS5:     {config.SocksListen}");
            Console.WriteLine($"  Key set:    {(!string.IsNullOrEmpty(config.Key) ? "yes" : "no")}");
        }

        Console.WriteLine($"\n  Theme:      {settings.Theme}");
        Console.WriteLine($"  DNS:        {settings.DnsProvider}");
        Console.WriteLine($"  Debug:      {settings.DebugMode}");
        Console.WriteLine($"  TUN mode:   {settings.FullSystemTunnel}");
        Console.WriteLine($"  Auto-start: {settings.AutoStart}");

        // Check legacy install
        var legacyDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Paqet Tunnel");
        if (System.IO.Directory.Exists(legacyDir))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  [!] Legacy install found: {legacyDir}");
            Console.WriteLine("    Run 'setup.ps1 update' to clean up.");
            Console.ResetColor();
        }
    }

    private static async Task RunUpdateCheck()
    {
        Console.WriteLine("\nChecking for updates...");
        var (available, local, remote, message) = await UpdateService.CheckAsync();
        if (available)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n  Update available!");
            Console.ResetColor();
            Console.WriteLine($"  Local:   {(string.IsNullOrEmpty(local) ? "unknown" : local[..7])}");
            Console.WriteLine($"  Remote:  {remote[..7]}");
            if (!string.IsNullOrEmpty(message))
                Console.WriteLine($"  Message: {message}");
            Console.WriteLine($"\n  Run --update to install.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [OK] ");
            Console.ResetColor();
            Console.WriteLine("Already up to date" + (string.IsNullOrEmpty(local) ? "" : $" ({local[..7]})"));
        }
    }

    private static async Task RunUpdate()
    {
        Console.WriteLine("\nChecking for updates...");
        var (available, local, remote, message) = await UpdateService.CheckAsync();
        if (!available)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [OK] ");
            Console.ResetColor();
            Console.WriteLine("Already up to date" + (string.IsNullOrEmpty(local) ? "" : $" ({local[..7]})"));
            return;
        }
        Console.WriteLine($"  Update: {local[..7]} -> {remote[..7]}");
        if (!string.IsNullOrEmpty(message))
            Console.WriteLine($"  Message: {message}");
        Console.WriteLine("\nStarting update...");
        var (success, msg) = await UpdateService.RunSilentUpdateAsync(p =>
        {
            Console.WriteLine($"  {p}");
        });
        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n  Update started — app will restart shortly.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  Update failed: {msg}");
            Console.ResetColor();
        }
    }

    private static async Task RunServerCommand(string[] args)
    {
        var configService = new ConfigService();
        var settings = configService.ReadAppSettings();

        if (string.IsNullOrEmpty(settings.ServerSshHost))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Server SSH not configured.");
            Console.ResetColor();
            Console.WriteLine("  Configure in GUI Settings or set in settings.json:");
            Console.WriteLine("    ServerSshHost, ServerSshUser, ServerSshKeyPath or ServerSshPassword");
            return;
        }

        var subCmd = args.Length > 0 ? args[0].ToLower().TrimStart('-') : "status";
        var sshService = new SshService();

        Console.WriteLine($"\n  Host: {settings.ServerSshUser}@{settings.ServerSshHost}:{settings.ServerSshPort}");
        Console.WriteLine($"  Auth: {(string.IsNullOrEmpty(settings.ServerSshKeyPath) ? "password" : "key (" + settings.ServerSshKeyPath + ")")}");

        // Test connection uses a separate path
        if (subCmd == "test")
        {
            Console.WriteLine("  Testing SSH connection...\n");
            var (ok, msg) = await sshService.TestConnectionAsync(settings);
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(ok ? "  [OK] " : "  [ERR] ");
            Console.ResetColor();
            Console.WriteLine(msg.Replace("\n", "\n  "));
            return;
        }

        // Read server config
        if (subCmd == "config")
        {
            Console.WriteLine("  Reading server config...\n");
            var (ok, yaml) = await sshService.ReadServerConfigAsync(settings);
            if (ok)
            {
                foreach (var line in yaml.Split('\n'))
                    Console.WriteLine($"  {line.TrimEnd('\r')}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [ERR] ");
                Console.ResetColor();
                Console.WriteLine(yaml);
            }
            return;
        }

        // Sync local breaking changes to server
        if (subCmd == "sync")
        {
            Console.WriteLine("  Comparing local config with server...\n");
            var localConfig = configService.ReadPaqetConfig();
            var (srvOk, srvYaml) = await sshService.ReadServerConfigAsync(settings);
            if (!srvOk)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [ERR] ");
                Console.ResetColor();
                Console.WriteLine($"Could not read server config: {srvYaml}");
                return;
            }
            var serverConfig = Models.PaqetConfig.FromYaml(srvYaml);
            var changes = localConfig.GetAllServerChanges(serverConfig);
            if (changes.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [OK] ");
                Console.ResetColor();
                Console.WriteLine("Server config matches local — no sync needed.");
                return;
            }
            Console.WriteLine($"  Changes to sync ({changes.Count}):");
            foreach (var (field, value) in changes)
                Console.WriteLine($"    {field}: {value}");
            Console.WriteLine();
            Console.Write("  Apply changes to server? [y/N] ");
            var answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("  Cancelled.");
                return;
            }
            var (patchOk, patchMsg) = await sshService.PatchServerConfigAsync(settings, changes,
                p => Console.WriteLine($"  {p}"));
            if (patchOk)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("\n  [OK] ");
                Console.ResetColor();
                Console.WriteLine(patchMsg);
                Console.Write("  Restart server now? [y/N] ");
                var restartAnswer = Console.ReadLine()?.Trim().ToLower();
                if (restartAnswer == "y" || restartAnswer == "yes")
                {
                    var (rOk, rMsg) = await sshService.ScheduleServerRestartAsync(settings, 2);
                    Console.WriteLine($"  {(rOk ? rMsg : "Restart failed: " + rMsg)}");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("\n  [ERR] ");
                Console.ResetColor();
                Console.WriteLine($"Sync failed: {patchMsg}");
            }
            return;
        }

        // Reset server config to defaults
        if (subCmd == "reset")
        {
            Console.Write("  Reset server config to defaults (key preserved)? [y/N] ");
            var answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("  Cancelled.");
                return;
            }
            var (ok, msg) = await sshService.ResetServerConfigAsync(settings);
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(ok ? "\n  [OK] " : "\n  [ERR] ");
            Console.ResetColor();
            Console.WriteLine(msg);
            return;
        }

        Console.WriteLine($"  Command: {subCmd}\n");

        var (success, output) = await sshService.RunServerCommandAsync(settings, subCmd, p =>
        {
            Console.WriteLine($"  {p}");
        });

        Console.WriteLine();
        if (!string.IsNullOrEmpty(output))
        {
            foreach (var line in output.Split('\n'))
                Console.WriteLine($"  {line.TrimEnd('\r')}");
        }

        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\n  [OK] ");
            Console.ResetColor();
            Console.WriteLine($"Server {subCmd} complete.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("\n  [ERR] ");
            Console.ResetColor();
            Console.WriteLine($"Server {subCmd} failed.");
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
Usage: PaqetTunnel.exe [--command] [options]

Client Commands:
  (no args)     Start the GUI application
  --diag        Run full diagnostic suite (DNS + ping + speed + info)
  --dns         Benchmark all DNS providers
  --ping        Test server connectivity (TCP + SOCKS5)
  --speed       Test download speed through tunnel
  --info        Show installation and config info
  --check       Check for client updates
  --update      Check and install client update

Server Commands:
  --server test        Test SSH connection
  --server status      Show server status
  --server config      Show server config (YAML)
  --server sync        Sync local config changes to server
  --server reset       Reset server config to defaults (key preserved)
  --server install     Install paqet server
  --server update      Update paqet server
  --server uninstall   Uninstall paqet server
  --server restart     Restart paqet server
  --server logs        Show server logs

  --help        Show this help

Examples:
  PaqetTunnel.exe --check
  PaqetTunnel.exe --update
  PaqetTunnel.exe --server status
  PaqetTunnel.exe --server sync
  PaqetTunnel.exe --server config
");
    }
}
