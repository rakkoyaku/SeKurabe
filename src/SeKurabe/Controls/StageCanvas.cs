using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SeKurabe.Models;
using SeKurabe.Services;
using SeKurabe.ViewModels;

namespace SeKurabe.Controls;

/// <summary>
/// 並べた結果（プレビュー画像）を表示し、キャラをクリックで選び、ドラッグで動かすキャンバス。
/// 位置の計算は cm で行い、ViewModel に渡す（吸い付きもそちらで計算する）。
/// </summary>
public sealed class StageCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<StageCanvas, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<ChartLayout?> ChartLayoutProperty =
        AvaloniaProperty.Register<StageCanvas, ChartLayout?>(nameof(ChartLayout));
    public static readonly StyledProperty<IReadOnlyList<SnapGuide>?> GuidesProperty =
        AvaloniaProperty.Register<StageCanvas, IReadOnlyList<SnapGuide>?>(nameof(Guides));
    public static readonly StyledProperty<Character?> SelectedProperty =
        AvaloniaProperty.Register<StageCanvas, Character?>(nameof(Selected));
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<StageCanvas, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<StageCanvas, IBrush?>(nameof(AccentBrush), Brushes.Teal);
    public static readonly DirectProperty<StageCanvas, double> ZoomProperty =
        AvaloniaProperty.RegisterDirect<StageCanvas, double>(nameof(Zoom), o => o.Zoom);

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public ChartLayout? ChartLayout { get => GetValue(ChartLayoutProperty); set => SetValue(ChartLayoutProperty, value); }
    public IReadOnlyList<SnapGuide>? Guides { get => GetValue(GuidesProperty); set => SetValue(GuidesProperty, value); }
    public Character? Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    /// <summary>キャラの一覧や配置の変更を伝える先。</summary>
    public MainViewModel? ViewModel { get; set; }

    double _zoom = 1;
    public double Zoom { get => _zoom; private set => SetAndRaise(ZoomProperty, ref _zoom, value); }
    Vector _offset;
    bool _needsFit = true;
    PixelSize _lastSize;

    enum DragMode { None, Move, Pan }
    DragMode _drag;
    Point _startPointer;
    Vector _startOffset;
    double _startX, _startLift;
    Character? _hover;
    bool _spaceDown;

    static readonly Color GuideColor = Color.FromRgb(0xE8, 0x3E, 0x8C);

    static StageCanvas()
    {
        AffectsRender<StageCanvas>(SourceProperty, ChartLayoutProperty, GuidesProperty, SelectedProperty, BackgroundProperty);
        FocusableProperty.OverrideDefaultValue<StageCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<StageCanvas>(true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && change.NewValue is Bitmap b)
        {
            // 大きさが大きく変わったときだけ全体表示し直す（ドラッグ中は変えない）
            var s = b.PixelSize;
            if (_drag == DragMode.None && (_lastSize.Width == 0 || Math.Abs(s.Width - _lastSize.Width) > 2 || Math.Abs(s.Height - _lastSize.Height) > s.Height * 0.15))
            {
                _needsFit = true;
                PostFit();
            }
            _lastSize = s;
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
        Zoom = Math.Min((Bounds.Width - 48) / s.Width, (Bounds.Height - 110) / s.Height);
        _offset = new Vector(Bounds.Width / 2 - s.Width / 2.0 * Zoom, (Bounds.Height - 60) / 2 - s.Height / 2.0 * Zoom);
        _needsFit = false;
        InvalidateVisual();
    }

    public void ZoomBy(double f) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), f);

    void ZoomAt(Point p, double f)
    {
        var img = ToImage(p);
        Zoom = Math.Clamp(Zoom * f, 0.05, 20);
        _offset = new Vector(p.X - img.X * Zoom, p.Y - img.Y * Zoom);
        InvalidateVisual();
    }

    Point ToImage(Point screen) => new((screen.X - _offset.X) / Zoom, (screen.Y - _offset.Y) / Zoom);
    Point ToScreen(double x, double y) => new(_offset.X + x * Zoom, _offset.Y + y * Zoom);

    Rect? ScreenRect(Character c)
    {
        if (ChartLayout is not { } l || !l.CharRects.TryGetValue(c.Id, out var r)) return null;
        var a = ToScreen(r.Left, r.Top);
        return new Rect(a.X, a.Y, r.Width * Zoom, r.Height * Zoom);
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(Background ?? Brushes.Black, b);
        if (Source is null || _needsFit) return;
        var px = Source.PixelSize;
        var img = new Rect(_offset.X, _offset.Y, px.Width * Zoom, px.Height * Zoom);
        // 画像の影
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, img.Translate(new Vector(0, 3)).Inflate(1), 6, 6);
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            ctx.DrawImage(Source, new Rect(0, 0, px.Width, px.Height), img);

        var accent = AccentBrush ?? Brushes.Teal;
        if (_hover is not null && _hover != Selected && ScreenRect(_hover) is { } hr)
            ctx.DrawRectangle(null, new Pen(accent, 1.5, new DashStyle([4, 4], 0)), hr.Inflate(3), 6, 6);
        if (Selected is not null && ScreenRect(Selected) is { } sr)
        {
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 4), sr.Inflate(4), 7, 7);
            ctx.DrawRectangle(null, new Pen(accent, 2), sr.Inflate(4), 7, 7);
        }

        // 吸い付きのガイド線
        if (Guides is { Count: > 0 } guides && ChartLayout is { } l)
        {
            var pen = new Pen(new SolidColorBrush(GuideColor), 1.5, new DashStyle([6, 4], 0));
            foreach (var g in guides)
            {
                Point a, z;
                if (g.Horizontal)
                {
                    double y = l.ToPxY(g.Pos);
                    a = ToScreen(l.ToPxX(g.From) - 30, y); z = ToScreen(l.ToPxX(g.To) + 30, y);
                }
                else
                {
                    double x = l.ToPxX(g.Pos);
                    a = ToScreen(x, l.ToPxY(g.To) - 30); z = ToScreen(x, l.ToPxY(g.From) + 30);
                }
                ctx.DrawLine(pen, a, z);
                var ft = Text(g.Label, 11.5, Brushes.White, FontWeight.SemiBold);
                var pill = new Rect(z.X + (g.Horizontal ? 6 : -ft.Width / 2 - 8), z.Y + (g.Horizontal ? -ft.Height / 2 - 3 : 6), ft.Width + 16, ft.Height + 6);
                ctx.DrawRectangle(new SolidColorBrush(GuideColor), null, pill, pill.Height / 2, pill.Height / 2);
                ctx.DrawText(ft, new Point(pill.X + 8, pill.Y + 3));
            }
        }

        // ドラッグ中は位置を表示
        if (_drag == DragMode.Move && Selected is { } c && ScreenRect(c) is { } r)
        {
            var text = c.Lift > 0.05 ? $"横 {c.X:0.#} cm   浮き {c.Lift:0.#} cm" : $"横 {c.X:0.#} cm   地面";
            var ft = Text(text, 12, Brushes.White, FontWeight.SemiBold);
            var pill = new Rect(r.Center.X - ft.Width / 2 - 10, r.Bottom + 12, ft.Width + 20, ft.Height + 8);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE0, 0x22, 0x22, 0x2A)), null, pill, pill.Height / 2, pill.Height / 2);
            ctx.DrawText(ft, new Point(pill.X + 10, pill.Y + 4));
        }
    }

    static FormattedText Text(string s, double size, IBrush brush, FontWeight weight) =>
        new(s, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    /// <summary>その位置にいるキャラ。重なっていたら小さい方（手前に置いた妖精など）を優先する。</summary>
    Character? HitTest(Point p)
    {
        if (ViewModel is null) return null;
        Character? best = null;
        double bestArea = double.MaxValue;
        foreach (var c in ViewModel.Characters)
        {
            if (ScreenRect(c) is not { } r || !r.Contains(p)) continue;
            var area = r.Width * r.Height;
            if (area < bestArea) { best = c; bestArea = area; }
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null) return;
        Focus();
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        var hit = props.IsLeftButtonPressed && !_spaceDown ? HitTest(p) : null;

        if (hit is not null && ViewModel is not null)
        {
            ViewModel.Selected = hit;
            if (e.ClickCount == 2)
            {
                // ダブルクリックで、そのキャラの調整へ
                ViewModel.Mode = "calibrate";
                e.Handled = true;
                return;
            }
            _drag = DragMode.Move;
            _startX = hit.X;
            _startLift = hit.Lift;
            ViewModel.BeginDrag();
        }
        else
        {
            if (e.ClickCount == 2) { FitToView(); return; }
            _drag = DragMode.Pan;
            _startOffset = _offset;
            if (props.IsLeftButtonPressed && hit is null && ViewModel is not null && !_spaceDown) ViewModel.Selected = null;
        }
        _startPointer = p;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        switch (_drag)
        {
            case DragMode.Pan:
                _offset = _startOffset + (p - _startPointer);
                InvalidateVisual();
                break;
            case DragMode.Move when Selected is { } c && ChartLayout is { } l && ViewModel is { } vm:
            {
                // 画面のずれ → 画像のずれ → cm
                var d = (p - _startPointer) / Zoom;
                double x = _startX + d.X / l.Scale;
                double lift = _startLift - d.Y / l.Scale;
                double tol = 10 / Zoom / l.Scale;     // 画面で 10px 以内なら吸い付く
                bool snap = !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                vm.DragTo(c, x, lift, snap, tol);
                InvalidateVisual();
                break;
            }
            default:
                var h = HitTest(p);
                if (h != _hover) { _hover = h; InvalidateVisual(); }
                Cursor = h is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == DragMode.Move) ViewModel?.EndDrag();
        _drag = DragMode.None;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
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
        if (e.Key == Key.Space) { _spaceDown = true; return; }
        if (Selected is not { } c || ViewModel is null) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left: c.X -= step; break;
            case Key.Right: c.X += step; break;
            case Key.Up: c.Lift += step; break;
            case Key.Down: c.Lift = Math.Max(0, c.Lift - step); break;
            default: return;
        }
        c.Placed = true;
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space) _spaceDown = false;
    }
}
