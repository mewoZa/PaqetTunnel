using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PaqetManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Register for the custom WM_PAQET_SHOW message (single instance communication)
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)NativeMethods.WM_PAQET_SHOW)
        {
            Show();
            Activate();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            handled = true;
        }
        return IntPtr.Zero;
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
        // Small delay to allow button clicks / UAC dialogs
        var timer = new System.Timers.Timer(300) { AutoReset = false };
        timer.Elapsed += (s, ev) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsActive && IsVisible)
                    Hide();
            });
            timer.Dispose();
        };
        timer.Start();
    }
}
