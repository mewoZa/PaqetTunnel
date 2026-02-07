using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PaqetTunnel.Views.Controls;

/// <summary>
/// High-performance dual-line speed graph with Bézier smoothing, gradient fills, and peak marker.
/// Download = blue, Upload = green. Uses OnRender for maximum performance.
/// </summary>
public sealed class SpeedGraph : FrameworkElement
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(List<double>), typeof(SpeedGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DownloadDataProperty =
        DependencyProperty.Register(nameof(DownloadData), typeof(List<double>), typeof(SpeedGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UploadDataProperty =
        DependencyProperty.Register(nameof(UploadData), typeof(List<double>), typeof(SpeedGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public List<double>? Data
    {
        get => (List<double>?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public List<double>? DownloadData
    {
        get => (List<double>?)GetValue(DownloadDataProperty);
        set => SetValue(DownloadDataProperty, value);
    }

    public List<double>? UploadData
    {
        get => (List<double>?)GetValue(UploadDataProperty);
        set => SetValue(UploadDataProperty, value);
    }

    private static readonly Color DlColor = Color.FromRgb(88, 166, 255);     // Accent blue
    private static readonly Color UlColor = Color.FromRgb(63, 185, 80);      // Success green
    private static readonly Color GridColor = Color.FromArgb(18, 255, 255, 255);
    private static readonly Color BgColor = Color.FromArgb(20, 255, 255, 255);

    private static readonly Pen GridPen;
    private static readonly SolidColorBrush BgBrush;
    private static readonly Typeface LabelTypeface = new("Segoe UI Variable");

    static SpeedGraph()
    {
        GridPen = new Pen(new SolidColorBrush(GridColor), 0.5);
        GridPen.Freeze();
        BgBrush = new SolidColorBrush(BgColor);
        BgBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Background with rounded corners
        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 6, 6);

        // Subtle grid lines (3 horizontal)
        for (int i = 1; i <= 3; i++)
        {
            var y = h * i / 4.0;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }

        var dlData = DownloadData;
        var ulData = UploadData;

        // Fallback to combined Data if separate lists not available
        if ((dlData == null || dlData.Count < 2) && (ulData == null || ulData.Count < 2))
        {
            dlData = Data;
            ulData = null;
        }

        // Calculate global max across both series
        double max = 1;
        if (dlData?.Count > 0) max = Math.Max(max, dlData.Max());
        if (ulData?.Count > 0) max = Math.Max(max, ulData.Max());
        max *= 1.15; // headroom

        // Draw upload first (behind), then download (in front)
        if (ulData?.Count >= 2)
            DrawSeries(dc, ulData, w, h, max, UlColor, 1.2);

        if (dlData?.Count >= 2)
            DrawSeries(dc, dlData, w, h, max, DlColor, 1.8);

        // Peak marker dot on download
        if (dlData?.Count >= 2)
        {
            var peakIdx = 0;
            var peakVal = dlData[0];
            for (int i = 1; i < dlData.Count; i++)
            {
                if (dlData[i] > peakVal) { peakVal = dlData[i]; peakIdx = i; }
            }
            if (peakVal > 0)
            {
                var stepX = w / (dlData.Count - 1);
                var px = peakIdx * stepX;
                var py = h - (peakVal / max * (h - 6)) - 3;
                var dotBrush = new SolidColorBrush(DlColor);
                dotBrush.Freeze();
                dc.DrawEllipse(dotBrush, null, new Point(px, py), 2.5, 2.5);
            }
        }

        // Right-edge live indicator dot
        if (dlData?.Count >= 2)
        {
            var lastVal = dlData[^1];
            var stepX = w / (dlData.Count - 1);
            var lx = (dlData.Count - 1) * stepX;
            var ly = h - (lastVal / max * (h - 6)) - 3;
            var liveBrush = new SolidColorBrush(Color.FromArgb(200, DlColor.R, DlColor.G, DlColor.B));
            liveBrush.Freeze();
            dc.DrawEllipse(liveBrush, null, new Point(lx, ly), 3, 3);
            var glowBrush = new RadialGradientBrush(
                Color.FromArgb(60, DlColor.R, DlColor.G, DlColor.B), Colors.Transparent);
            glowBrush.Freeze();
            dc.DrawEllipse(glowBrush, null, new Point(lx, ly), 8, 8);
        }
    }

    private static void DrawSeries(DrawingContext dc, List<double> data, double w, double h,
        double max, Color color, double strokeWidth)
    {
        var count = data.Count;
        var stepX = w / (count - 1);
        var pad = 3.0;
        var usableH = h - pad * 2;

        double YFor(double val) => h - (val / max * usableH) - pad;

        // Build smooth Bézier path
        var lineGeo = new StreamGeometry();
        var fillGeo = new StreamGeometry();

        using (var ctx = lineGeo.Open())
        {
            ctx.BeginFigure(new Point(0, YFor(data[0])), false, false);
            AddBezierPoints(ctx, data, stepX, max, usableH, h, pad);
        }
        lineGeo.Freeze();

        using (var ctx = fillGeo.Open())
        {
            ctx.BeginFigure(new Point(0, h), true, true);
            ctx.LineTo(new Point(0, YFor(data[0])), false, false);
            AddBezierPoints(ctx, data, stepX, max, usableH, h, pad);
            ctx.LineTo(new Point((count - 1) * stepX, h), false, false);
        }
        fillGeo.Freeze();

        // Gradient fill (top = color, bottom = transparent)
        var fillBrush = new LinearGradientBrush(
            Color.FromArgb(50, color.R, color.G, color.B),
            Color.FromArgb(3, color.R, color.G, color.B),
            new Point(0, 0), new Point(0, 1));
        fillBrush.Freeze();
        dc.DrawGeometry(fillBrush, null, fillGeo);

        // Stroke
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)), strokeWidth)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, lineGeo);
    }

    private static void AddBezierPoints(StreamGeometryContext ctx, List<double> data,
        double stepX, double max, double usableH, double h, double pad)
    {
        double YFor(double val) => h - (val / max * usableH) - pad;
        var tension = 0.3;

        for (int i = 1; i < data.Count; i++)
        {
            var x0 = (i - 1) * stepX;
            var y0 = YFor(data[i - 1]);
            var x1 = i * stepX;
            var y1 = YFor(data[i]);
            var cp = (x1 - x0) * tension;
            ctx.BezierTo(
                new Point(x0 + cp, y0),
                new Point(x1 - cp, y1),
                new Point(x1, y1), true, false);
        }
    }
}
