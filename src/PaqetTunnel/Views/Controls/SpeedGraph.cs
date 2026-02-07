using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PaqetTunnel.Views.Controls;

/// <summary>
/// Lightweight custom control for rendering a speed graph.
/// Uses OnRender for maximum performance — no Canvas, no UIElement children.
/// </summary>
public sealed class SpeedGraph : FrameworkElement
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(List<double>), typeof(SpeedGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeColorProperty =
        DependencyProperty.Register(nameof(StrokeColor), typeof(Color), typeof(SpeedGraph),
            new FrameworkPropertyMetadata(Color.FromRgb(88, 166, 255), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillColorProperty =
        DependencyProperty.Register(nameof(FillColor), typeof(Color), typeof(SpeedGraph),
            new FrameworkPropertyMetadata(Color.FromArgb(40, 88, 166, 255), FrameworkPropertyMetadataOptions.AffectsRender));

    public List<double>? Data
    {
        get => (List<double>?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), null, new Rect(0, 0, w, h));

        // Grid lines
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), 0.5);
        gridPen.Freeze();
        for (int i = 1; i < 4; i++)
        {
            var y = h * i / 4.0;
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        var data = Data;
        if (data == null || data.Count < 2) return;

        var max = data.Max();
        if (max <= 0) max = 1;

        var stepX = w / (data.Count - 1);

        // Build path
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var firstY = h - (data[0] / max * (h - 4)) - 2;
            ctx.BeginFigure(new Point(0, firstY), false, false);

            for (int i = 1; i < data.Count; i++)
            {
                var x = i * stepX;
                var y = h - (data[i] / max * (h - 4)) - 2;
                ctx.LineTo(new Point(x, y), true, true);
            }
        }
        geometry.Freeze();

        // Fill area
        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open())
        {
            var firstY = h - (data[0] / max * (h - 4)) - 2;
            ctx.BeginFigure(new Point(0, h), true, true);
            ctx.LineTo(new Point(0, firstY), true, false);

            for (int i = 1; i < data.Count; i++)
            {
                var x = i * stepX;
                var y = h - (data[i] / max * (h - 4)) - 2;
                ctx.LineTo(new Point(x, y), true, true);
            }
            ctx.LineTo(new Point((data.Count - 1) * stepX, h), true, false);
        }
        fillGeometry.Freeze();

        var fillBrush = new LinearGradientBrush(
            FillColor,
            Color.FromArgb(5, FillColor.R, FillColor.G, FillColor.B),
            new Point(0, 0), new Point(0, 1));
        fillBrush.Freeze();
        dc.DrawGeometry(fillBrush, null, fillGeometry);

        // Stroke
        var strokePen = new Pen(new SolidColorBrush(StrokeColor), 1.5)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        strokePen.Freeze();
        dc.DrawGeometry(null, strokePen, geometry);
    }
}
