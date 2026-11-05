using SkiaSharp;
using SeKurabe.Models;

namespace SeKurabe.Services;

/// <summary>描画のために写し取ったキャラの情報（描画中に元のデータが変わっても困らないように）。</summary>
public sealed record CharData(
    string Id, string Name, string ImagePath, double HeightCm, double GroundY, double TopY,
    double X, double Lift, bool Flip, bool ShowLabel, string Color, SKRectI Opaque, int ImageWidth)
{
    public double PxPerCm => HeightCm > 0 ? Math.Max(0.0001, (GroundY - TopY) / HeightCm) : 1;
    public double WidthCm => Opaque.Width / PxPerCm;
    public double ImageTopCm => (GroundY - Opaque.Top) / PxPerCm;
    public double CenterPx => Opaque.Left + Opaque.Width / 2.0;
    /// <summary>地面から浮いているか（寸法線で身長を示す）。</summary>
    public bool Floating => Lift > 0.5;

    public static CharData From(Character c) => new(c.Id, c.Name, c.ImagePath, c.HeightCm, c.GroundY, c.TopY,
        c.X, c.Lift, c.Flip, c.ShowLabel, c.EffectiveColor, c.Opaque, c.ImageWidth);
}

/// <summary>画像を取り出す口（プレビューは縮小版、書き出しは原寸）。scale = 返す画像のピクセル ÷ 元画像のピクセル。</summary>
public interface ICharImageSource
{
    SKBitmap? Get(CharData c, out double scale);
    void Release(SKBitmap bmp);
}

/// <summary>描く範囲（cm）。ドラッグ中は固定して、画面が伸び縮みしないようにする。</summary>
public sealed record Extents(double Left, double Right, double Top);

/// <summary>描いた結果の配置（画面での当たり判定やガイドの表示に使う）。</summary>
public sealed class ChartLayout
{
    public int Width, Height;
    public double Scale;          // 1cm あたりのピクセル
    public double OriginX;        // 0cm の位置（px）
    public double GroundPx;       // 地面の位置（px）
    public double ChartLeft, ChartRight, ChartTop;
    public Extents Extents = new(0, 100, 100);
    public readonly Dictionary<string, SKRect> CharRects = new();

    public double ToPxX(double cm) => OriginX + cm * Scale;
    public double ToPxY(double hcm) => GroundPx - hcm * Scale;
    public double ToCmX(double px) => (px - OriginX) / Scale;
    public double ToCmY(double px) => (GroundPx - px) / Scale;
}

/// <summary>身長比較の画像を描く。画面のプレビューと書き出しで同じ処理を使う。</summary>
public static class ChartRenderer
{
    /// <summary>キャラ全員が収まる範囲（cm）。</summary>
    public static Extents ComputeExtents(IReadOnlyList<CharData> chars)
    {
        if (chars.Count == 0) return new Extents(-50, 50, 180);
        double left = chars.Min(c => c.X - c.WidthCm / 2);
        double right = chars.Max(c => c.X + c.WidthCm / 2);
        double top = chars.Max(c => c.Lift + Math.Max(c.HeightCm, c.ImageTopCm));
        double pad = Math.Max(5, top * 0.05);
        return new Extents(left - pad, right + pad, top * 1.1 + pad * 0.5);
    }

    public static ChartLayout Layout(IReadOnlyList<CharData> chars, ChartSettings s, double scale, Extents? fixedExtents = null)
    {
        var ext = fixedExtents ?? ComputeExtents(chars);
        int W = Math.Max(64, (int)Math.Round(s.Width * scale));
        double u = W / 1000.0;
        double margin = 34 * u;
        double rulerL = s.RulerSide is "left" or "both" ? 70 * u : 0;
        double rulerR = s.RulerSide is "right" or "both" ? 70 * u : 0;
        double labelReserve = s.LabelPlacement == "side" ? 120 * u : 20 * u;
        double topPad = 30 * u;
        double floorH = s.ShowNames ? (s.ShowNameHeight ? 120 : 92) * u : 46 * u;

        double worldW = Math.Max(1, ext.Right - ext.Left);
        double worldH = Math.Max(1, ext.Top);
        double availW = W - 2 * margin - rulerL - rulerR - labelReserve;
        double sc;
        int H;
        if (TryRatio(s.Aspect, out var ratio))
        {
            H = (int)Math.Round(W / ratio);
            double availH = H - topPad - floorH - margin * 0.5;
            sc = Math.Min(availW / worldW, availH / worldH);
        }
        else
        {
            sc = availW / worldW;
            H = (int)Math.Round(topPad + worldH * sc + floorH + margin * 0.5);
            H = Math.Clamp(H, (int)(W * 0.25), W * 3);
        }

        var l = new ChartLayout
        {
            Width = W, Height = H, Scale = sc, Extents = ext,
            GroundPx = H - floorH,
            ChartLeft = margin + rulerL,
            ChartRight = W - margin - rulerR,
        };
        // 横は使える幅の中で中央に
        double used = worldW * sc;
        l.OriginX = margin + rulerL + (availW - used) / 2 + labelReserve * 0.15 - ext.Left * sc;
        l.ChartTop = l.ToPxY(ext.Top);
        return l;
    }

