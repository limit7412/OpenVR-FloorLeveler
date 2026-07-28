using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FloorLeveler.Core;

namespace FloorLeveler.App.Controls;

/// <summary>
/// 点群と推定床面を 2D に描画する軽量コントロール (仕様 F-4)。投影は Core の
/// <see cref="FloorProjection"/> が純粋関数として行い、本コントロールはメートル座標を
/// ピクセルへ写して点・床線・枠・ラベルを描くだけ (Imperative Shell)。
/// </summary>
public sealed class FloorPlotControl : Control
{
    /// <summary>描画する投影データ (俯瞰または側面)。</summary>
    public static readonly StyledProperty<FloorPlot?> PlotProperty =
        AvaloniaProperty.Register<FloorPlotControl, FloorPlot?>(nameof(Plot));

    /// <summary>横縦を等倍で描くか (俯瞰=true で広がりの形を保つ / 側面=false で傾きを見せる)。</summary>
    public static readonly StyledProperty<bool> EqualAspectProperty =
        AvaloniaProperty.Register<FloorPlotControl, bool>(nameof(EqualAspect));

    /// <summary>描くデータが無いときに表示する文言 (言語に依存するため外から渡す)。</summary>
    public static readonly StyledProperty<string?> EmptyTextProperty =
        AvaloniaProperty.Register<FloorPlotControl, string?>(nameof(EmptyText));

    static FloorPlotControl()
    {
        AffectsRender<FloorPlotControl>(PlotProperty, EqualAspectProperty, EmptyTextProperty);
    }

    public FloorPlot? Plot
    {
        get => GetValue(PlotProperty);
        set => SetValue(PlotProperty, value);
    }

    public bool EqualAspect
    {
        get => GetValue(EqualAspectProperty);
        set => SetValue(EqualAspectProperty, value);
    }

    public string? EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        var background = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
        context.FillRectangle(background, area);

        var axis = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)), 1);
        var muted = new SolidColorBrush(Color.FromArgb(0xC0, 0x90, 0x90, 0x90));

        // 軸ラベルのための余白をとった内側の描画領域。
        const double left = 40, bottom = 20, top = 8, right = 10;
        if (area.Width <= left + right + 8 || area.Height <= top + bottom + 8)
        {
            return;
        }

        var plotRect = new Rect(left, top, area.Width - left - right, area.Height - top - bottom);
        context.DrawRectangle(null, axis, plotRect);

        var plot = Plot;
        if (plot is null || plot.IsEmpty)
        {
            DrawText(context, EmptyText ?? string.Empty, new Point(plotRect.X + 8, plotRect.Y + 8), muted);
            return;
        }

        // メートル → ピクセルの写像。データ範囲に 8% の余白を足し、退化 (幅 0) は最小幅に広げる。
        double minX = plot.Min.X, maxX = plot.Max.X, minY = plot.Min.Y, maxY = plot.Max.Y;
        Expand(ref minX, ref maxX, 0.5);
        Expand(ref minY, ref maxY, EqualAspect ? 0.5 : 0.05);
        var rangeX = maxX - minX;
        var rangeY = maxY - minY;
        minX -= rangeX * 0.08; maxX += rangeX * 0.08; rangeX = maxX - minX;
        minY -= rangeY * 0.08; maxY += rangeY * 0.08; rangeY = maxY - minY;

        var scaleX = plotRect.Width / rangeX;
        var scaleY = plotRect.Height / rangeY;
        double offsetX = 0, offsetY = 0;
        if (EqualAspect)
        {
            var scale = Math.Min(scaleX, scaleY);
            scaleX = scaleY = scale;
            offsetX = (plotRect.Width - (rangeX * scale)) * 0.5;
            offsetY = (plotRect.Height - (rangeY * scale)) * 0.5;
        }

        // 世界座標 (x 右, y 上) → 画面座標 (y 下)。
        Point ToPixel(double wx, double wy) => new(
            plotRect.X + offsetX + ((wx - minX) * scaleX),
            plotRect.Bottom - offsetY - ((wy - minY) * scaleY));

        // 推定床面の断面線。
        if (plot.FloorLine is { } fl)
        {
            var floorPen = new Pen(new SolidColorBrush(Color.FromArgb(0xF0, 0xE0, 0x7A, 0x1E)), 2);
            context.DrawLine(floorPen, ToPixel(fl.Start.X, fl.Start.Y), ToPixel(fl.End.X, fl.End.Y));
        }

        // 点群。
        var pointFill = new SolidColorBrush(Color.FromArgb(0xE0, 0x3A, 0x9B, 0xD5));
        foreach (var p in plot.Points)
        {
            context.DrawEllipse(pointFill, null, ToPixel(p.X, p.Y), 3, 3);
        }

        // 軸ラベル。
        DrawText(context, plot.HorizontalLabel,
            new Point(plotRect.Right - 72, plotRect.Bottom + 3), muted);
        DrawText(context, plot.VerticalLabel, new Point(4, plotRect.Y - 1), muted);
    }

    private static void Expand(ref double min, ref double max, double minHalfWidth)
    {
        if (max - min >= minHalfWidth * 2)
        {
            return;
        }

        var center = (min + max) * 0.5;
        min = center - minHalfWidth;
        max = center + minHalfWidth;
    }

    private static void DrawText(DrawingContext context, string text, Point origin, IBrush brush)
    {
        var formatted = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, brush);
        context.DrawText(formatted, origin);
    }
}
