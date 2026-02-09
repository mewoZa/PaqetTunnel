using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PaqetTunnel.Converters;

/// <summary>bool → Visibility</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        value is Visibility.Visible;
}

/// <summary>Inverted bool → Visibility (true = Collapsed)</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        value is Visibility.Collapsed;
}

/// <summary>bool → connected/disconnected color</summary>
public sealed class ConnectionColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(63, 185, 80))   // green
            : new SolidColorBrush(Color.FromRgb(139, 148, 158)); // gray

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>bool → accent glow color</summary>
public sealed class ConnectionGlowConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        value is true
            ? Color.FromArgb(80, 63, 185, 80)
            : Color.FromArgb(0, 0, 0, 0);

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>Invert boolean</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        value is bool b ? !b : value;
}

/// <summary>bool → toggle switch text</summary>
public sealed class ToggleTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        value is true ? "ON" : "OFF";

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>Non-empty string → Visibility</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>Hex color string → SolidColorBrush</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { }
        }
        return new SolidColorBrush(Color.FromRgb(139, 148, 158));
    }

    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) =>
        throw new NotImplementedException();
}