    public static (SKBitmap Bitmap, ChartLayout Layout) Render(IReadOnlyList<CharData> chars, ChartSettings s, ICharImageSource src,
        double scale, bool fast = false, Extents? fixedExtents = null, CancellationToken ct = default)
    {
        var l = Layout(chars, s, scale, fixedExtents);
        int W = l.Width, H = l.Height;
        double u = W / 1000.0;
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        var text = Col(s.TextColor, new SKColor(0x3C, 0x32, 0x58));
        c.Clear(Col(s.BgColor, SKColors.White));

        DrawGridAndRulers(c, s, l, u, text);

        // 地面
        if (s.ShowFloor)
            using (var fp = new SKPaint { Color = Col(s.FloorColor, new SKColor(0xE8, 0xE4, 0xF2)) })
                c.DrawRect(new SKRect(0, (float)l.GroundPx, W, H), fp);
        using (var gp = new SKPaint { Color = text.WithAlpha(150), StrokeWidth = (float)Math.Max(1, 2.2 * u), IsAntialias = true })
            c.DrawLine(0, (float)l.GroundPx, W, (float)l.GroundPx, gp);
        DrawRulers(c, s, l, u, text);
        ct.ThrowIfCancellationRequested();

        // 頭頂の点線をキャラの後ろに通す場合は、キャラより先に描く
        if (s.ShowHeadLines && s.HeadLinesBehind) DrawHeadLines(c, s, l, chars, u);

        // キャラ（一覧の順に奥から）。どこに絵があるかを「絵の地図」に記録しておき、札を置く場所の判断に使う
        using var occ = new Occupancy(W, H);
        foreach (var ch in chars)
        {
            ct.ThrowIfCancellationRequested();
            var rect = DrawCharacter(c, s, l, ch, src, u, fast, occ);
            if (rect is { } r) l.CharRects[ch.Id] = r;
        }

        if (s.ShowHeadLines && !s.HeadLinesBehind) DrawHeadLines(c, s, l, chars, u);

        DrawHeightLabels(c, s, l, chars, u, text, occ);
        if (s.ShowNames) DrawNames(c, s, l, chars, u, text);
        c.Flush();
        return (bmp, l);
    }

    // ================= 目盛り =================

    /// <summary>目盛りの間隔（cm）を、文字が詰まりすぎない大きさから選ぶ。</summary>
    static (double Major, double Minor) PickCmStep(double pxPerCm, double u)
    {
        double[] steps = [1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000];
        foreach (var st in steps)
            if (st * pxPerCm >= 55 * u)
                return (st, st is 25 ? 5 : st / (st.ToString().StartsWith("2") ? 2 : 5) );
        return (1000, 200);
    }

    static (double MajorIn, double MinorIn) PickInchStep(double pxPerCm, double u)
    {
        double pxPerIn = pxPerCm * Units.CmPerInch;
        double[] steps = [1, 2, 3, 6, 12, 24, 36, 60, 120, 240];
        foreach (var st in steps)
            if (st * pxPerIn >= 55 * u)
                return (st, st >= 12 ? st / (st == 12 ? 4 : 2) : Math.Max(1, st / 2));
        return (240, 60);
    }

