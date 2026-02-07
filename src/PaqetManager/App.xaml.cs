using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PaqetManager.Services;
using PaqetManager.ViewModels;
using PaqetManager.Views;
using Forms = System.Windows.Forms;

namespace PaqetManager;

public partial class App : Application
{
    private static Mutex? _mutex;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── Single instance check ──────────────────────────────────
        _mutex = new Mutex(true, "Global\\PaqetManager_SingleInstance", out bool isNew);
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

        // ── Initialize logger (read settings to check debug mode) ──
        var configService = new ConfigService();
        var appSettings = configService.ReadAppSettings();
        Services.Logger.Initialize(appSettings.DebugMode);
        Services.Logger.CleanOldLogs();
        Services.Logger.Info("=== App OnStartup ===");

        // ── Migrate from old %USERPROFILE%\paqet if present ────────
        SetupService.MigrateFromOldLocation();

        // ── Initialize services ────────────────────────────────────
        var paqetService = new PaqetService();
        var proxyService = new ProxyService();
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

        Services.Logger.Info("Starting InitializeAsync...");

        // ── Start services ─────────────────────────────────────────
        await _viewModel.InitializeAsync();

        Services.Logger.Info("InitializeAsync complete");

        // ── Auto-connect if --connect flag passed ──────────────────
        if (e.Args.Length > 0 && e.Args[0] == "--connect")
        {
            Services.Logger.Info("Auto-connect requested via --connect flag");
            if (!_viewModel.IsConnected && !_viewModel.NeedsSetup)
            {
                await Task.Delay(500);
                _viewModel.ToggleConnectionCommand.Execute(null);
            }
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Paqet Manager",
            Icon = GenerateTrayIcon(false),
            Visible = true
        };

        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ToggleWindow();
        };

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Quit", null, (s, e) => QuitApp());
        _trayIcon.ContextMenuStrip = contextMenu;

        // Listen for status changes to update tray icon
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsConnected) && _trayIcon != null)
                {
                    var oldIcon = _trayIcon.Icon;
                    _trayIcon.Icon = GenerateTrayIcon(_viewModel.IsConnected);
                    _trayIcon.Text = _viewModel.IsConnected ? "Paqet — Connected" : "Paqet — Disconnected";
                    oldIcon?.Dispose();
                }
            };
        }
    }

    private static Icon GenerateTrayIcon(bool connected)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Outer circle
        var circleColor = connected ? Color.FromArgb(63, 185, 80) : Color.FromArgb(139, 148, 158);
        using var brush = new SolidBrush(circleColor);
        g.FillEllipse(brush, 2, 2, size - 4, size - 4);

        // Inner dark circle
        using var innerBrush = new SolidBrush(Color.FromArgb(13, 17, 23));
        g.FillEllipse(innerBrush, 6, 6, size - 12, size - 12);

        // Power icon (vertical line + arc)
        using var pen = new Pen(circleColor, 2.5f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        g.DrawLine(pen, size / 2, 9, size / 2, 16);
        g.DrawArc(pen, 9, 9, size - 18, size - 18, -60, 300);

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
            ShowWindow();
        }
    }

    public void ShowWindow()
    {
        if (_mainWindow == null) return;
        PositionWindowNearTray();
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void PositionWindowNearTray()
    {
        if (_mainWindow == null) return;

        var workArea = SystemParameters.WorkArea;
        _mainWindow.Left = workArea.Right - _mainWindow.Width - 12;
        _mainWindow.Top = workArea.Bottom - _mainWindow.Height - 12;
    }

    private void QuitApp()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _viewModel?.Cleanup();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _mutex?.Dispose();
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
