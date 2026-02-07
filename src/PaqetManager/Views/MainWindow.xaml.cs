using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PaqetManager.Views;

public partial class MainWindow : Window
{
    private DateTime _suppressHideUntil = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
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
            _suppressHideUntil = DateTime.UtcNow.AddSeconds(2);
            // Use App.ShowWindow() which handles positioning near tray
            if (Application.Current is App app)
                app.ShowWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Suppress auto-hide for a short period (e.g. after tray click).</summary>
    public void SuppressAutoHide(int seconds = 2)
    {
        _suppressHideUntil = DateTime.UtcNow.AddSeconds(seconds);
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
        Hide(); // Minimize to tray, not taskbar
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide(); // Close to tray
    }

    // ── Auto-hide when window loses focus ─────────────────────────
    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (DateTime.UtcNow < _suppressHideUntil) return;

        var timer = new System.Timers.Timer(400) { AutoReset = false };
        timer.Elapsed += (s, ev) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsActive && IsVisible && DateTime.UtcNow >= _suppressHideUntil)
                    Hide();
            });
            timer.Dispose();
        };
        timer.Start();
    }
}