    static void DrawGridAndRulers(SKCanvas c, ChartSettings s, ChartLayout l, double u, SKColor text)
    {
        var grid = Col(s.GridColor, new SKColor(0xD6, 0xD0, 0xE4));
        float gx0 = (float)l.ChartLeft, gx1 = (float)l.ChartRight;
        float top = (float)l.ChartTop;
        bool useCm = s.Unit != "ftin";
        var (major, minor) = PickCmStep(l.Scale, u);
        var (majorIn, minorIn) = PickInchStep(l.Scale, u);

        // 横線（主の単位の目盛りに合わせる）
        if (s.ShowGrid)
        {
            using var thin = new SKPaint { Color = grid.WithAlpha((byte)(grid.Alpha * 0.55)), StrokeWidth = (float)Math.Max(1, 1.2 * u), IsAntialias = true };
            using var thick = new SKPaint { Color = grid, StrokeWidth = (float)Math.Max(1, 2 * u), IsAntialias = true };
            double stepCm = useCm ? major : majorIn * Units.CmPerInch;
            double minorCm = useCm ? minor : minorIn * Units.CmPerInch;
            for (double h = minorCm; h <= l.Extents.Top + 0.001; h += minorCm)
            {
                bool isMajor = Math.Abs(h / stepCm - Math.Round(h / stepCm)) < 1e-6;
                float y = (float)l.ToPxY(h);
                if (y < top - 1) break;
                if (isMajor || minorCm * l.Scale >= 14 * u)
                    c.DrawLine(gx0, y, gx1, y, isMajor ? thick : thin);
            }
        }

    }

    static void DrawRulers(SKCanvas c, ChartSettings s, ChartLayout l, double u, SKColor text)
    {
        if (s.RulerSide is "left" or "both")
            DrawRuler(c, l, u, text, (float)l.ChartLeft, left: true, inches: s.Unit == "ftin");
        if (s.RulerSide is "right" or "both")
            DrawRuler(c, l, u, text, (float)l.ChartRight, left: false, inches: s.Unit is "ftin" or "both");
    }

