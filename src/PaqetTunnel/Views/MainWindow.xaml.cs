using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PaqetTunnel.Views;

public partial class MainWindow : Window
{
    private bool _autoHideEnabled;

    // R3-11 fix: create timer ONCE, reuse across deactivations
    private readonly System.Timers.Timer _deactivateTimer;

    public MainWindow()
    {
        InitializeComponent();
        _deactivateTimer = new System.Timers.Timer(400) { AutoReset = false };
        _deactivateTimer.Elapsed += (s, ev) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsActive && IsVisible)
                    Hide();
            });
        };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)NativeMethods.WM_PAQET_SHOW)
        {
            if (Application.Current is App app)
                app.ShowWindow();
            handled = true;
        }
        else if (msg == NativeMethods.WM_DISPLAYCHANGE || msg == NativeMethods.WM_DPICHANGED)
        {
            // Display resolution or DPI changed — reposition on next show
            if (Application.Current is App app2 && IsVisible)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    app2.ShowWindow();
                }));
            }
            // Don't set handled — let WPF process it too
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// When true, window auto-hides on deactivation (tray popup mode).
    /// When false, window stays visible like a normal window.
    /// </summary>
    public bool AutoHideEnabled
    {
        get => _autoHideEnabled;
        set => _autoHideEnabled = value;
    }

    // ── Title bar drag ────────────────────────────────────────────
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    // ── Window chrome buttons ─────────────────────────────────────
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        // R5-31: minimize to taskbar instead of hiding to tray
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // ── Auto-hide when window loses focus (only in tray popup mode) ──
    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_autoHideEnabled) return;

        // R3-11 fix: just restart the existing timer (no allocation)
        _deactivateTimer.Stop();
        _deactivateTimer.Start();
    }

    // ── DNS ComboBox selection handler ────────────────────────────
    private void DnsProviderChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox combo &&
            combo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string provider &&
            DataContext is ViewModels.MainViewModel vm)
        {
            vm.SetDnsProviderCommand.Execute(provider);
        }
    }

    // ── Theme ComboBox selection handler ──────────────────────────
    private void ThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox combo &&
            combo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string theme &&
            DataContext is ViewModels.MainViewModel vm)
        {
            vm.SetThemeCommand.Execute(theme);
        }
    }
}
