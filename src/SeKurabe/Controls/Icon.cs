using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SeKurabe.Controls;

/// <summary>
/// 線画アイコン（24×24 の座標系、Lucide 風）。色は親から継承した文字色を使う。
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Kind));
    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), 1.9);

    /// <summary>線の色。指定しなければ親の文字色を継承する。</summary>
    public static readonly AttachedProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    static readonly Dictionary<string, string> Paths = new()
    {
        ["plus"] = "M5 12h14 M12 5v14",
        ["minus"] = "M5 12h14",
        ["x"] = "M18 6L6 18 M6 6l12 12",
        ["check"] = "M20 6L9 17l-5-5",
        ["folder"] = "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        ["folder-plus"] = "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z M12 10v6 M9 13h6",
        ["image"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M21 15l-3.09-3.09a2 2 0 0 0-2.82 0L6 21 M11 9a2 2 0 1 1-4 0a2 2 0 1 1 4 0",
        ["images"] = "M8 2h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z M22 13l-2.6-2.6a2 2 0 0 0-2.8 0L10 17 M2 7v13a2 2 0 0 0 2 2h13 M13 7a1.5 1.5 0 1 1-3 0a1.5 1.5 0 1 1 3 0",
        ["trash"] = "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
        ["zoom-in"] = "M19 11a8 8 0 1 1-16 0a8 8 0 1 1 16 0 M21 21l-4.35-4.35 M11 8v6 M8 11h6",
        ["zoom-out"] = "M19 11a8 8 0 1 1-16 0a8 8 0 1 1 16 0 M21 21l-4.35-4.35 M8 11h6",
        ["fit"] = "M8 3H5a2 2 0 0 0-2 2v3 M21 8V5a2 2 0 0 0-2-2h-3 M3 16v3a2 2 0 0 0 2 2h3 M16 21h3a2 2 0 0 0 2-2v-3",
        ["focus"] = "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M8 14s1.5 2 4 2 4-2 4-2 M9 9h.01 M15 9h.01",
        ["grid"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M3 9h18 M3 15h18 M9 3v18 M15 3v18",
        ["crosshair"] = "M22 12a10 10 0 1 1-20 0a10 10 0 1 1 20 0 M22 12h-4 M6 12H2 M12 6V2 M12 22v-4",
        ["circle"] = "M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0",
        ["help"] = "M22 12a10 10 0 1 1-20 0a10 10 0 1 1 20 0 M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3 M12 17h.01",
        ["star"] = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z",
        ["alert"] = "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z M12 9v4 M12 17h.01",
        ["alert-circle"] = "M22 12a10 10 0 1 1-20 0a10 10 0 1 1 20 0 M12 8v4 M12 16h.01",
        ["info"] = "M22 12a10 10 0 1 1-20 0a10 10 0 1 1 20 0 M12 16v-4 M12 8h.01",
        ["download"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3",
        ["upload"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M17 8l-5-5-5 5 M12 3v12",
        ["sort"] = "M3 6h18 M6 12h12 M10 18h4",
        ["external"] = "M15 3h6v6 M10 14L21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
        ["undo"] = "M3 7v6h6 M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13",
        // 胸から上の人物（頭＋肩）を枠で囲んだもの
        ["bust"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M15 10a3 3 0 1 1-6 0a3 3 0 1 1 6 0 M6.5 21a5.5 5 0 0 1 11 0",
        ["pencil"] = "M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z M15 5l4 4",
        ["refresh"] = "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5",
        ["chevron-up"] = "M18 15l-6-6-6 6",
        ["chevron-down"] = "M6 9l6 6 6-6",
        ["flip"] = "M12 3v18 M16 7l5 5-5 5V7z M8 7l-5 5 5 5V7z",
        ["save"] = "M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z M17 21v-8H7v8 M7 3v5h8",
        ["menu"] = "M4 6h16 M4 12h16 M4 18h16",
        ["ruler"] = "M21.3 15.3a2.4 2.4 0 0 1 0 3.4l-2.6 2.6a2.4 2.4 0 0 1-3.4 0L2.7 8.7a2.41 2.41 0 0 1 0-3.4l2.6-2.6a2.41 2.41 0 0 1 3.4 0Z M14.5 12.5l2-2 M11.5 9.5l2-2 M8.5 6.5l2-2 M17.5 15.5l2-2",
        ["move"] = "M5 9l-3 3 3 3 M9 5l3-3 3 3 M15 19l-3 3-3-3 M19 9l3 3-3 3 M2 12h20 M12 2v20",
        ["users"] = "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M13 7a4 4 0 1 1-8 0a4 4 0 1 1 8 0 M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75",
        ["mouse"] = "M12 2a6 6 0 0 1 6 6v8a6 6 0 0 1-12 0V8a6 6 0 0 1 6-6z M12 6v4",
    };

    static readonly Dictionary<string, Geometry> Cache = new();

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, StrokeWidthProperty, TextElement.ForegroundProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<Icon>(false);
        WidthProperty.OverrideDefaultValue<Icon>(16);
        HeightProperty.OverrideDefaultValue<Icon>(16);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Kind is null || !Paths.TryGetValue(Kind, out var data)) return;
        if (!Cache.TryGetValue(Kind, out var geo)) Cache[Kind] = geo = StreamGeometry.Parse(data);

        var brush = Foreground ?? Brushes.Gray;
        var s = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var ox = (Bounds.Width - 24 * s) / 2;
        var oy = (Bounds.Height - 24 * s) / 2;
        using (ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        {
            var pen = new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            ctx.DrawGeometry(null, pen, geo);
        }
    }
}
