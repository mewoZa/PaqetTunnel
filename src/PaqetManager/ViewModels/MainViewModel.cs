using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaqetManager.Models;
using PaqetManager.Services;

namespace PaqetManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PaqetService _paqetService;
    private readonly ProxyService _proxyService;
    private readonly NetworkMonitorService _networkMonitor;
    private readonly ConfigService _configService;
    private readonly SetupService _setupService;
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

    // ── Toggles ───────────────────────────────────────────────────

    [ObservableProperty] private bool _isSystemProxyEnabled;
    [ObservableProperty] private bool _isProxySharingEnabled;
    [ObservableProperty] private bool _isAutoStartEnabled;

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
    [ObservableProperty] private string _paqetVersion = "";
    [ObservableProperty] private string _interfaceList = "";
    [ObservableProperty] private string _pingResult = "";
    [ObservableProperty] private string _fullVersionInfo = "";

    public MainViewModel(
        PaqetService paqetService,
        ProxyService proxyService,
        NetworkMonitorService networkMonitor,
        ConfigService configService,
        SetupService setupService)
    {
        _paqetService = paqetService;
        _proxyService = proxyService;
        _networkMonitor = networkMonitor;
        _configService = configService;
        _setupService = setupService;

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
        Logger.Info($"Config loaded: server={config.ServerAddr}, interface={config.Interface}, socks={config.SocksListen}");

        // Load toggle states
        IsSystemProxyEnabled = _proxyService.IsSystemProxyEnabled();
        IsProxySharingEnabled = _proxyService.IsProxySharingEnabled();
        IsAutoStartEnabled = _proxyService.IsAutoStartEnabled();

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

        Logger.Info("InitializeAsync complete");
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
        Logger.Info("ConnectAsync started");

        await Task.Run(() =>
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

            Logger.Info("Calling PaqetService.Start()...");
            var (success, message) = _paqetService.Start();
            Logger.Info($"Start() returned: success={success}, message={message}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    // Verify port is actually listening before claiming connected
                    var portReady = PaqetService.IsPortListening();
                    Logger.Info($"Post-start port check: {portReady}");

                    if (portReady)
                    {
                        IsConnected = true;
                        _connectedSince = DateTime.Now;
                        ConnectionStatus = "Connected";
                        StatusBarText = "Connected";
                        _networkMonitor.Start();
                    }
                    else
                    {
                        IsConnected = false;
                        ConnectionStatus = message;
                        StatusBarText = message;
                    }
                }
                else
                {
                    ConnectionStatus = message;
                    StatusBarText = message;
                    Logger.Warn($"ConnectAsync failed: {message}");
                }
                IsConnecting = false;
            });
        });
    }

    private async Task DisconnectAsync()
    {
        IsConnecting = true;
        ConnectionStatus = "Disconnecting...";

        await Task.Run(() =>
        {
            _networkMonitor.Stop();
            var (success, message) = _paqetService.Stop();

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
                    ConnectionStatus = portReady ? "Connected" : running ? "Port not ready" : "Disconnected";
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

                // Update history for graph
                lock (_networkMonitor)
                {
                    SpeedHistory = new List<double>(_networkMonitor.History
                        .ConvertAll(s => s.DownloadSpeed + s.UploadSpeed));
                }
            });
        }
        catch { /* Swallow UI errors */ }
    }

    public void Cleanup()
    {
        _statusTimer.Stop();
        _statusTimer.Dispose();
        _networkMonitor.Stop();
        _networkMonitor.Dispose();
    }
}
