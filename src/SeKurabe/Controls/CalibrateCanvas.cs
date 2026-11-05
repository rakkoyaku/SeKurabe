using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SeKurabe.Controls;

/// <summary>
/// 立ち絵 1 枚に「地面」と「頭頂」の横線を合わせる画面。線はドラッグか矢印キーで動かす。
/// 座標（GroundY / TopY）は元画像のピクセル。表示している画像は縮小版なので ImageScale で換算する。
/// </summary>
public sealed class CalibrateCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<CalibrateCanvas, Bitmap?>(nameof(Source));
    /// <summary>表示画像のピクセル ÷ 元画像のピクセル。</summary>
    public static readonly StyledProperty<double> ImageScaleProperty =
        AvaloniaProperty.Register<CalibrateCanvas, double>(nameof(ImageScale), 1);
    public static readonly StyledProperty<double> GroundYProperty =
        AvaloniaProperty.Register<CalibrateCanvas, double>(nameof(GroundY), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> TopYProperty =
        AvaloniaProperty.Register<CalibrateCanvas, double>(nameof(TopY), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string?> HeightTextProperty =
        AvaloniaProperty.Register<CalibrateCanvas, string?>(nameof(HeightText));
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<CalibrateCanvas, IBrush?>(nameof(Background));
    public static readonly DirectProperty<CalibrateCanvas, double> ZoomProperty =
        AvaloniaProperty.RegisterDirect<CalibrateCanvas, double>(nameof(Zoom), o => o.Zoom);

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public double ImageScale { get => GetValue(ImageScaleProperty); set => SetValue(ImageScaleProperty, value); }
    public double GroundY { get => GetValue(GroundYProperty); set => SetValue(GroundYProperty, value); }
    public double TopY { get => GetValue(TopYProperty); set => SetValue(TopYProperty, value); }
    public string? HeightText { get => GetValue(HeightTextProperty); set => SetValue(HeightTextProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    double _zoom = 1;
    public double Zoom { get => _zoom; private set => SetAndRaise(ZoomProperty, ref _zoom, value); }
    Vector _offset;
    bool _needsFit = true;

    enum Drag { None, Ground, Top, Pan }
    Drag _drag;
    Drag _active = Drag.Top;      // 矢印キーで動かす線
    Drag _hover;
    Point _last, _pointer;
    Vector _panStart;
    Point _panPointer;

    static readonly Color GroundColor = Color.FromRgb(0x2E, 0xB8, 0x6E);
    static readonly Color TopColor = Color.FromRgb(0xF0, 0x8A, 0x24);
    static readonly IBrush Checker = CreateChecker();

    static CalibrateCanvas()
    {
        AffectsRender<CalibrateCanvas>(SourceProperty, ImageScaleProperty, GroundYProperty, TopYProperty, HeightTextProperty, BackgroundProperty);
        FocusableProperty.OverrideDefaultValue<CalibrateCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<CalibrateCanvas>(true);
    }

    static IBrush CreateChecker()
    {
        const int cell = 8;
        var wb = new WriteableBitmap(new PixelSize(cell * 2, cell * 2), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            unsafe
            {
                for (int y = 0; y < cell * 2; y++)
                {
                    var row = (uint*)((byte*)fb.Address + y * fb.RowBytes);
                    for (int x = 0; x < cell * 2; x++) row[x] = ((x / cell + y / cell) % 2 == 0) ? 0xFFD0D0D0 : 0xFFF2F2F2;
                }
            }
        }
        return new ImageBrush(wb) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, cell * 2, cell * 2, RelativeUnit.Absolute) };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && !ReferenceEquals(change.OldValue, change.NewValue))
        {
            _needsFit = true;
            PostFit();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_needsFit) PostFit();
    }

    void PostFit() => Dispatcher.UIThread.Post(() => { if (_needsFit && Source is not null && Bounds.Width > 0) FitToView(); }, DispatcherPriority.Render);

    public void FitToView()
    {
        if (Source is null || Bounds.Width <= 0) return;
        var s = Source.PixelSize;
        Zoom = Math.Min(Bounds.Width / s.Width, Bounds.Height / s.Height) * 0.9;
        _offset = new Vector(Bounds.Width / 2 - s.Width / 2.0 * Zoom, Bounds.Height / 2 - s.Height / 2.0 * Zoom);
        _needsFit = false;
        InvalidateVisual();
    }

    public void ZoomBy(double f) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), f);

    void ZoomAt(Point p, double f)
    {
        var img = new Point((p.X - _offset.X) / Zoom, (p.Y - _offset.Y) / Zoom);
        Zoom = Math.Clamp(Zoom * f, 0.02, 40);
        _offset = new Vector(p.X - img.X * Zoom, p.Y - img.Y * Zoom);
        InvalidateVisual();
    }

    // 元画像の Y（px）⇔ 画面の Y
    double ScreenY(double imageY) => _offset.Y + imageY * ImageScale * Zoom;
    double ImageY(double screenY) => (screenY - _offset.Y) / (ImageScale * Zoom);

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(Background ?? Brushes.Black, b);
        if (Source is null || _needsFit) return;
        var px = Source.PixelSize;
        var img = new Rect(_offset.X, _offset.Y, px.Width * Zoom, px.Height * Zoom);
        ctx.FillRectangle(Checker, img.Intersect(b));
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Zoom * 1 >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
            ctx.DrawImage(Source, new Rect(0, 0, px.Width, px.Height), img);

        double gy = ScreenY(GroundY), ty = ScreenY(TopY);
        // 身長として測る範囲の外側を少し暗くする
        var dim = new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x10, 0x18));
        ctx.FillRectangle(dim, new Rect(img.Left, img.Top, img.Width, Math.Max(0, ty - img.Top)));
        ctx.FillRectangle(dim, new Rect(img.Left, gy, img.Width, Math.Max(0, img.Bottom - gy)));

        double x0 = Math.Max(0, img.Left - 40), x1 = Math.Min(b.Width, img.Right + 40);
        DrawLine(ctx, ty, x0, x1, TopColor, "頭頂", _active == Drag.Top, _hover == Drag.Top || _drag == Drag.Top);
        DrawLine(ctx, gy, x0, x1, GroundColor, "地面", _active == Drag.Ground, _hover == Drag.Ground || _drag == Drag.Ground);

        // 左に身長の寸法線
        double bx = Math.Max(14, img.Left - 26);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF4)), 2);
        ctx.DrawLine(pen, new Point(bx, ty), new Point(bx, gy));
        ctx.DrawLine(pen, new Point(bx - 6, ty), new Point(bx + 6, ty));
        ctx.DrawLine(pen, new Point(bx - 6, gy), new Point(bx + 6, gy));
        if (!string.IsNullOrEmpty(HeightText))
        {
            var ft = Text(HeightText!, 13, Brushes.White, FontWeight.SemiBold);
            var pill = new Rect(bx - ft.Width / 2 - 9, (ty + gy) / 2 - ft.Height / 2 - 4, ft.Width + 18, ft.Height + 8);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x2A)), null, pill, pill.Height / 2, pill.Height / 2);
            ctx.DrawText(ft, new Point(pill.X + 9, pill.Y + 4));
        }

        if (_drag is Drag.Ground or Drag.Top) DrawLoupe(ctx, b, img);
    }

    void DrawLine(DrawingContext ctx, double y, double x0, double x1, Color col, string label, bool active, bool hot)
    {
        var under = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), hot ? 5 : 4);
        var pen = new Pen(new SolidColorBrush(col), hot ? 3 : 2);
        ctx.DrawLine(under, new Point(x0, y), new Point(x1, y));
        ctx.DrawLine(pen, new Point(x0, y), new Point(x1, y));
        var ft = Text(label, 12.5, Brushes.White, FontWeight.Bold);
        var tag = new Rect(x1 - ft.Width - 26, y - ft.Height / 2 - 5, ft.Width + 20, ft.Height + 10);
        ctx.DrawRectangle(new SolidColorBrush(col), active ? new Pen(Brushes.White, 2) : null, tag, tag.Height / 2, tag.Height / 2);
        ctx.DrawText(ft, new Point(tag.X + 10, tag.Y + 5));
    }

    /// <summary>ドラッグ中、指先のあたりを拡大して見せる（線を 1px 単位で合わせやすく）。</summary>
    void DrawLoupe(DrawingContext ctx, Rect bounds, Rect img)
    {
        if (Source is null) return;
        const double R = 70, Mag = 4;
        var center = new Point(Math.Clamp(_pointer.X + 110, R + 4, bounds.Width - R - 4), Math.Clamp(_pointer.Y - 110, R + 4, bounds.Height - R - 4));
        double lineY = _drag == Drag.Ground ? ScreenY(GroundY) : ScreenY(TopY);
        var focus = new Point(_pointer.X, lineY);
        // 表示画像上の、拡大する範囲
        double srcHalf = R / (Zoom * Mag);
        var srcCenter = new Point((focus.X - _offset.X) / Zoom, (focus.Y - _offset.Y) / Zoom);
        var srcRect = new Rect(srcCenter.X - srcHalf, srcCenter.Y - srcHalf, srcHalf * 2, srcHalf * 2);
        var dst = new Rect(center.X - R, center.Y - R, R * 2, R * 2);
        var circle = new EllipseGeometry(dst);
        using (ctx.PushGeometryClip(circle))
        {
            ctx.FillRectangle(Checker, dst);
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                ctx.DrawImage(Source, srcRect, dst);
            var col = _drag == Drag.Ground ? GroundColor : TopColor;
            ctx.DrawLine(new Pen(new SolidColorBrush(col), 2), new Point(dst.Left, center.Y), new Point(dst.Right, center.Y));
        }
        ctx.DrawGeometry(null, new Pen(Brushes.White, 3), circle);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), 1), new EllipseGeometry(dst.Inflate(2)));
    }

    static FormattedText Text(string s, double size, IBrush brush, FontWeight weight) =>
        new(s, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    Drag HitLine(Point p)
    {
        if (Source is null) return Drag.None;
        double gy = ScreenY(GroundY), ty = ScreenY(TopY);
        double dg = Math.Abs(p.Y - gy), dt = Math.Abs(p.Y - ty);
        const double tol = 9;
        if (dg <= tol && dg <= dt) return Drag.Ground;
        if (dt <= tol) return Drag.Top;
        return Drag.None;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null) return;
        Focus();
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (e.ClickCount == 2 && props.IsLeftButtonPressed && HitLine(p) == Drag.None) { FitToView(); return; }
        var hit = props.IsLeftButtonPressed ? HitLine(p) : Drag.None;
        if (hit != Drag.None) { _drag = hit; _active = hit; }
        else { _drag = Drag.Pan; _panStart = _offset; _panPointer = p; }
        _last = _pointer = p;
        e.Pointer.Capture(this);
        UpdateCursor(p);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _pointer = p;
        switch (_drag)
        {
            case Drag.Pan:
                _offset = _panStart + (p - _panPointer);
                InvalidateVisual();
                break;
            case Drag.Ground:
                GroundY = Math.Round(Math.Max(ImageY(p.Y), TopY + 1));
                break;
            case Drag.Top:
                TopY = Math.Round(Math.Min(ImageY(p.Y), GroundY - 1));
                break;
            default:
                var h = HitLine(p);
                if (h != _hover) { _hover = h; InvalidateVisual(); }
                UpdateCursor(p);
                break;
        }
        _last = p;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), Math.Pow(1.2, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Source is null) return;
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Up or Key.Down:
                int d = e.Key == Key.Up ? -step : step;
                if (_active == Drag.Ground) GroundY = Math.Max(TopY + 1, GroundY + d);
                else TopY = Math.Min(GroundY - 1, TopY + d);
                break;
            case Key.Tab:
                _active = _active == Drag.Top ? Drag.Ground : Drag.Top;
                InvalidateVisual();
                break;
            default: return;
        }
        e.Handled = true;
    }

    void UpdateCursor(Point p)
    {
        Cursor = _drag switch
        {
            Drag.Pan => new Cursor(StandardCursorType.SizeAll),
            Drag.Ground or Drag.Top => new Cursor(StandardCursorType.SizeNorthSouth),
            _ => HitLine(p) != Drag.None ? new Cursor(StandardCursorType.SizeNorthSouth) : Cursor.Default,
        };
    }
}
