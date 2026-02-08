using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaqetTunnel.Models;
using PaqetTunnel.Services;

namespace PaqetTunnel.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PaqetService _paqetService;
    private readonly ProxyService _proxyService;
    private readonly NetworkMonitorService _networkMonitor;
    private readonly ConfigService _configService;
    private readonly SetupService _setupService;
    private readonly TunService _tunService;
    private readonly Timer _statusTimer;

    // ── Connection State ──────────────────────────────────────────

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _connectionTime = "";
    private DateTime _connectedSince;

    // ── Speed ─────────────────────────────────────────────────────

    [ObservableProperty] private string _downloadSpeed = "0 B/s";
    [ObservableProperty] private string _uploadSpeed = "0 B/s";
    [ObservableProperty] private List<double> _speedHistory = new();
    [ObservableProperty] private List<double> _downloadHistory = new();
    [ObservableProperty] private List<double> _uploadHistory = new();
    [ObservableProperty] private double _peakSpeed;
    [ObservableProperty] private string _totalTransferred = "0 B";

    // ── Toggles ───────────────────────────────────────────────────

    [ObservableProperty] private bool _isSystemProxyEnabled;
    [ObservableProperty] private bool _isProxySharingEnabled;
    [ObservableProperty] private bool _isAutoStartEnabled;
    [ObservableProperty] private bool _isStartBeforeLogonEnabled;

    // ── Config Fields ─────────────────────────────────────────────

    [ObservableProperty] private string _serverAddress = "";
    [ObservableProperty] private int _serverPort = 443;
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _networkInterface = "";

    // ── Setup State ───────────────────────────────────────────────

    [ObservableProperty] private bool _isSettingUp;
    [ObservableProperty] private bool _needsSetup;
    [ObservableProperty] private string _setupStatus = "";
    [ObservableProperty] private bool _isNpcapInstalled;

    // ── UI State ──────────────────────────────────────────────────

    [ObservableProperty] private string _statusBarText = "Initializing...";
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _showTools;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _debugMode;
    [ObservableProperty] private bool _isFullSystemTunnel;
    [ObservableProperty] private bool _isAutoConnect;
    [ObservableProperty] private string _paqetVersion = "";
    [ObservableProperty] private string _interfaceList = "";
    [ObservableProperty] private string _pingResult = "";
    [ObservableProperty] private string _fullVersionInfo = "";
    [ObservableProperty] private string _updateStatus = "";

    // ── Connection Info ───────────────────────────────────────────

    [ObservableProperty] private string _localIp = "";
    [ObservableProperty] private string _serverIpDisplay = "";
    [ObservableProperty] private string _tunnelMode = "SOCKS5";
    [ObservableProperty] private string _socksPort = "10800";
    [ObservableProperty] private bool _showInfoPanel;
    [ObservableProperty] private string _publicIp = "—";

    public MainViewModel(
        PaqetService paqetService,
        ProxyService proxyService,
        NetworkMonitorService networkMonitor,
        ConfigService configService,
        SetupService setupService,
        TunService tunService)
    {
        _paqetService = paqetService;
        _proxyService = proxyService;
        _networkMonitor = networkMonitor;
        _configService = configService;
        _setupService = setupService;
        _tunService = tunService;

        // Speed updates from monitor
        _networkMonitor.SpeedUpdated += OnSpeedUpdated;

        // Periodic status polling (every 3 seconds)
        _statusTimer = new Timer(3000);
        _statusTimer.Elapsed += OnStatusTick;
        _statusTimer.AutoReset = true;
    }

    /// <summary>Initialize the app — check setup, load config, start polling.</summary>
    public async Task InitializeAsync()
    {
        Logger.Info("InitializeAsync started");

        // Load debug mode state
        var appSettings = _configService.ReadAppSettings();
        DebugMode = appSettings.DebugMode;
        IsFullSystemTunnel = appSettings.FullSystemTunnel;
        IsAutoConnect = appSettings.AutoConnectOnLaunch;

        // Check if paqet binary exists
        NeedsSetup = !_paqetService.BinaryExists();
        IsNpcapInstalled = SetupService.IsNpcapInstalled();
        Logger.Info($"NeedsSetup={NeedsSetup}, NpcapInstalled={IsNpcapInstalled}");

        // Load config
        var config = _configService.ReadPaqetConfig();
        ServerAddress = config.ServerHost;
        ServerPort = config.ServerPort;
        Key = config.Key;
        NetworkInterface = config.Interface;
        ServerIpDisplay = config.ServerHost;
        Logger.Info($"Config loaded: server={config.ServerAddr}, interface={config.Interface}, socks={config.SocksListen}");

        // Get local IP
        LocalIp = GetLocalIp();

        // Load toggle states
        IsSystemProxyEnabled = _proxyService.IsSystemProxyEnabled();
        IsProxySharingEnabled = _proxyService.IsProxySharingEnabled();
        IsAutoStartEnabled = _proxyService.IsAutoStartEnabled();
        IsStartBeforeLogonEnabled = _proxyService.IsStartBeforeLogonEnabled();

        // ── Check running state (use IsReady for port-verified status)
        var running = _paqetService.IsRunning();
        var ready = running && PaqetService.IsPortListening();
        Logger.Info($"Initial state: running={running}, portReady={ready}");
        IsConnected = ready;
        if (IsConnected)
        {
            _connectedSince = DateTime.Now;
            ConnectionStatus = "Connected";
            _networkMonitor.Start();
        }
        else if (running)
        {
            ConnectionStatus = "Process running, port not ready";
        }

        // Get version
        PaqetVersion = _paqetService.GetVersion() ?? "";
        Logger.Info($"PaqetVersion={PaqetVersion}");

        // Start polling
        _statusTimer.Start();

        StatusBarText = NeedsSetup
            ? "Setup required — click Install"
            : IsConnected ? "Connected" : "Ready";

        Logger.Info($"StatusBarText={StatusBarText}");

        // Auto-setup if needed
        if (NeedsSetup)
        {
            Logger.Info("Running auto-setup...");
            await RunSetupAsync();
        }

        // Pre-download TUN binaries in background if missing (so they're ready when user needs them)
        if (!_tunService.AllBinariesExist())
        {
            Logger.Info("TUN binaries missing, pre-downloading in background...");
            _ = Task.Run(async () =>
            {
                var result = await _tunService.DownloadBinariesAsync();
                Logger.Info($"TUN pre-download: {result.Message}");
            });
        }

        Logger.Info("InitializeAsync complete");

        // Background update check
        if (UpdateService.ShouldCheck())
            _ = CheckForUpdatesAsync();

        // Auto-connect if enabled and not already connected
        if (IsAutoConnect && !IsConnected && !NeedsSetup)
        {
            Logger.Info("Auto-connect enabled, starting connection...");
            await ConnectAsync();
        }
    }

    // ── Commands ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnecting) return;

        if (IsConnected)
        {
            await DisconnectAsync();
        }
        else
        {
            await ConnectAsync();
        }
    }

    private async Task ConnectAsync()
    {
        IsConnecting = true;
        ConnectionStatus = "Connecting...";
        StatusBarText = "Connecting...";
        Logger.Info($"ConnectAsync started (FullSystemTunnel={IsFullSystemTunnel})");

        await Task.Run(async () =>
        {
            // Ensure binary exists
            if (!_paqetService.BinaryExists())
            {
                Logger.Warn("ConnectAsync: binary not found");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConnectionStatus = "Binary not found";
                    StatusBarText = "Setup required";
                    IsConnecting = false;
                });
                return;
            }

            // Step 1: Start paqet SOCKS5
            Logger.Info("Calling PaqetService.Start()...");
            Application.Current.Dispatcher.Invoke(() => ConnectionStatus = "Starting paqet...");
            var (success, message) = _paqetService.Start();
            Logger.Info($"Start() returned: success={success}, message={message}");

            if (!success)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConnectionStatus = message;
                    StatusBarText = message;
                    IsConnecting = false;
                });
                return;
            }

            // Verify port is actually listening
            var portReady = PaqetService.IsPortListening();
            Logger.Info($"Post-start port check: {portReady}");

            if (!portReady)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = false;
                    ConnectionStatus = message;
                    StatusBarText = message;
                    IsConnecting = false;
                });
                return;
            }

            // Step 2: If TUN mode, start tun2socks
            if (IsFullSystemTunnel)
            {
                // Auto-download TUN binaries if missing
                if (!_tunService.AllBinariesExist())
                {
                    Application.Current.Dispatcher.Invoke(() => ConnectionStatus = "Downloading TUN binaries...");
                    Logger.Info("TUN binaries missing, downloading...");
                    var dlResult = await _tunService.DownloadBinariesAsync();
                    Logger.Info($"TUN download: success={dlResult.Success}, message={dlResult.Message}");
                    if (!dlResult.Success)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsConnected = true;
                            _connectedSince = DateTime.Now;
                            ConnectionStatus = $"SOCKS5 only — {dlResult.Message}";
                            StatusBarText = dlResult.Message;
                            _networkMonitor.Start();
                            IsConnecting = false;
                        });
                        return;
                    }
                }

                // Disable system proxy before starting TUN — proxy + TUN conflicts
                EnsureProxyDisabledForTun();

                Application.Current.Dispatcher.Invoke(() => ConnectionStatus = "Starting TUN tunnel...");
                Logger.Info("Starting TUN tunnel...");
                var config = _configService.ReadPaqetConfig();
                var serverIp = config.ServerHost;
                var tunResult = await _tunService.StartAsync(serverIp);
                Logger.Info($"TUN start: success={tunResult.Success}, message={tunResult.Message}");

                if (!tunResult.Success)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // SOCKS5 is still running, show partial success
                        IsConnected = true;
                        _connectedSince = DateTime.Now;
                        ConnectionStatus = $"SOCKS5 only — TUN: {tunResult.Message}";
                        StatusBarText = tunResult.Message;
                        _networkMonitor.Start();
                        IsConnecting = false;
                    });
                    return;
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = true;
                _connectedSince = DateTime.Now;
                TunnelMode = IsFullSystemTunnel ? "Full System (TUN)" : "SOCKS5 Proxy";
                ConnectionStatus = IsFullSystemTunnel ? "Connected (Full System)" : "Connected";
                StatusBarText = IsFullSystemTunnel ? "Full system tunnel active" : "Connected";
                _networkMonitor.Start();
                IsConnecting = false;
            });
            // Flush DNS cache after connecting (clear stale ISP DNS entries)
            _ = Task.Run(() => TunService.FlushDnsCache());
            _ = FetchPublicIpAsync();
        });
    }

    private async Task DisconnectAsync()
    {
        IsConnecting = true;
        ConnectionStatus = "Disconnecting...";
        Logger.Info("DisconnectAsync started");

        await Task.Run(async () =>
        {
            _networkMonitor.Stop();

            // Stop TUN first (restore routes before killing paqet)
            if (_tunService.IsRunning())
            {
                Logger.Info("Stopping TUN tunnel...");
                await _tunService.StopAsync();
            }

            var (success, message) = _paqetService.Stop();
            Logger.Info($"Stop() returned: success={success}, message={message}");

            // Flush DNS cache after disconnect to clear tunnel DNS entries
            TunService.FlushDnsCache();

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    IsConnected = false;
                    ConnectionStatus = "Disconnected";
                    ConnectionTime = "";
                    DownloadSpeed = "0 B/s";
                    UploadSpeed = "0 B/s";
                    SpeedHistory = new List<double>();
                    DownloadHistory = new List<double>();
                    UploadHistory = new List<double>();
                    PeakSpeed = 0;
                    TotalTransferred = "0 B";
                    PublicIp = "—";
                    StatusBarText = "Disconnected";
                }
                else
                {
                    ConnectionStatus = message;
                }
                IsConnecting = false;
            });
        });
    }

    [RelayCommand]
    private async Task ToggleSystemProxyAsync()
    {
        var newState = !IsSystemProxyEnabled;

        // Block enabling system proxy when TUN tunnel is active
        if (newState && _tunService.IsRunning())
        {
            StatusBarText = "System proxy disabled in TUN mode (not needed)";
            Logger.Info("Blocked system proxy enable — TUN tunnel active");
            return;
        }

        IsBusy = true;
        await Task.Run(() =>
        {
            var result = _proxyService.SetSystemProxy(newState);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    IsSystemProxyEnabled = newState;
                    StatusBarText = result.Message;
                }
                else
                {
                    StatusBarText = result.Message;
                }
                IsBusy = false;
            });
        });
    }

    [RelayCommand]
    private async Task ToggleProxySharingAsync()
    {
        var newState = !IsProxySharingEnabled;
        IsBusy = true;
        await Task.Run(() =>
        {
            var result = _proxyService.SetProxySharing(newState);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    IsProxySharingEnabled = newState;
                    var settings = _configService.ReadAppSettings();
                    settings.ProxySharingEnabled = newState;
                    _configService.WriteAppSettings(settings);
                    StatusBarText = result.Message;
                }
                else
                {
                    StatusBarText = result.Message;
                }
                IsBusy = false;
            });
        });
    }

    [RelayCommand]
    private async Task ToggleAutoStartAsync()
    {
        var newState = !IsAutoStartEnabled;
        IsBusy = true;
        await Task.Run(() =>
        {
            var result = _proxyService.SetAutoStart(newState);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    IsAutoStartEnabled = newState;
                    var settings = _configService.ReadAppSettings();
                    settings.AutoStart = newState;
                    _configService.WriteAppSettings(settings);
                    StatusBarText = result.Message;
                }
                else
                {
                    StatusBarText = result.Message;
                }
                IsBusy = false;
            });
        });
    }

    [RelayCommand]
    private async Task ToggleStartBeforeLogonAsync()
    {
        var newState = !IsStartBeforeLogonEnabled;
        IsBusy = true;
        await Task.Run(() =>
        {
            var result = _proxyService.SetStartBeforeLogon(newState);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    IsStartBeforeLogonEnabled = newState;
                    var settings = _configService.ReadAppSettings();
                    settings.StartBeforeLogon = newState;
                    _configService.WriteAppSettings(settings);
                    StatusBarText = result.Message;
                }
                else
                {
                    StatusBarText = result.Message;
                }
                IsBusy = false;
            });
        });
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        ShowSettings = !ShowSettings;
    }

    [RelayCommand]
    private void ToggleTools()
    {
        ShowTools = !ShowTools;
    }

    [RelayCommand]
    private void ToggleInfoPanel()
    {
        ShowInfoPanel = !ShowInfoPanel;
        if (ShowInfoPanel && IsConnected && PublicIp == "—")
            _ = FetchPublicIpAsync();
    }

    private async Task FetchPublicIpAsync()
    {
        try
        {
            var ip = await Task.Run(() => PaqetService.RunCommand("curl", "-s --max-time 5 https://api.ipify.org"));
            Application.Current.Dispatcher.Invoke(() => PublicIp = ip?.Trim() ?? "—");
        }
        catch { Application.Current.Dispatcher.Invoke(() => PublicIp = "unavailable"); }
    }

    [RelayCommand]
    private void ToggleDebugMode()
    {
        DebugMode = !DebugMode;
        var settings = _configService.ReadAppSettings();
        settings.DebugMode = DebugMode;
        _configService.WriteAppSettings(settings);

        if (DebugMode && !Logger.IsEnabled)
            Logger.Initialize(true);

        Logger.Info($"Debug mode toggled: {DebugMode}");
        StatusBarText = DebugMode
            ? $"Debug ON — {Logger.LogPath}"
            : "Debug mode disabled (restart to stop logging).";
    }

    [RelayCommand]
    private void ToggleAutoConnect()
    {
        IsAutoConnect = !IsAutoConnect;
        var settings = _configService.ReadAppSettings();
        settings.AutoConnectOnLaunch = IsAutoConnect;
        _configService.WriteAppSettings(settings);
        Logger.Info($"Auto-connect toggled: {IsAutoConnect}");
        StatusBarText = IsAutoConnect ? "Auto-connect enabled" : "Auto-connect disabled";
    }

    [RelayCommand]
    private async Task ToggleFullSystemTunnel()
    {
        IsFullSystemTunnel = !IsFullSystemTunnel;
        Logger.Info($"Full system tunnel toggled to: {IsFullSystemTunnel}");
        var settings = _configService.ReadAppSettings();
        settings.FullSystemTunnel = IsFullSystemTunnel;
        _configService.WriteAppSettings(settings);

        // If currently connected, start/stop TUN on the fly
        if (IsConnected)
        {
            if (IsFullSystemTunnel)
            {
                // Auto-download TUN binaries if missing
                if (!_tunService.AllBinariesExist())
                {
                    ConnectionStatus = "Downloading TUN binaries...";
                    var dlResult = await _tunService.DownloadBinariesAsync();
                    if (!dlResult.Success)
                    {
                        ConnectionStatus = $"SOCKS5 only — {dlResult.Message}";
                        return;
                    }
                }

                // Disable system proxy — TUN handles all traffic
                EnsureProxyDisabledForTun();

                ConnectionStatus = "Starting TUN tunnel...";
                var config = _configService.ReadPaqetConfig();
                var serverIp = config.ServerHost;
                var result = await _tunService.StartAsync(serverIp);
                ConnectionStatus = result.Success ? "Connected (Full System)" : $"SOCKS5 only — TUN: {result.Message}";
            }
            else
            {
                ConnectionStatus = "Stopping TUN tunnel...";
                await _tunService.StopAsync();
                ConnectionStatus = "Connected";
            }
        }
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        IsBusy = true;
        StatusBarText = "Saving config...";
        await Task.Run(() =>
        {
            var config = _configService.ReadPaqetConfig();
            config.ServerAddr = $"{ServerAddress}:{ServerPort}";
            config.Key = Key;
            config.Interface = NetworkInterface;
            _configService.WritePaqetConfig(config);
        });
        StatusBarText = "Config saved.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RunSetupAsync()
    {
        IsSettingUp = true;
        SetupStatus = "Starting setup...";
        StatusBarText = "Setting up...";

        var progress = new Progress<string>(msg =>
        {
            Application.Current.Dispatcher.Invoke(() => SetupStatus = msg);
        });

        var result = await _setupService.RunFullSetupAsync(progress);

        IsSettingUp = false;
        NeedsSetup = !_paqetService.BinaryExists();
        IsNpcapInstalled = SetupService.IsNpcapInstalled();
        PaqetVersion = _paqetService.GetVersion() ?? "";
        SetupStatus = result.Message;
        StatusBarText = result.Success ? "Setup complete." : "Setup had issues.";
    }

    // ── Tool Commands ─────────────────────────────────────────────

    [RelayCommand]
    private async Task ListInterfacesAsync()
    {
        IsBusy = true;
        StatusBarText = "Listing interfaces...";
        await Task.Run(() =>
        {
            var (success, output) = _paqetService.ListInterfaces();
            Application.Current.Dispatcher.Invoke(() =>
            {
                InterfaceList = output;
                StatusBarText = success ? "Interfaces listed." : output;
                IsBusy = false;
            });
        });
    }

    [RelayCommand]
    private async Task GenerateSecretAsync()
    {
        IsBusy = true;
        StatusBarText = "Generating secret key...";
        await Task.Run(() =>
        {
            var (success, key) = _paqetService.GenerateSecret();
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (success && !string.IsNullOrEmpty(key))
                {
                    Key = key;
                    StatusBarText = "Secret key generated and applied.";
                }
                else
                {
                    StatusBarText = key;
                }
                IsBusy = false;
            });
        });
    }

    [RelayCommand]
    private async Task PingServerAsync()
    {
        IsBusy = true;
        PingResult = "Pinging...";
        StatusBarText = "Sending ping...";
        await Task.Run(() =>
        {
            var (success, output) = _paqetService.Ping();
            Application.Current.Dispatcher.Invoke(() =>
            {
                PingResult = output;
                StatusBarText = success ? "Ping complete." : output;
                IsBusy = false;
            });
        });
    }

    [RelayCommand]
    private void ShowVersionInfo()
    {
        var info = _paqetService.GetFullVersionInfo();
        FullVersionInfo = info ?? "Version info not available.";
        StatusBarText = info != null ? "Version info loaded." : "Binary not found.";
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = "Checking...";
        StatusBarText = "Checking for updates...";
        var (available, local, remote) = await UpdateService.CheckAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (available)
            {
                UpdateStatus = $"Update available ({remote[..7]})";
                StatusBarText = $"Update available! {local[..7]} → {remote[..7]}";
            }
            else
            {
                UpdateStatus = "Up to date";
                StatusBarText = "No updates available.";
            }
        });
    }

    [RelayCommand]
    private async Task RunUpdateAsync()
    {
        StatusBarText = "Starting update...";
        await UpdateService.RunUpdateAsync();
    }

    // ── Periodic Status Check ─────────────────────────────────────

    private void OnStatusTick(object? sender, ElapsedEventArgs e)
    {
        try
        {
            var running = _paqetService.IsRunning();
            var portReady = running && PaqetService.IsPortListening();

            // Only log when state changes or periodically
            if (portReady != IsConnected)
                Logger.Info($"OnStatusTick: state change — running={running}, portReady={portReady}, wasConnected={IsConnected}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (portReady != IsConnected)
                {
                    IsConnected = portReady;
                    var tunActive = _tunService.IsRunning();
                    ConnectionStatus = portReady
                        ? (tunActive ? "Connected (Full System)" : "Connected")
                        : running ? "Port not ready" : "Disconnected";
                    Logger.Info($"OnStatusTick: ConnectionStatus={ConnectionStatus}");
                    if (portReady)
                    {
                        _connectedSince = DateTime.Now;
                        _networkMonitor.Start();
                    }
                    else
                    {
                        _networkMonitor.Stop();
                        DownloadSpeed = "0 B/s";
                        UploadSpeed = "0 B/s";
                        ConnectionTime = "";
                    }
                }

                // Update connection time
                if (IsConnected)
                {
                    var elapsed = DateTime.Now - _connectedSince;
                    ConnectionTime = elapsed.TotalHours >= 1
                        ? elapsed.ToString(@"hh\:mm\:ss")
                        : elapsed.ToString(@"mm\:ss");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("OnStatusTick exception", ex);
        }
    }

    private void OnSpeedUpdated()
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var latest = _networkMonitor.Latest;
                DownloadSpeed = NetworkMonitorService.FormatSpeed(latest.DownloadSpeed);
                UploadSpeed = NetworkMonitorService.FormatSpeed(latest.UploadSpeed);

                lock (_networkMonitor)
                {
                    var hist = _networkMonitor.History;
                    DownloadHistory = new List<double>(hist.ConvertAll(s => s.DownloadSpeed));
                    UploadHistory = new List<double>(hist.ConvertAll(s => s.UploadSpeed));
                    SpeedHistory = new List<double>(hist.ConvertAll(s => s.DownloadSpeed + s.UploadSpeed));

                    var peak = hist.Count > 0 ? hist.Max(s => s.DownloadSpeed + s.UploadSpeed) : 0;
                    PeakSpeed = peak;

                    double totalDl = hist.Sum(s => s.DownloadSpeed);
                    double totalUl = hist.Sum(s => s.UploadSpeed);
                    TotalTransferred = NetworkMonitorService.FormatBytes(totalDl + totalUl);
                }
            });
        }
        catch { /* Swallow UI errors */ }
    }

    /// <summary>
    /// Disable system proxy and WinHTTP proxy when TUN tunnel is active.
    /// TUN routes ALL traffic through the tunnel — proxy settings cause conflicts
    /// (double-proxy, broken Windows Update, cert mismatches on CDN endpoints).
    /// </summary>
    private void EnsureProxyDisabledForTun()
    {
        Logger.Info("EnsureProxyDisabledForTun: cleaning all proxy settings for TUN mode");

        // Always force full proxy disable — clears ProxyEnable, ProxyServer, ProxyOverride
        _proxyService.SetSystemProxy(false);
        Application.Current.Dispatcher.Invoke(() => IsSystemProxyEnabled = false);

        // Reset WinHTTP proxy (Windows Update, system services)
        try
        {
            PaqetService.RunCommand("netsh", "winhttp reset proxy");
            Logger.Info("Reset WinHTTP proxy for TUN mode");
        }
        catch (Exception ex) { Logger.Debug($"WinHTTP reset: {ex.Message}"); }
    }

    public void Cleanup()
    {
        _statusTimer.Stop();
        _statusTimer.Dispose();
        _networkMonitor.Stop();
        _networkMonitor.Dispose();

        // Stop TUN if running
        if (_tunService.IsRunning())
        {
            Logger.Info("Cleanup: stopping TUN tunnel");
            _tunService.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static string GetLocalIp()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                    ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                    !ni.Name.Contains("PaqetTun", StringComparison.OrdinalIgnoreCase) &&
                    ni.GetIPProperties().GatewayAddresses.Count > 0)
                {
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return addr.Address.ToString();
                    }
                }
            }
        }
        catch { }
        return "Unknown";
    }
}
