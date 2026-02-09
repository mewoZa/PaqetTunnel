using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PaqetTunnel.Services;

namespace PaqetTunnel;

/// <summary>
/// Custom entry point that supports CLI diagnostics mode.
/// Usage:
///   PaqetTunnel.exe                    — Start GUI (default)
///   PaqetTunnel.exe --diag             — Run full diagnostic suite
///   PaqetTunnel.exe --dns              — Benchmark all DNS providers
///   PaqetTunnel.exe --ping             — Test tunnel connectivity
///   PaqetTunnel.exe --speed            — Test download speed
///   PaqetTunnel.exe --info             — Show system/config info
///   PaqetTunnel.exe --help             — Show CLI help
/// </summary>
public static class Program
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--"))
        {
            RunCli(args);
            return;
        }

        // Normal WPF startup
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static void RunCli(string[] args)
    {
        // Attach to parent console (if launched from cmd/powershell) or allocate new
        if (!AttachConsole(-1))
            AllocConsole();

        Console.WriteLine();
        Console.WriteLine("PaqetTunnel Diagnostics");
        Console.WriteLine("═══════════════════════");

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
        Console.WriteLine("\n✓ Full diagnostic complete.");
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
        Console.WriteLine(new string('─', 56));

        var results = await DnsService.BenchmarkAllAsync(localIp);
        int rank = 1;
        foreach (var r in results)
        {
            var latStr = r.AvgLatencyMs >= 9999 ? "timeout" : $"{r.AvgLatencyMs:F0}ms";
            var color = r.AvgLatencyMs < 50 ? ConsoleColor.Green :
                        r.AvgLatencyMs < 200 ? ConsoleColor.Yellow :
                        r.AvgLatencyMs < 9999 ? ConsoleColor.White : ConsoleColor.DarkGray;
            var marker = rank == 1 ? " ★ FASTEST" : "";

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
        var port = serverAddr.Contains(':') ? int.Parse(serverAddr.Split(':')[1]) : 8443;

        Console.WriteLine($"Pinging {host}:{port}...");

        // TCP connect test
        for (int i = 0; i < 5; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var connectTask = tcp.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask && tcp.Connected)
                {
                    sw.Stop();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  [{i + 1}] TCP connect: {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [{i + 1}] TCP connect: timeout");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [{i + 1}] TCP connect: {ex.Message}");
            }
            Console.ResetColor();
        }

        // SOCKS5 proxy check
        Console.Write("\nSOCKS5 proxy (127.0.0.1:10800): ");
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync("127.0.0.1", 10800);
            if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask && tcp.Connected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("listening ✓");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("not listening ✗");
            }
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("not listening ✗");
        }
        Console.ResetColor();
    }

    private static async Task RunSpeed()
    {
        var urls = new[]
        {
            ("Cloudflare 10MB", "https://speed.cloudflare.com/__down?bytes=10000000"),
            ("Cloudflare 1MB", "https://speed.cloudflare.com/__down?bytes=1000000"),
        };

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
                Console.WriteLine($"failed: {ex.Message}");
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
        Console.WriteLine($"  Binary:     {(System.IO.File.Exists(AppPaths.BinaryPath) ? "✓" : "✗")} {AppPaths.BinaryPath}");
        Console.WriteLine($"  Config:     {(System.IO.File.Exists(AppPaths.PaqetConfigPath) ? "✓" : "✗")} {AppPaths.PaqetConfigPath}");
        Console.WriteLine($"  Tun2socks:  {(System.IO.File.Exists(AppPaths.Tun2SocksPath) ? "✓" : "✗")} {AppPaths.Tun2SocksPath}");
        Console.WriteLine($"  WinTun:     {(System.IO.File.Exists(AppPaths.WintunDllPath) ? "✓" : "✗")} {AppPaths.WintunDllPath}");

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
            Console.WriteLine($"\n  ⚠ Legacy install found: {legacyDir}");
            Console.WriteLine("    Run 'setup.ps1 update' to clean up.");
            Console.ResetColor();
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
Usage: PaqetTunnel.exe [--command]

Commands:
  (no args)     Start the GUI application
  --diag        Run full diagnostic suite (DNS + ping + speed + info)
  --dns         Benchmark all DNS providers
  --ping        Test server connectivity (TCP + SOCKS5)
  --speed       Test download speed through tunnel
  --info        Show installation and config info
  --help        Show this help

Examples:
  PaqetTunnel.exe --dns
  PaqetTunnel.exe --diag
  PaqetTunnel.exe --ping
");
    }
}