    static void DrawRuler(SKCanvas c, ChartLayout l, double u, SKColor text, float x, bool left, bool inches)
    {
        float top = (float)l.ChartTop, ground = (float)l.GroundPx;
        using var line = new SKPaint { Color = text, StrokeWidth = (float)Math.Max(1, 2.4 * u), IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(x, top, x, ground, line);
        using var font = Fonts.Make(null, false, (float)(17 * u), "0123456789cm′″");
        using var tp = new SKPaint { Color = text.WithAlpha(210), IsAntialias = true };
        float dir = left ? -1 : 1;

        if (!inches)
        {
            var (major, minor) = PickCmStep(l.Scale, u);
            for (double h = 0; h <= l.Extents.Top + 0.001; h += minor)
            {
                float y = (float)l.ToPxY(h);
                if (y < top - 1) break;
                bool isMajor = Math.Abs(h / major - Math.Round(h / major)) < 1e-6;
                float len = (float)((isMajor ? 16 : 8) * u);
                c.DrawLine(x, y, x + dir * len, y, line);
                if (isMajor)
                    c.DrawText($"{h:0}", x + dir * (len + 6 * (float)u), y + font.Size * 0.36f, left ? SKTextAlign.Right : SKTextAlign.Left, font, tp);
            }
            c.DrawText("cm", x, top - 10 * (float)u, SKTextAlign.Center, font, tp);
        }
        else
        {
            var (majorIn, minorIn) = PickInchStep(l.Scale, u);
            double topIn = l.Extents.Top / Units.CmPerInch;
            for (double inch = 0; inch <= topIn + 0.001; inch += minorIn)
            {
                float y = (float)l.ToPxY(inch * Units.CmPerInch);
                if (y < top - 1) break;
                bool isMajor = Math.Abs(inch / majorIn - Math.Round(inch / majorIn)) < 1e-6;
                float len = (float)((isMajor ? 16 : 8) * u);
                c.DrawLine(x, y, x + dir * len, y, line);
                if (isMajor)
                {
                    int ft = (int)(inch / 12), rem = (int)Math.Round(inch % 12);
                    var label = rem == 0 ? $"{ft}'" : ft > 0 ? $"{ft}'{rem}\"" : $"{rem}\"";
                    c.DrawText(label, x + dir * (len + 6 * (float)u), y + font.Size * 0.36f, left ? SKTextAlign.Right : SKTextAlign.Left, font, tp);
                }
            }
            c.DrawText("ft", x, top - 10 * (float)u, SKTextAlign.Center, font, tp);
        }
    }

    // ================= キャラ =================

    /// <summary>立ち絵（絵の部分）を描く位置（出力ピクセル）。</summary>
    static SKRect? CharRect(ChartLayout l, CharData ch)
    {
        if (ch.Opaque.Width <= 0 || ch.Opaque.Height <= 0) return null;
        double k = l.Scale / ch.PxPerCm;     // 出力ピクセル ÷ 元画像ピクセル
        float cx = (float)l.ToPxX(ch.X);
        float footY = (float)l.ToPxY(ch.Lift);
        float w = (float)(ch.Opaque.Width * k), h = (float)(ch.Opaque.Height * k);
        return SKRect.Create(cx - w / 2, footY - (float)((ch.GroundY - ch.Opaque.Top) * k), w, h);
    }

    static SKRect? DrawCharacter(SKCanvas c, ChartSettings s, ChartLayout l, CharData ch, ICharImageSource src, double u, bool fast, Occupancy occ)
    {
        if (CharRect(l, ch) is not { } dst) return null;
        double k = l.Scale / ch.PxPerCm;
        float cx = dst.MidX;
        float w = dst.Width, h = dst.Height, left = dst.Left, top = dst.Top;

        // 足元の影（地面に立っているときだけ）
        if (s.FootShadow && !ch.Floating)
        {
            using var sp = new SKPaint
            {
                Color = SKColors.Black.WithAlpha(45), IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(6 * u)),
            };
            float sw = w * 0.36f, sh = (float)Math.Max(3, 7 * u);
            c.DrawOval(cx, (float)l.GroundPx, sw, sh, sp);
        }

        var img = src.Get(ch, out var sc);
        if (img is null) return dst;
        SKBitmap part;
        try
        {
            var srcRect = new SKRect((float)(ch.Opaque.Left * sc), (float)(ch.Opaque.Top * sc), (float)(ch.Opaque.Right * sc), (float)(ch.Opaque.Bottom * sc));
            part = ImageOps.Resample(img, srcRect, Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)), null);
        }
        finally { src.Release(img); }

        using (part)
        {
            occ.Add(part, dst, ch.Flip, cx);
            occ.Heads[ch.Id] = HeadSpan(part, dst, ch, l, cx);
            c.Save();
            if (ch.Flip) c.Scale(-1, 1, cx, 0);
            float x = (float)Math.Round(left), y = (float)Math.Round(top);
            if (s.Outline && s.OutlineWidth > 0 && !fast)
            {
                float r = (float)(s.OutlineWidth * u);
                using var dilate = SKImageFilter.CreateDilate(r, r);
                using var blur = SKImageFilter.CreateBlur((float)Math.Max(0.6, u * 0.7), (float)Math.Max(0.6, u * 0.7), dilate);
                using var tint = SKColorFilter.CreateBlendMode(Col(s.OutlineColor, SKColors.White), SKBlendMode.SrcIn);
                using var f = SKImageFilter.CreateColorFilter(tint, blur);
                using var op = new SKPaint { ImageFilter = f };
                c.DrawBitmap(part, x, y, op);
            }
            c.DrawBitmap(part, x, y);
            c.Restore();
        }
        return dst;
    }

    // ================= 身長の表記 =================

    sealed class Label
    {
        public required CharData Char;
        public required string Text;
        public SKRect Rect;
    }

    /// <summary>
    /// 描いた絵がどこにあるかの地図（1/4 の大きさのアルファだけの画像）。
    /// 身長の札を、ほかのキャラや自分の顔に重ならない場所へ置くのに使う。
    /// </summary>
    sealed class Occupancy : IDisposable
    {
        const int Div = 4;
        readonly SKBitmap _mask;
        readonly SKCanvas _canvas;
        public readonly Dictionary<string, (float Left, float Right)> Heads = new();

        public Occupancy(int w, int h)
        {
            _mask = new SKBitmap(new SKImageInfo(Math.Max(1, w / Div), Math.Max(1, h / Div), SKColorType.Alpha8, SKAlphaType.Premul));
            _canvas = new SKCanvas(_mask);
            _canvas.Clear(SKColors.Transparent);
        }

        public void Add(SKBitmap part, SKRect dst, bool flip, float cx)
        {
            _canvas.Save();
            _canvas.Scale(1f / Div);
            if (flip) _canvas.Scale(-1, 1, cx, 0);
            _canvas.DrawBitmap(part, dst);
            _canvas.Restore();
        }

        /// <summary>矩形の中の絵の濃さの平均（0〜1）。</summary>
        public unsafe float Coverage(SKRect r)
        {
            _canvas.Flush();
            int x0 = Math.Max(0, (int)(r.Left / Div)), x1 = Math.Min(_mask.Width, (int)Math.Ceiling(r.Right / Div));
            int y0 = Math.Max(0, (int)(r.Top / Div)), y1 = Math.Min(_mask.Height, (int)Math.Ceiling(r.Bottom / Div));
            if (x1 <= x0 || y1 <= y0) return 0;
            byte* p = (byte*)_mask.GetPixels();
            int stride = _mask.RowBytes;
            long sum = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) sum += p[y * stride + x];
            return sum / (255f * (x1 - x0) * (y1 - y0));
        }

        public void Dispose()
        {
            _canvas.Dispose();
            _mask.Dispose();
        }
    }

    /// <summary>頭の左右の端（出力ピクセル）。頭頂から身長の 1 割ほど下までの範囲で、絵がある幅を測る。</summary>
    static unsafe (float Left, float Right) HeadSpan(SKBitmap part, SKRect dst, CharData ch, ChartLayout l, float cx)
    {
        float headY = (float)l.ToPxY(ch.Lift + ch.HeightCm);
        int r0 = Math.Clamp((int)(headY - dst.Top), 0, part.Height - 1);
        int r1 = Math.Clamp((int)(headY - dst.Top + ch.HeightCm * 0.1 * l.Scale), r0 + 1, part.Height);
        byte* p = (byte*)part.GetPixels();
        int stride = part.RowBytes, minX = int.MaxValue, maxX = -1;
        for (int y = r0; y < r1; y += 2)
        {
            byte* row = p + y * stride;
            for (int x = 0; x < part.Width; x++)
                if (row[x * 4 + 3] > 96) { if (x < minX) minX = x; break; }
            for (int x = part.Width - 1; x >= 0; x--)
                if (row[x * 4 + 3] > 96) { if (x > maxX) maxX = x; break; }
        }
        if (maxX < 0) return (dst.Left, dst.Right);
        float left = dst.Left + minX, right = dst.Left + maxX + 1;
        // 左右反転していれば、中心で折り返す
        return ch.Flip ? (2 * cx - right, 2 * cx - left) : (left, right);
    }

    static void DrawHeightLabels(SKCanvas c, ChartSettings s, ChartLayout l, IReadOnlyList<CharData> chars, double u, SKColor text, Occupancy occ)
    {
        float fontSize = (float)(24 * u);
        using var font = Fonts.Make(s.Font, true, fontSize, "0123456789cm'\"/ ");
        float padX = fontSize * 0.55f, pillH = fontSize * 1.55f, gap = (float)(8 * u);
        var placed = new List<Label>();
        var leftRuler = s.RulerSide is "left" or "both";
        var rightRuler = s.RulerSide is "right" or "both";

        // 置き場所の候補の中から、絵やほかの札に重ならず、頭に近い場所を選ぶ
        float Score(SKRect r, float prefX, float prefY)
        {
            if (r.Left < 2 || r.Right > l.Width - 2 || r.Top < 2 || r.Bottom > l.GroundPx) return float.MaxValue;
            float score = occ.Coverage(r) * 12;
            foreach (var o in placed)
            {
                var i = SKRect.Intersect(r, o.Rect);
                if (!i.IsEmpty) score += 30 + i.Width * i.Height / (r.Width * r.Height) * 30;
            }
            // 定規の数字にかぶらないように
            if (leftRuler && r.Left < l.ChartLeft + 4) score += 5;
            if (rightRuler && r.Right > l.ChartRight - 4) score += 5;
            score += Math.Abs(r.MidX - prefX) / l.Width * 2.5f + Math.Abs(r.MidY - prefY) / l.Height * 4f;
            return score;
        }

        // 背の高い（上にいる）キャラから順に置く
        foreach (var ch in chars.OrderBy(ch => l.ToPxY(ch.Lift + ch.HeightCm)))
        {
            if (!ch.ShowLabel || !l.CharRects.TryGetValue(ch.Id, out var rect)) continue;
            var col = Col(ch.Color, SKColors.Gray);
            float headY = (float)l.ToPxY(ch.Lift + ch.HeightCm);
            float footY = (float)l.ToPxY(ch.Lift);
            var label = Units.Format(ch.HeightCm, s.Unit);
            float pillW = font.MeasureText(label) + padX * 2;
            var (headL, headR) = occ.Heads.GetValueOrDefault(ch.Id, (rect.Left, rect.Right));
            var cands = new List<SKRect>();
            float prefX, prefY;

            if (ch.Floating)
            {
                // 浮いているキャラは、足元から頭頂までの寸法線で示す（定規の目盛りと混同しないように）
                // 寸法線は絵の右か左の、空いている側に置く
                float bxR = rect.Right + (float)(10 * u), bxL = rect.Left - (float)(10 * u);
                float midY = (headY + footY) / 2;
                var lineR = SKRect.Create(bxR - 4, headY, 8, footY - headY);
                var lineL = SKRect.Create(bxL - 4, headY, 8, footY - headY);
                bool right = occ.Coverage(lineR) <= occ.Coverage(lineL) + 0.05f || bxL < l.ChartLeft;
                float bx = right ? bxR : bxL;
                using var lp = new SKPaint { Color = col, StrokeWidth = (float)Math.Max(1, 2.4 * u), IsAntialias = true, StrokeCap = SKStrokeCap.Round };
                float cap = (float)(7 * u);
                c.DrawLine(bx, headY, bx, footY, lp);
                c.DrawLine(bx - cap, headY, bx + cap, headY, lp);
                c.DrawLine(bx - cap, footY, bx + cap, footY, lp);
                prefX = right ? bx + gap + pillW / 2 : bx - gap - pillW / 2;
                prefY = midY;
                foreach (var dy in new[] { 0f, -pillH, pillH, (headY - midY), (footY - midY) })
                {
                    cands.Add(SKRect.Create(bx + gap * 1.5f, midY + dy - pillH / 2, pillW, pillH));
                    cands.Add(SKRect.Create(bx - gap * 1.5f - pillW, midY + dy - pillH / 2, pillW, pillH));
                }
                cands.Add(SKRect.Create((headL + headR) / 2 - pillW / 2, headY - pillH - gap, pillW, pillH));
            }
            else if (s.LabelPlacement == "above")
            {
                float hx = (headL + headR) / 2;
                float top = Math.Min(headY, rect.Top) - pillH - gap;
                prefX = hx; prefY = top + pillH / 2;
                foreach (var y in new[] { top, headY - pillH - gap, top - pillH - gap })
                    foreach (var dx in new[] { 0f, -0.6f, 0.6f, -1.2f, 1.2f })
                        cands.Add(SKRect.Create(hx - pillW / 2 + dx * pillW, y, pillW, pillH));
            }
            else
            {
                // 点線の上（頭のすぐ右が第一候補）。点線と同じ色なので、少し離れても誰の身長か分かる
                prefX = headR + gap + pillW / 2;
                prefY = headY;
                var ys = new[] { headY - pillH / 2, headY - pillH - gap * 0.5f };
                var xs = new List<float> { headR + gap, headL - gap - pillW, (float)l.ChartLeft + gap, (float)l.ChartRight - gap - pillW };
                for (int i = 0; i <= 12; i++)
                    xs.Add((float)(l.ChartLeft + gap + (l.ChartRight - l.ChartLeft - pillW - gap * 2) * i / 12.0));
                foreach (var y in ys)
                    foreach (var x in xs)
                        cands.Add(SKRect.Create(x, y, pillW, pillH));
            }

            var best = cands.MinBy(r => Score(r, prefX, prefY));
            placed.Add(new Label { Char = ch, Text = label, Rect = best });
        }

        foreach (var lb in placed)
        {
            var col = Col(lb.Char.Color, SKColors.Gray);
            // 札のまわりに背景色のふちを付けて、下の絵や線から浮かせる
            using (var halo = new SKPaint { Color = Col(s.BgColor, SKColors.White).WithAlpha(200), IsAntialias = true })
            {
                var hr = lb.Rect;
                hr.Inflate((float)(2.5 * u), (float)(2.5 * u));
                c.DrawRoundRect(hr, hr.Height / 2, hr.Height / 2, halo);
            }
            using (var pp = new SKPaint { Color = col, IsAntialias = true })
                c.DrawRoundRect(lb.Rect, lb.Rect.Height / 2, lb.Rect.Height / 2, pp);
            var fg = Luma(col) > 0.62f ? new SKColor(0x22, 0x22, 0x28) : SKColors.White;
            using var tp = new SKPaint { Color = fg, IsAntialias = true };
            font.MeasureText(lb.Text, out var tb);
            c.DrawText(lb.Text, lb.Rect.MidX, lb.Rect.MidY - tb.MidY, SKTextAlign.Center, font, tp);
        }
    }

    /// <summary>地面に立つキャラの、頭頂から定規までの点線。</summary>
    static void DrawHeadLines(SKCanvas c, ChartSettings s, ChartLayout l, IReadOnlyList<CharData> chars, double u)
    {
        foreach (var ch in chars)
        {
            if (!ch.ShowLabel || ch.Floating || CharRect(l, ch) is not { } rect) continue;
            var col = Col(ch.Color, SKColors.Gray);
            float headY = (float)l.ToPxY(ch.Lift + ch.HeightCm);
            using var dash = new SKPaint
            {
                Color = col.WithAlpha(220), StrokeWidth = (float)Math.Max(1, 2.2 * u), IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash([(float)(9 * u), (float)(7 * u)], 0),
            };
            float x0 = s.RulerSide is "left" or "both" ? (float)l.ChartLeft : rect.Left;
            float x1 = s.RulerSide is "right" or "both" ? (float)l.ChartRight : rect.Right;
            c.DrawLine(x0, headY, x1, headY, dash);
        }
    }

    static void DrawNames(SKCanvas c, ChartSettings s, ChartLayout l, IReadOnlyList<CharData> chars, double u, SKColor text)
    {
        foreach (var ch in chars)
        {
            if (string.IsNullOrWhiteSpace(ch.Name) || !l.CharRects.TryGetValue(ch.Id, out var rect)) continue;
            float cx = (float)l.ToPxX(ch.X);
            if (!ch.Floating)
            {
                float size = (float)(30 * u);
                using var font = Fonts.Make(s.Font, true, size, ch.Name);
                FitWidth(font, ch.Name, (float)Math.Max(rect.Width * 1.3, 140 * u));
                using var tp = new SKPaint { Color = text, IsAntialias = true };
                float y = (float)l.GroundPx + (float)(26 * u) + font.Size * 0.8f;
                c.DrawText(ch.Name, cx, y, SKTextAlign.Center, font, tp);
                // キャラの色の短い下線
                using var bp = new SKPaint { Color = Col(ch.Color, SKColors.Gray), IsAntialias = true };
                float bw = (float)(28 * u), bh = (float)Math.Max(2, 4 * u);
                c.DrawRoundRect(SKRect.Create(cx - bw / 2, y + font.Size * 0.32f, bw, bh), bh / 2, bh / 2, bp);
                if (s.ShowNameHeight)
                {
                    using var small = Fonts.Make(s.Font, false, (float)(20 * u), "0123456789cm′″/ ");
                    using var sp = new SKPaint { Color = text.WithAlpha(190), IsAntialias = true };
                    c.DrawText(Units.Format(ch.HeightCm, s.Unit), cx, y + font.Size * 0.32f + bh + small.Size * 1.3f, SKTextAlign.Center, small, sp);
                }
            }
            else
            {
                // 浮いているキャラの名前は足元に小さく
                float size = (float)(19 * u);
                using var font = Fonts.Make(s.Font, true, size, ch.Name);
                using var stroke = new SKPaint
                {
                    Color = Col(s.BgColor, SKColors.White), Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.28f,
                    StrokeJoin = SKStrokeJoin.Round, IsAntialias = true,
                };
                using var tp = new SKPaint { Color = text, IsAntialias = true };
                float y = (float)l.ToPxY(ch.Lift) + size * 1.25f;
                c.DrawText(ch.Name, cx, y, SKTextAlign.Center, font, stroke);
                c.DrawText(ch.Name, cx, y, SKTextAlign.Center, font, tp);
            }
        }
    }

    static void FitWidth(SKFont font, string text, float max)
    {
        var w = font.MeasureText(text);
        if (w > max && w > 0) font.Size *= max / w;
    }

    static SKColor Col(string? hex, SKColor fallback) => ImageOps.ParseColor(hex) ?? fallback;
    static float Luma(SKColor c) => (0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue) / 255f;

    static bool TryRatio(string? text, out double ratio)
    {
        ratio = 1;
        var p = (text ?? "").Split(':');
        if (p.Length == 2 && double.TryParse(p[0], out var a) && double.TryParse(p[1], out var b) && a > 0 && b > 0)
        {
            ratio = a / b;
            return true;
        }
        return false;
    }
}
