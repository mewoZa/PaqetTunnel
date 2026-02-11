using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PaqetTunnel.Services;
using PaqetTunnel.ViewModels;
using PaqetTunnel.Views;
using Forms = System.Windows.Forms;

namespace PaqetTunnel;

public partial class App : Application
{
    private static Mutex? _mutex;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private ProxyService? _proxyService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // BUG-19 fix: wrap async void in try-catch to prevent silent crash
        try
        {
            await OnStartupCoreAsync(e);
        }
        catch (Exception ex)
        {
            Services.Logger.Error("Fatal error during startup", ex);
            MessageBox.Show($"PaqetTunnel failed to start:\n{ex.Message}", "Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task OnStartupCoreAsync(StartupEventArgs e)
    {
        // ── Single instance check ──────────────────────────────────
        _mutex = new Mutex(true, "Global\\PaqetTunnel_SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Signal existing instance to show its window
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_PAQET_SHOW, IntPtr.Zero, IntPtr.Zero);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        // ── Ensure data directories exist ──────────────────────────
        AppPaths.EnsureDirectories();

        // ── Initialize logger (always on; debug mode adds verbose output) ──
        var configService = new ConfigService();
        var appSettings = configService.ReadAppSettings();
        Services.Logger.Initialize(appSettings.DebugMode);
        Services.Logger.CleanOldLogs();
        Services.Logger.Info("=== App OnStartup ===");

        // ── Apply saved theme ──────────────────────────────────────
        ThemeManager.LoadFromSettings(appSettings);

        // ── Migrate from old %USERPROFILE%\paqet if present ────────
        SetupService.MigrateFromOldLocation();

        // ── Migrate config port 1080→10800 (svchost conflict) ──────
        configService.MigrateConfigPort();

        // ── Initialize services ────────────────────────────────────
        var paqetService = new PaqetService();
        var proxyService = new ProxyService();
        _proxyService = proxyService;
        proxyService.OnStartup(appSettings.ProxySharingEnabled); // Preserve portproxy if sharing was enabled
        var networkMonitor = new NetworkMonitorService();
        var tunService = new TunService();
        var setupService = new SetupService(paqetService, tunService);

        Services.Logger.Info("Services initialized");

        // ── Create ViewModel ───────────────────────────────────────
        _viewModel = new MainViewModel(paqetService, proxyService, networkMonitor, configService, setupService, tunService);

        // ── Create main window ─────────────────────────────────────
        _mainWindow = new MainWindow { DataContext = _viewModel };

        // ── System tray icon ───────────────────────────────────────
        CreateTrayIcon();

        // R4-16: Show window BEFORE InitializeAsync so user sees UI immediately
        // (unless StartMinimized is enabled — then stay in tray only)
        if (!appSettings.StartMinimized)
            ShowWindow();
        else
            Services.Logger.Info("StartMinimized=true — window hidden, tray only");

        Services.Logger.Info("Starting InitializeAsync...");

        // ── Start services ─────────────────────────────────────────
        await _viewModel.InitializeAsync();

        // R4-01 fix: --connect flag only fires if InitializeAsync didn't already connect
        // (auto-connect in InitializeAsync already handles AutoConnectOnLaunch setting)
        var shouldAutoConnect = (e.Args.Length > 0 && e.Args[0] == "--connect");
        if (shouldAutoConnect && !_viewModel.IsConnected && !_viewModel.IsConnecting && !_viewModel.NeedsSetup)
        {
            Services.Logger.Info("Auto-connect requested via --connect flag (not already connected)");
            await _viewModel.ConnectFromStartupAsync();
        }
    }

    private System.ComponentModel.PropertyChangedEventHandler? _trayIconHandler;

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Paqet Tunnel",
            Icon = GenerateTrayIcon(false),
            Visible = true
        };

        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ToggleWindow();
        };

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) => ShowWindowAsTrayPopup());
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Quit", null, (s, e) => QuitApp());
        _trayIcon.ContextMenuStrip = contextMenu;

        // Listen for status changes to update tray icon
        // R5 life-02: store handler reference so we can unsubscribe on exit
        if (_viewModel != null)
        {
            _trayIconHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsConnected) && _trayIcon != null)
                {
                    var oldIcon = _trayIcon.Icon;
                    _trayIcon.Icon = GenerateTrayIcon(_viewModel.IsConnected);
                    _trayIcon.Text = _viewModel.IsConnected ? "Paqet — Connected" : "Paqet — Disconnected";
                    oldIcon?.Dispose();
                }
            };
            _viewModel.PropertyChanged += _trayIconHandler;
        }
    }

    private static Icon GenerateTrayIcon(bool connected)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Match logo gradient: #4facfe → #00f2fe (blue-cyan)
        var color1 = connected ? Color.FromArgb(79, 172, 254) : Color.FromArgb(139, 148, 158);
        var color2 = connected ? Color.FromArgb(0, 242, 254) : Color.FromArgb(110, 118, 128);

        // Motion dots (3 dots, increasing opacity like the logo)
        using var dot1 = new SolidBrush(Color.FromArgb(77, color1));
        using var dot2 = new SolidBrush(Color.FromArgb(153, color1));
        using var dot3 = new SolidBrush(Color.FromArgb(255, color1));
        g.FillEllipse(dot1, 1f, 14f, 3.5f, 3.5f);
        g.FillEllipse(dot2, 5.5f, 13.5f, 4f, 4f);
        g.FillEllipse(dot3, 10.5f, 13f, 4.5f, 4.5f);

        // Double chevron (>>) with gradient pen
        using var gradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new PointF(14f, 0f), new PointF(30f, 30f), color1, color2);
        using var pen = new Pen(gradBrush, 2.8f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };

        // First chevron
        g.DrawLines(pen, new PointF[] {
            new(15f, 6f), new(23f, 16f), new(15f, 26f)
        });

        // Second chevron
        g.DrawLines(pen, new PointF[] {
            new(22f, 6f), new(30f, 16f), new(22f, 26f)
        });

        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    public void ToggleWindow()
    {
        if (_mainWindow == null) return;
        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            ShowWindowAsTrayPopup();
        }
    }

    public void ShowWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.AutoHideEnabled = false;
        PositionWindowNearTray();
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    /// <summary>Show as tray popup (auto-hides on deactivate).</summary>
    private void ShowWindowAsTrayPopup()
    {
        if (_mainWindow == null) return;
        PositionWindowNearTray();
        _mainWindow.AutoHideEnabled = true;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void PositionWindowNearTray()
    {
        if (_mainWindow == null) return;

        // R5 life-03: use ActualWidth/Height if layout has completed, else fall back to Width/Height
        var w = _mainWindow.ActualWidth > 0 ? _mainWindow.ActualWidth : _mainWindow.Width;
        var h = _mainWindow.ActualHeight > 0 ? _mainWindow.ActualHeight : _mainWindow.Height;

        var workArea = SystemParameters.WorkArea;
        _mainWindow.Left = Math.Max(workArea.Left, workArea.Right - w - 12);
        _mainWindow.Top = Math.Max(workArea.Top, workArea.Bottom - h - 12);
    }

    private void QuitApp()
    {
        // R4-04: try/finally ensures mutex is always released even if Cleanup throws
        try
        {
            // R5 life-02: unsubscribe PropertyChanged handler before cleanup
            if (_viewModel != null && _trayIconHandler != null)
                _viewModel.PropertyChanged -= _trayIconHandler;

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _viewModel?.Cleanup();
            _proxyService?.OnShutdown();
        }
        catch (Exception ex)
        {
            Services.Logger.Error("QuitApp error (continuing shutdown)", ex);
        }
        finally
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); _mutex = null; } catch { }
            Shutdown(0);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // R4-20: ensure proxy cleanup happens even on unexpected shutdown paths
        try { _proxyService?.OnShutdown(); } catch { }
        try { _trayIcon?.Dispose(); _trayIcon = null; } catch { }
        try { _mutex?.Dispose(); _mutex = null; } catch { }
        base.OnExit(e);
    }
}

/// <summary>
/// Native Win32 interop for single-instance communication.
/// </summary>
internal static class NativeMethods
{
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    public static readonly uint WM_PAQET_SHOW = RegisterWindowMessage("WM_PAQET_SHOW");

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);
}
