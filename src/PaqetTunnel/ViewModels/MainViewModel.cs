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
    private readonly DiagnosticService _diagnosticService;
    private readonly Timer _statusTimer;

    // ── Connection State ──────────────────────────────────────────

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _connectionTime = "";
    private DateTime _connectedSince;

    // ── Reconnection & Health ─────────────────────────────────────

    private bool _userRequestedConnect;   // true if user explicitly connected
    private int _reconnectAttempts;
    private const int MAX_RECONNECT_ATTEMPTS = 5;
    private const int RECONNECT_BASE_DELAY_MS = 3000;
    private int _healthCheckCounter;
    private int _healthRefreshCounter;
    private int _consecutiveHealthFailures;
    private const int HEALTH_CHECK_INTERVAL = 10; // every 10 ticks (30s at 3s/tick)
    private const int HEALTH_FAIL_THRESHOLD = 2;  // require 2 consecutive failures before warning
    private List<(string Name, string? OriginalDns)>? _savedAdapterDns; // BUG-05: saved DNS for restore

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
    [ObservableProperty] private string _sharingInfo = "";
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
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _updateProgressText = "";
    [ObservableProperty] private string _updateCommitMessage = "";
    [ObservableProperty] private string _updateRemoteSha = "";

    // ── Connection Info ───────────────────────────────────────────

    [ObservableProperty] private string _localIp = "";
    [ObservableProperty] private string _serverIpDisplay = "";
    [ObservableProperty] private string _tunnelMode = "SOCKS5";
    [ObservableProperty] private string _socksPort = "10800";
    [ObservableProperty] private bool _showInfoPanel;
    [ObservableProperty] private string _publicIp = "—";

    // DNS settings
    [ObservableProperty] private string _selectedDnsProvider = "auto";
    [ObservableProperty] private string _customDnsPrimary = "";
    [ObservableProperty] private string _customDnsSecondary = "";
    [ObservableProperty] private string _dnsStatus = "";
    [ObservableProperty] private string _activeDns = "";
    [ObservableProperty] private string _dnsBenchmarkResults = "";
    [ObservableProperty] private bool _isDnsBenchmarking;

    // Theme
    [ObservableProperty] private string _selectedTheme = "dark";
    public string[] AvailableThemes => ThemeManager.AvailableThemes;

    // ── Diagnostics ──────────────────────────────────────────────

    [ObservableProperty] private bool _isDiagnosticRunning;
    [ObservableProperty] private string _diagnosticReport = "";
    [ObservableProperty] private string _diagnosticStatus = "";
    [ObservableProperty] private bool _showDiagnosticPanel;

    // ── Process Health ─────────────────────────────────────────

    [ObservableProperty] private string _paqetPid = "—";
    [ObservableProperty] private string _paqetMemory = "—";
    [ObservableProperty] private string _paqetUptime = "—";
    [ObservableProperty] private string _tun2socksPid = "—";
    [ObservableProperty] private string _tun2socksMemory = "—";
    [ObservableProperty] private string _tunAdapterStatus = "—";
    [ObservableProperty] private string _encryptionInfo = "—";
    [ObservableProperty] private string _kcpModeInfo = "—";
    [ObservableProperty] private string _connectionQuality = "—";
    [ObservableProperty] private string _connectionQualityColor = "#8b949e";

    // ── Live Log Viewer ────────────────────────────────────────

    [ObservableProperty] private bool _showLogViewer;
    [ObservableProperty] private string _logViewerText = "";
    [ObservableProperty] private string _logFilter = "ALL";
    private readonly List<LogEntry> _logEntries = new();

    // ── Server Management ────────────────────────────────────────

    [ObservableProperty] private string _serverSshHost = "";
    [ObservableProperty] private string _serverSshUser = "root";
    [ObservableProperty] private int _serverSshPort = 22;
    [ObservableProperty] private string _serverSshKeyPath = "";
    [ObservableProperty] private string _serverSshPassword = "";
    [ObservableProperty] private string _serverOutput = "";
    [ObservableProperty] private string _serverStatus = "";
    [ObservableProperty] private bool _isServerBusy;
    [ObservableProperty] private bool _showServerPanel;
    private readonly SshService _sshService = new();

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
        _diagnosticService = new DiagnosticService(paqetService);

        // Speed updates from monitor
        _networkMonitor.SpeedUpdated += OnSpeedUpdated;

        // Live log viewer subscription
        Logger.LogAdded += OnLogAdded;

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
        TunnelMode = IsFullSystemTunnel ? "TUNNEL" : "SOCKS5";
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
        IsProxySharingEnabled = appSettings.ProxySharingEnabled;
        IsAutoStartEnabled = _proxyService.IsAutoStartEnabled();
        IsStartBeforeLogonEnabled = _proxyService.IsStartBeforeLogonEnabled();

        // Load DNS settings
        SelectedDnsProvider = appSettings.DnsProvider ?? "auto";
        CustomDnsPrimary = appSettings.CustomDnsPrimary ?? "";
        CustomDnsSecondary = appSettings.CustomDnsSecondary ?? "";
        var (dnsPri, dnsSec) = DnsService.Resolve(appSettings);
        ActiveDns = $"{dnsPri}, {dnsSec}";
        Logger.Info($"DNS settings: provider={SelectedDnsProvider}, active={ActiveDns}");
        UpdateSharingInfo();

        // Load theme
        SelectedTheme = appSettings.Theme ?? "dark";

        // Load server SSH settings
        ServerSshHost = appSettings.ServerSshHost ?? "";
        ServerSshUser = string.IsNullOrEmpty(appSettings.ServerSshUser) ? "root" : appSettings.ServerSshUser;
        ServerSshPort = appSettings.ServerSshPort > 0 ? appSettings.ServerSshPort : 22;
        ServerSshKeyPath = appSettings.ServerSshKeyPath ?? "";
        ServerSshPassword = appSettings.ServerSshPassword ?? "";

        // ── Check running state (use IsReady for port-verified status)
        var running = _paqetService.IsRunning();
        var ready = running && PaqetService.IsPortListening();
        Logger.Info($"Initial state: running={running}, portReady={ready}");
        IsConnected = ready;
        if (IsConnected)
        {
            _connectedSince = DateTime.Now;
            var tunActive = _tunService.IsRunning();
            TunnelMode = tunActive ? "TUNNEL" : "SOCKS5";
            ConnectionStatus = tunActive ? "Connected (Full System)" : "Connected";
            _networkMonitor.Start();

            // Restore LAN sharing if enabled (portproxy is volatile — lost on reboot)
            if (IsProxySharingEnabled)
            {
                Logger.Info("Already connected — restoring LAN sharing...");
                _ = Task.Run(() =>
                {
                    _proxyService.RestoreSharingIfEnabled();
                    Logger.Info($"LAN sharing restored: portproxy={_proxyService.IsPortproxyActive()}");
                });
            }
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
            : IsConnected ? (TunnelMode == "TUNNEL" ? "Full system tunnel active" : "Connected") : "Ready";

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
        _userRequestedConnect = true;
        _reconnectAttempts = 0;
        _consecutiveHealthFailures = 0;
        ConnectionStatus = "Connecting...";
        StatusBarText = "Connecting...";

        // R3-12 fix: capture UI-bound property values before entering Task.Run
        var wantFullSystem = IsFullSystemTunnel;
        var wantSharing = IsProxySharingEnabled;
        Logger.Info($"ConnectAsync started (FullSystemTunnel={wantFullSystem})");

        await Task.Run(async () =>
        {
            // Ensure binary exists
            if (!_paqetService.BinaryExists())
            {
                Logger.Warn("ConnectAsync: binary not found");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ConnectionStatus = "Binary not found";
                    StatusBarText = "Setup required";
                    IsConnecting = false;
                });
                return;
            }

            // Step 1: Start paqet SOCKS5
            Logger.Info("Calling PaqetService.Start()...");
            Application.Current?.Dispatcher?.Invoke(() => ConnectionStatus = "Starting paqet...");
            var (success, message) = _paqetService.Start();
            Logger.Info($"Start() returned: success={success}, message={message}");

            if (!success)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
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
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsConnected = false;
                    ConnectionStatus = message;
                    StatusBarText = message;
                    IsConnecting = false;
                });
                return;
            }

            // Step 1b: Verify actual tunnel connectivity (not just port binding)
            Application.Current?.Dispatcher?.Invoke(() => ConnectionStatus = "Verifying tunnel...");
            var tunnelIp = await PaqetService.CheckTunnelConnectivityAsync(8000);
            if (tunnelIp == null)
            {
                Logger.Warn("Tunnel connectivity check failed — SOCKS5 port open but tunnel not working");
                // Don't fail — the tunnel may need more time to establish, continue with warning
                Logger.Info("Proceeding with connection despite connectivity check failure");
            }
            else
            {
                Logger.Info($"Tunnel verified — exit IP: {tunnelIp}");
                Application.Current?.Dispatcher?.Invoke(() => PublicIp = tunnelIp);
            }

            // Step 2: If TUN mode, start tun2socks
            if (wantFullSystem)
            {
                // Auto-download TUN binaries if missing
                if (!_tunService.AllBinariesExist())
                {
                    Application.Current?.Dispatcher?.Invoke(() => ConnectionStatus = "Downloading TUN binaries...");
                    Logger.Info("TUN binaries missing, downloading...");
                    var dlResult = await _tunService.DownloadBinariesAsync();
                    Logger.Info($"TUN download: success={dlResult.Success}, message={dlResult.Message}");
                    if (!dlResult.Success)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
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

                Application.Current?.Dispatcher?.Invoke(() => ConnectionStatus = "Starting TUN tunnel...");
                Logger.Info("Starting TUN tunnel...");
                var config = _configService.ReadPaqetConfig();
                var serverIp = config.ServerHost;
                var tunResult = await _tunService.StartAsync(serverIp, _configService.ReadAppSettings());
                Logger.Info($"TUN start: success={tunResult.Success}, message={tunResult.Message}");

                if (!tunResult.Success)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
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

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsConnected = true;
                _connectedSince = DateTime.Now;
                TunnelMode = wantFullSystem ? "TUNNEL" : "SOCKS5";
                ConnectionStatus = wantFullSystem ? "Connected (Full System)" : "Connected";
                StatusBarText = wantFullSystem ? "Full system tunnel active" : "Connected";
                _networkMonitor.Start();
                IsConnecting = false;
            });
            // Flush DNS cache after connecting (clear stale ISP DNS entries)
            _ = Task.Run(() =>
            {
                DnsService.FlushCache();
                // In SOCKS5-only mode, still force DNS to prevent ISP DNS leaks
                if (!wantFullSystem)
                {
                    var appSett = _configService.ReadAppSettings();
                    var (p, s) = DnsService.Resolve(appSett);
                    Logger.Info($"SOCKS5 mode: forcing DNS to {p}, {s} for leak prevention");
                    _savedAdapterDns = DnsService.ForceAllAdaptersDns(p, s); // BUG-05: save original DNS
                }
            });
            // Fetch public IP through tunnel if not already fetched
            if (tunnelIp == null)
                _ = FetchPublicIpAsync();

            // Restore LAN sharing if it was enabled (portproxy is volatile — lost on reboot)
            if (wantSharing)
            {
                Logger.Info("Restoring LAN sharing after connect...");
                _ = Task.Run(() =>
                {
                    _proxyService.RestoreSharingIfEnabled();
                    Logger.Info($"LAN sharing active: portproxy={_proxyService.IsPortproxyActive()}");
                });
            }
        });
    }

    private async Task DisconnectAsync()
    {
        IsConnecting = true;
        _userRequestedConnect = false; // User explicitly disconnected — no auto-reconnect
        _reconnectAttempts = 0;
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

            // Flush DNS cache and restore original DNS after disconnect
            DnsService.FlushCache();
            // BUG-05 fix: restore saved DNS instead of blindly resetting to DHCP
            if (_savedAdapterDns != null && _savedAdapterDns.Count > 0)
            {
                foreach (var (name, originalDns) in _savedAdapterDns)
                    DnsService.RestoreAdapterDns(name, originalDns);
                _savedAdapterDns = null;
            }
            else
            {
                // Fallback: restore to DHCP if we don't have saved DNS
                try
                {
                    var output = PaqetService.RunCommand("powershell", "-NoProfile -Command \"Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Select-Object -ExpandProperty Name\"");
                    foreach (var adapter in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = adapter.Trim();
                        if (!string.IsNullOrEmpty(name) && name != "PaqetTun")
                            DnsService.RestoreAdapterDns(name, null);
                    }
                }
                catch { }
            }
            DnsService.FlushCache();

            Application.Current?.Dispatcher?.Invoke(() =>
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
            Application.Current?.Dispatcher?.Invoke(() =>
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
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (result.Success)
                {
                    IsProxySharingEnabled = newState;
                    var settings = _configService.ReadAppSettings();
                    settings.ProxySharingEnabled = newState;
                    _configService.WriteAppSettings(settings);
                    StatusBarText = result.Message;
                    UpdateSharingInfo();
                }
                else
                {
                    StatusBarText = result.Message;
                }
                IsBusy = false;
            });
        });
    }

    private void UpdateSharingInfo()
    {
        if (IsProxySharingEnabled)
        {
            try
            {
                var localIp = PaqetService.RunCommand("powershell", "-NoProfile -Command \"(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } | Select-Object -First 1).IPAddress\"").Trim();
                SharingInfo = string.IsNullOrEmpty(localIp) ? $":{ProxyService.SHARING_PORT}" : $"{localIp}:{ProxyService.SHARING_PORT}";
            }
            catch { SharingInfo = $":{ProxyService.SHARING_PORT}"; }
        }
        else
        {
            SharingInfo = "";
        }
    }

    [RelayCommand]
    private async Task ToggleAutoStartAsync()
    {
        var newState = !IsAutoStartEnabled;
        IsBusy = true;
        await Task.Run(() =>
        {
            var result = _proxyService.SetAutoStart(newState);
            Application.Current?.Dispatcher?.Invoke(() =>
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
            Application.Current?.Dispatcher?.Invoke(() =>
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
    private void ToggleServerPanel()
    {
        ShowServerPanel = !ShowServerPanel;
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
            // Fetch through SOCKS5 proxy to get the tunnel exit IP (not local IP)
            var ip = await PaqetService.CheckTunnelConnectivityAsync(8000);
            Application.Current?.Dispatcher?.Invoke(() => PublicIp = ip ?? "—");
        }
        catch { Application.Current?.Dispatcher?.Invoke(() => PublicIp = "unavailable"); }
    }

    [RelayCommand]
    private void ToggleDebugMode()
    {
        DebugMode = !DebugMode;
        var settings = _configService.ReadAppSettings();
        settings.DebugMode = DebugMode;
        _configService.WriteAppSettings(settings);

        Logger.SetDebugMode(DebugMode);

        Logger.Info($"Debug mode toggled: {DebugMode}");
        StatusBarText = DebugMode
            ? $"Debug ON — {Logger.LogPath}"
            : "Debug mode disabled (verbose logging off).";
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
                var result = await _tunService.StartAsync(serverIp, _configService.ReadAppSettings());
                TunnelMode = result.Success ? "TUNNEL" : "SOCKS5";
                ConnectionStatus = result.Success ? "Connected (Full System)" : $"SOCKS5 only — TUN: {result.Message}";
            }
            else
            {
                ConnectionStatus = "Stopping TUN tunnel...";
                await _tunService.StopAsync();
                TunnelMode = "SOCKS5";
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
            Application.Current?.Dispatcher?.Invoke(() => SetupStatus = msg);
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
            Application.Current?.Dispatcher?.Invoke(() =>
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
            Application.Current?.Dispatcher?.Invoke(() =>
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
            Application.Current?.Dispatcher?.Invoke(() =>
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
        var (available, local, remote, message) = await UpdateService.CheckAsync();
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (available)
            {
                IsUpdateAvailable = true;
                var shortRemote = remote.Length >= 7 ? remote[..7] : remote; // R3-10 fix
                var shortLocal = local.Length >= 7 ? local[..7] : local;
                UpdateRemoteSha = shortRemote;
                UpdateCommitMessage = message;
                UpdateStatus = $"Update available ({shortRemote})";
                StatusBarText = $"Update available! {shortLocal} → {shortRemote}";
                Logger.Info($"Update banner shown: {message}");
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateStatus = "Up to date";
                StatusBarText = "No updates available.";
            }
        });
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (IsUpdating) return;
        IsUpdating = true;
        UpdateProgressText = "Preparing update...";
        Logger.Info("User initiated in-app update");

        var (success, msg) = await UpdateService.RunSilentUpdateAsync(progress =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                UpdateProgressText = progress;
                StatusBarText = progress;
            });
        });

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (success)
            {
                UpdateProgressText = "Updating... app will restart shortly";
                StatusBarText = "Updating... app will restart shortly";
            }
            else
            {
                IsUpdating = false;
                UpdateProgressText = "";
                UpdateStatus = msg;
                StatusBarText = $"Update failed: {msg}";
                Logger.Warn($"Update failed: {msg}");
            }
        });
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        IsUpdateAvailable = false;
        UpdateProgressText = "";
        StatusBarText = IsConnected ? (TunnelMode == "TUNNEL" ? "Full system tunnel active" : "Connected") : "Ready";
    }

    [RelayCommand]
    private async Task RunUpdateAsync()
    {
        await InstallUpdateAsync();
    }

    // ── DNS Commands ──────────────────────────────────────────────

    [RelayCommand]
    private async Task BenchmarkDnsAsync()
    {
        if (IsDnsBenchmarking) return;
        IsDnsBenchmarking = true;
        DnsStatus = $"⏳ Benchmarking {DnsService.Providers.Count} providers...";
        DnsBenchmarkResults = "";
        try
        {
            var results = await DnsService.BenchmarkAllAsync(LocalIp);
            var best = results.FirstOrDefault(); // NEW-25 fix: avoid crash on empty results
            if (best == null)
            {
                DnsStatus = "❌ All DNS benchmarks failed";
                return;
            }

            // Build formatted results table
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"{"#",-3} {"Provider",-22} {"Latency",8}  {"Server",-15}");
            lines.AppendLine(new string('─', 54));
            int rank = 1;
            foreach (var r in results)
            {
                var latStr = r.AvgLatencyMs >= 9999 ? "timeout" : $"{r.AvgLatencyMs:F0}ms";
                var marker = rank == 1 ? " ★" : "";
                lines.AppendLine($"{rank,-3} {r.Name,-22} {latStr,8}  {r.Primary,-15}{marker}");
                rank++;
            }
            DnsBenchmarkResults = lines.ToString();
            DnsStatus = $"★ Fastest: {best.Name} ({best.AvgLatencyMs:F0}ms)";
            Logger.Info($"DNS benchmark complete. Best: {best.Name} at {best.AvgLatencyMs:F1}ms");

            foreach (var r in results.Take(5))
                Logger.Info($"  {r.Name}: {r.AvgLatencyMs:F1}ms ({r.Primary})");
        }
        catch (Exception ex)
        {
            DnsStatus = $"Benchmark failed: {ex.Message}";
            DnsBenchmarkResults = "";
            Logger.Error("DNS benchmark failed", ex);
        }
        finally
        {
            IsDnsBenchmarking = false;
        }
    }

    [RelayCommand]
    private void SetDnsProvider(string provider)
    {
        Logger.Info($"DNS provider changed: {SelectedDnsProvider} → {provider}");
        SelectedDnsProvider = provider;
        var settings = _configService.ReadAppSettings();
        settings.DnsProvider = provider;
        settings.CustomDnsPrimary = CustomDnsPrimary;
        settings.CustomDnsSecondary = CustomDnsSecondary;
        _configService.WriteAppSettings(settings);

        var (p, s) = DnsService.Resolve(settings);
        ActiveDns = $"{p}, {s}";

        // Apply DNS immediately if connected
        if (IsConnected)
        {
            if (IsFullSystemTunnel && _tunService.IsRunning())
                DnsService.ForceAdapterDns("PaqetTun", p, s);
            DnsService.ForceAllAdaptersDns(p, s, excludeAdapter: IsFullSystemTunnel ? "PaqetTun" : null);
            DnsService.FlushCache();
            DnsStatus = $"Applied: {p}";
            Logger.Info($"DNS live-updated to {p}, {s}");
        }
    }

    [RelayCommand]
    private async Task AutoSelectDnsAsync()
    {
        DnsStatus = "Auto-selecting best DNS...";
        try
        {
            var (providerId, primary, secondary) = await DnsService.AutoSelectAsync(LocalIp);
            SelectedDnsProvider = providerId;
            ActiveDns = $"{primary}, {secondary}";

            var settings = _configService.ReadAppSettings();
            settings.DnsProvider = providerId;
            _configService.WriteAppSettings(settings);

            DnsStatus = $"Selected: {DnsService.Providers[providerId].Name} (fastest)";

            if (IsConnected)
            {
                if (IsFullSystemTunnel && _tunService.IsRunning())
                    DnsService.ForceAdapterDns("PaqetTun", primary, secondary);
                DnsService.ForceAllAdaptersDns(primary, secondary, excludeAdapter: IsFullSystemTunnel ? "PaqetTun" : null);
                DnsService.FlushCache();
            }
        }
        catch (Exception ex)
        {
            DnsStatus = $"Auto-select failed: {ex.Message}";
        }
    }

    // ── Theme ─────────────────────────────────────────────────────

    [RelayCommand]
    private void SetTheme(string theme)
    {
        Logger.Info($"Theme changed: {SelectedTheme} → {theme}");
        SelectedTheme = theme;
        ThemeManager.Apply(theme);

        var settings = _configService.ReadAppSettings();
        settings.Theme = theme;
        _configService.WriteAppSettings(settings);
    }

    // ── Diagnostics ───────────────────────────────────────────────

    [RelayCommand]
    private async Task RunDiagnosticAsync()
    {
        if (IsDiagnosticRunning) return;
        IsDiagnosticRunning = true;
        DiagnosticStatus = "Running full diagnostic...";
        DiagnosticReport = "";
        try
        {
            var report = await _diagnosticService.RunFullDiagnosticAsync();
            DiagnosticReport = DiagnosticService.FormatReport(report);
            DiagnosticStatus = $"Complete ({report.DurationMs / 1000:F1}s)";
        }
        catch (Exception ex)
        {
            DiagnosticReport = $"Diagnostic failed: {ex.Message}";
            DiagnosticStatus = "Failed";
            Logger.Error("Diagnostic run failed", ex);
        }
        finally
        {
            IsDiagnosticRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunQuickCheckAsync()
    {
        if (IsDiagnosticRunning) return;
        IsDiagnosticRunning = true;
        DiagnosticStatus = "Running quick check...";
        DiagnosticReport = "";
        try
        {
            var report = await _diagnosticService.RunQuickCheckAsync();
            DiagnosticReport = DiagnosticService.FormatReport(report);
            DiagnosticStatus = $"Complete ({report.DurationMs / 1000:F1}s)";
        }
        catch (Exception ex)
        {
            DiagnosticReport = $"Quick check failed: {ex.Message}";
            DiagnosticStatus = "Failed";
            Logger.Error("Quick check failed", ex);
        }
        finally
        {
            IsDiagnosticRunning = false;
        }
    }

    [RelayCommand]
    private void ShowLatestDiagnostic()
    {
        var latest = Models.DiagnosticReport.LoadLatest();
        if (latest != null)
        {
            DiagnosticReport = DiagnosticService.FormatReport(latest);
            DiagnosticStatus = $"Loaded report from {latest.Timestamp:yyyy-MM-dd HH:mm}";
        }
        else
        {
            DiagnosticReport = "No diagnostic reports found. Run a diagnostic first.";
            DiagnosticStatus = "";
        }
    }

    [RelayCommand]
    private void CompareDiagnostics()
    {
        var reports = Models.DiagnosticReport.LoadAll(2);
        if (reports.Count >= 2)
        {
            DiagnosticReport = DiagnosticService.FormatComparison(reports[0], reports[1]);
            DiagnosticStatus = "Comparison loaded";
        }
        else
        {
            DiagnosticReport = "Need at least 2 reports to compare. Run diagnostics multiple times.";
            DiagnosticStatus = "";
        }
    }

    [RelayCommand]
    private void ToggleDiagnosticPanel() => ShowDiagnosticPanel = !ShowDiagnosticPanel;

    // ── Live Log Viewer ───────────────────────────────────────────

    [RelayCommand]
    private void ToggleLogViewer()
    {
        ShowLogViewer = !ShowLogViewer;
        if (ShowLogViewer) RefreshLogViewer();
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logger.ClearBuffer();
        _logEntries.Clear();
        LogViewerText = "";
    }

    [RelayCommand]
    private void SetLogFilter(string filter)
    {
        LogFilter = filter;
        RefreshLogViewer();
    }

    private readonly object _logEntriesLock = new(); // R3-07 fix: thread-safe log list

    private void OnLogAdded(LogEntry entry)
    {
        try
        {
            lock (_logEntriesLock)
            {
                _logEntries.Add(entry);
                if (_logEntries.Count > 500)
                    _logEntries.RemoveAt(0);
            }

            if (!ShowLogViewer) return;
            if (LogFilter != "ALL" && entry.Level != LogFilter) return;

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                LogViewerText += entry.Formatted + "\n";
                // Trim if too long
                if (LogViewerText.Length > 50000)
                    LogViewerText = LogViewerText[^30000..];
            }));
        }
        catch { }
    }

    private void RefreshLogViewer()
    {
        var entries = Logger.GetRecentLogs(200);
        if (LogFilter != "ALL")
            entries = entries.Where(e => e.Level == LogFilter).ToList();
        LogViewerText = string.Join("\n", entries.Select(e => e.Formatted));
    }

    // ── Process Health ────────────────────────────────────────────

    private void RefreshProcessHealth()
    {
        try
        {
            // R3-09 fix: dispose all Process objects from GetProcessesByName to avoid handle leaks
            var paqetProcs = System.Diagnostics.Process.GetProcessesByName("paqet_windows_amd64");
            try
            {
                var paqetProc = paqetProcs.FirstOrDefault();
                if (paqetProc != null)
                {
                    PaqetPid = paqetProc.Id.ToString();
                    PaqetMemory = $"{paqetProc.WorkingSet64 / (1024 * 1024)} MB";
                    var uptime = DateTime.Now - paqetProc.StartTime;
                    PaqetUptime = uptime.TotalHours >= 1
                        ? uptime.ToString(@"hh\:mm\:ss")
                        : uptime.ToString(@"mm\:ss");
                }
                else
                {
                    PaqetPid = "—";
                    PaqetMemory = "—";
                    PaqetUptime = "—";
                }
            }
            finally { foreach (var p in paqetProcs) p.Dispose(); }

            var t2sProcs = System.Diagnostics.Process.GetProcessesByName("tun2socks");
            try
            {
                var t2sProc = t2sProcs.FirstOrDefault();
                if (t2sProc != null)
                {
                    Tun2socksPid = t2sProc.Id.ToString();
                    Tun2socksMemory = $"{t2sProc.WorkingSet64 / (1024 * 1024)} MB";
                }
                else
                {
                    Tun2socksPid = "—";
                    Tun2socksMemory = "—";
                }
            }
            finally { foreach (var p in t2sProcs) p.Dispose(); }

            // TUN adapter
            TunAdapterStatus = _tunService.IsRunning() ? "Active" : "Inactive";

            // R3-18 fix: read encryption and KCP mode from actual config
            if (System.IO.File.Exists(AppPaths.PaqetConfigPath))
            {
                var config = PaqetConfig.FromYaml(System.IO.File.ReadAllText(AppPaths.PaqetConfigPath));
                EncryptionInfo = string.IsNullOrEmpty(config.Protocol) ? "KCP" : config.Protocol.ToUpperInvariant();
                KcpModeInfo = string.IsNullOrEmpty(config.KcpMode) ? "fast" : config.KcpMode;
            }

            // Connection quality based on health check result
            if (IsConnected)
            {
                var status = ConnectionStatus;
                if (status.Contains("tunnel check failed"))
                {
                    ConnectionQuality = "⚠ Degraded";
                    ConnectionQualityColor = "#d29922";
                }
                else if (status.Contains("Reconnecting"))
                {
                    ConnectionQuality = "⚠ Reconnecting";
                    ConnectionQualityColor = "#d29922";
                }
                else
                {
                    ConnectionQuality = "● Excellent";
                    ConnectionQualityColor = "#3fb950";
                }
            }
            else
            {
                ConnectionQuality = "○ Offline";
                ConnectionQualityColor = "#8b949e";
            }
        }
        catch (Exception ex) { Logger.Debug($"RefreshProcessHealth: {ex.Message}"); } // R3-17 fix
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var logDir = Logger.LogDir;
            if (System.IO.Directory.Exists(logDir))
                System.Diagnostics.Process.Start("explorer.exe", logDir);
        }
        catch { }
    }

    [RelayCommand]
    private void OpenDiagnosticsFolder()
    {
        try
        {
            var diagDir = AppPaths.DiagnosticsDir;
            if (System.IO.Directory.Exists(diagDir))
                System.Diagnostics.Process.Start("explorer.exe", diagDir);
        }
        catch { }
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

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (portReady != IsConnected)
                {
                    IsConnected = portReady;
                    var tunActive = _tunService.IsRunning();
                    TunnelMode = tunActive ? "TUNNEL" : "SOCKS5";
                    ConnectionStatus = portReady
                        ? (tunActive ? "Connected (Full System)" : "Connected")
                        : running ? "Port not ready" : "Disconnected";
                    Logger.Info($"OnStatusTick: ConnectionStatus={ConnectionStatus}");
                    if (portReady)
                    {
                        _connectedSince = DateTime.Now;
                        _reconnectAttempts = 0;
                        _networkMonitor.Start();
                    }
                    else
                    {
                        _networkMonitor.Stop();
                        DownloadSpeed = "0 B/s";
                        UploadSpeed = "0 B/s";
                        ConnectionTime = "";

                        // Auto-reconnect if user had explicitly connected
                        if (_userRequestedConnect && !IsConnecting && _reconnectAttempts < MAX_RECONNECT_ATTEMPTS)
                        {
                            _reconnectAttempts++;
                            var delay = RECONNECT_BASE_DELAY_MS * _reconnectAttempts;
                            Logger.Info($"Auto-reconnect attempt {_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS} in {delay}ms");
                            ConnectionStatus = $"Reconnecting ({_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS})...";
                            StatusBarText = ConnectionStatus;
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(delay);
                                if (_userRequestedConnect && !IsConnected && !IsConnecting)
                                    if (Application.Current?.Dispatcher != null)
                                        await Application.Current.Dispatcher.InvokeAsync(async () => await ConnectInternalAsync());
                            });
                        }
                        else if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
                        {
                            Logger.Warn("Auto-reconnect exhausted all attempts");
                            ConnectionStatus = "Disconnected (reconnect failed)";
                            StatusBarText = "Reconnect failed — click to retry";
                            _userRequestedConnect = false;
                        }
                    }
                }

                // Periodic health check — verify actual tunnel connectivity
                if (IsConnected && !IsConnecting)
                {
                    _healthCheckCounter++;
                    if (_healthCheckCounter >= HEALTH_CHECK_INTERVAL)
                    {
                        _healthCheckCounter = 0;
                        _ = Task.Run(async () =>
                        {
                            var ip = await PaqetService.CheckTunnelConnectivityAsync(8000);
                            if (ip == null && IsConnected)
                            {
                                var failures = System.Threading.Interlocked.Increment(ref _consecutiveHealthFailures);
                                Logger.Warn($"Health check failed ({failures}/{HEALTH_FAIL_THRESHOLD})");
                                if (failures >= HEALTH_FAIL_THRESHOLD)
                                {
                                    Application.Current?.Dispatcher?.Invoke(() =>
                                    {
                                        ConnectionStatus = "Connected (tunnel check failed)";
                                    });
                                }
                            }
                            else if (ip != null)
                            {
                                var prevFailures = System.Threading.Interlocked.Exchange(ref _consecutiveHealthFailures, 0);
                                if (prevFailures > 0)
                                    Logger.Info($"Health check recovered after {prevFailures} failure(s)");
                                Application.Current?.Dispatcher?.Invoke(() =>
                                {
                                    // Restore normal status if it was degraded
                                    if (ConnectionStatus.Contains("tunnel check failed"))
                                        ConnectionStatus = IsFullSystemTunnel ? "Connected (Full System)" : "Connected";
                                    if (string.IsNullOrEmpty(PublicIp) || PublicIp == "—")
                                        PublicIp = ip;
                                });
                                Logger.Debug($"Health check OK — exit IP: {ip}");
                            }
                        });
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

                // Update process health info (every tick when connected, or every 10th tick otherwise)
                _healthRefreshCounter++;
                if (_healthRefreshCounter >= (IsConnected ? 1 : 10))
                {
                    _healthRefreshCounter = 0;
                    RefreshProcessHealth();
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
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var latest = _networkMonitor.Latest;
                DownloadSpeed = NetworkMonitorService.FormatSpeed(latest.DownloadSpeed);
                UploadSpeed = NetworkMonitorService.FormatSpeed(latest.UploadSpeed);

                lock (_networkMonitor)
                {
                    var hist = _networkMonitor.GetHistorySnapshot(); // BUG-07 fix: thread-safe copy
                    DownloadHistory = new List<double>(hist.ConvertAll(s => s.DownloadSpeed));
                    UploadHistory = new List<double>(hist.ConvertAll(s => s.UploadSpeed));
                    SpeedHistory = new List<double>(hist.ConvertAll(s => s.DownloadSpeed + s.UploadSpeed));

                    var peak = hist.Count > 0 ? hist.Max(s => s.DownloadSpeed + s.UploadSpeed) : 0;
                    PeakSpeed = peak;

                    // BUG-11 fix: calculate total from byte counters, not speed sums
                    if (hist.Count >= 2)
                    {
                        var first = hist[0];
                        var last = hist[^1];
                        double totalDl = Math.Max(0, last.BytesReceived - first.BytesReceived);
                        double totalUl = Math.Max(0, last.BytesSent - first.BytesSent);
                        TotalTransferred = NetworkMonitorService.FormatBytes(totalDl + totalUl);
                    }
                    else
                    {
                        TotalTransferred = "0 B";
                    }
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
        Application.Current?.Dispatcher?.Invoke(() => IsSystemProxyEnabled = false);

        // Reset WinHTTP proxy (Windows Update, system services)
        try
        {
            PaqetService.RunCommand("netsh", "winhttp reset proxy");
            Logger.Info("Reset WinHTTP proxy for TUN mode");
        }
        catch (Exception ex) { Logger.Debug($"WinHTTP reset: {ex.Message}"); }
    }

    /// <summary>Internal reconnect — doesn't reset user intent or attempt counter.</summary>
    private async Task ConnectInternalAsync()
    {
        if (IsConnecting || IsConnected) return;
        IsConnecting = true;
        ConnectionStatus = $"Reconnecting ({_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS})...";
        Logger.Info($"ConnectInternalAsync: reconnect attempt {_reconnectAttempts}");

        await Task.Run(async () =>
        {
            if (!_paqetService.BinaryExists())
            {
                Application.Current?.Dispatcher?.Invoke(() => { IsConnecting = false; });
                return;
            }

            var (success, message) = _paqetService.Start();
            Logger.Info($"Reconnect Start(): success={success}, message={message}");

            if (!success || !PaqetService.IsPortListening())
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ConnectionStatus = $"Reconnect failed: {message}";
                    IsConnecting = false;
                });
                return;
            }

            // If TUN mode was active, restart TUN
            if (IsFullSystemTunnel && _tunService.AllBinariesExist())
            {
                var config = _configService.ReadPaqetConfig();
                var tunResult = await _tunService.StartAsync(config.ServerHost, _configService.ReadAppSettings());
                if (!tunResult.Success)
                    Logger.Warn($"TUN reconnect failed: {tunResult.Message}");
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsConnected = true;
                _connectedSince = DateTime.Now;
                _reconnectAttempts = 0;
                var tunActive = _tunService.IsRunning();
                TunnelMode = tunActive ? "TUNNEL" : "SOCKS5";
                ConnectionStatus = tunActive ? "Reconnected (Full System)" : "Reconnected";
                StatusBarText = "Reconnected";
                _networkMonitor.Start();
                IsConnecting = false;
            });
            _ = Task.Run(() => DnsService.FlushCache());
        });
    }

    public void Cleanup()
    {
        Logger.LogAdded -= OnLogAdded;
        _networkMonitor.SpeedUpdated -= OnSpeedUpdated;
        _statusTimer.Stop();
        _statusTimer.Dispose();
        _networkMonitor.Stop();
        _networkMonitor.Dispose();

        // BUG-20 fix: stop SOCKS5 tunnel if connected
        if (IsConnected)
        {
            try { _paqetService.Stop(); } catch { }
        }

        // Stop TUN if running
        if (_tunService.IsRunning())
        {
            Logger.Info("Cleanup: stopping TUN tunnel");
            _tunService.StopAsync().GetAwaiter().GetResult();
        }

        // R3-01/02/03 fix: proxy cleanup is handled by ProxyService.OnShutdown() in App.OnExit
        // Do NOT duplicate proxy restore here — Cleanup() focuses on process/TUN teardown only
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

    // ── Server Management Commands ─────────────────────────────

    private void SaveServerSettings()
    {
        var settings = _configService.ReadAppSettings();
        settings.ServerSshHost = ServerSshHost;
        settings.ServerSshUser = ServerSshUser;
        settings.ServerSshPort = ServerSshPort;
        settings.ServerSshKeyPath = ServerSshKeyPath;
        settings.ServerSshPassword = ServerSshPassword;
        _configService.WriteAppSettings(settings);
    }

    private AppSettings GetServerSettings()
    {
        var settings = _configService.ReadAppSettings();
        settings.ServerSshHost = ServerSshHost;
        settings.ServerSshUser = ServerSshUser;
        settings.ServerSshPort = ServerSshPort;
        settings.ServerSshKeyPath = ServerSshKeyPath;
        settings.ServerSshPassword = ServerSshPassword;
        return settings;
    }

    [RelayCommand]
    private void SaveServerConfig()
    {
        SaveServerSettings();
        ServerStatus = "Settings saved";
        Logger.Info($"Server SSH config saved: {ServerSshHost}:{ServerSshPort} user={ServerSshUser}");
    }

    [RelayCommand]
    private async Task TestServerConnection()
    {
        if (IsServerBusy) return;
        SaveServerSettings();
        IsServerBusy = true;
        ServerStatus = "Testing connection...";
        ServerOutput = "";
        try
        {
            var (ok, msg) = await _sshService.TestConnectionAsync(GetServerSettings());
            ServerStatus = ok ? "Connected" : "Failed";
            ServerOutput = msg;
        }
        catch (Exception ex)
        {
            ServerStatus = "Error";
            ServerOutput = ex.Message;
        }
        finally { IsServerBusy = false; }
    }

    [RelayCommand]
    private async Task RunServerAction(string command)
    {
        if (IsServerBusy) return;
        SaveServerSettings();
        IsServerBusy = true;
        ServerStatus = $"Running {command}...";
        ServerOutput = "";
        try
        {
            var (ok, output) = await _sshService.RunServerCommandAsync(
                GetServerSettings(), command,
                msg => Application.Current?.Dispatcher?.BeginInvoke(
                    new Action(() => { ServerStatus = msg; })));
            ServerStatus = ok ? "Done" : "Failed";
            ServerOutput = output;
        }
        catch (Exception ex)
        {
            ServerStatus = "Error";
            ServerOutput = ex.Message;
        }
        finally { IsServerBusy = false; }
    }
}
