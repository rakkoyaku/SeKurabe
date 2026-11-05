using SkiaSharp;

namespace SeKurabe.Services;

/// <summary>
/// 立ち絵からキャラの色（トレードカラー）を取り出し、配色をその色に合わせる。
/// </summary>
public static class ColorAnalyzer
{
    public readonly record struct Hsl(double H, double S, double L);

    /// <summary>
    /// 立ち絵の色を色相ごとに集計し、目立つ順に返す（最大 max 色）。
    /// 肌・線画・白など、彩度の低い色は数えない。
    /// </summary>
    public static List<SKColor> ExtractPalette(SKBitmap bmp, int max = 6)
    {
        const int Bins = 36;
        var weight = new double[Bins];
        var sumR = new double[Bins];
        var sumG = new double[Bins];
        var sumB = new double[Bins];
        int step = Math.Max(1, (int)Math.Sqrt((double)bmp.Width * bmp.Height / 160_000));   // 16 万点ほどを調べる

        for (int y = 0; y < bmp.Height; y += step)
        {
            for (int x = 0; x < bmp.Width; x += step)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha < 200) continue;
                var h = ToHsl(c);
                if (h.S < 0.22 || h.L < 0.12 || h.L > 0.92) continue;
                // 肌色（橙寄りで彩度・明度が中くらい）は弱く数える
                double w = h.S * h.S * (1 - Math.Abs(h.L - 0.5) * 1.2);
                if (h.H is >= 10 and <= 40 && h.S < 0.6 && h.L > 0.6) w *= 0.15;
                int bin = (int)(h.H / 360 * Bins) % Bins;
                weight[bin] += w;
                sumR[bin] += c.Red * w;
                sumG[bin] += c.Green * w;
                sumB[bin] += c.Blue * w;
            }
        }

        // となりの色相とまとめて山を作り、大きい山から順に取る（近すぎる色相は避ける）
        var smooth = new double[Bins];
        for (int i = 0; i < Bins; i++)
            smooth[i] = weight[(i + Bins - 1) % Bins] * 0.5 + weight[i] + weight[(i + 1) % Bins] * 0.5;
        double total = weight.Sum();
        var picked = new List<int>();
        var result = new List<SKColor>();
        while (result.Count < max)
        {
            int best = -1;
            for (int i = 0; i < Bins; i++)
            {
                if (picked.Any(p => HueDistance(p * 10, i * 10) < 40)) continue;
                if (best < 0 || smooth[i] > smooth[best]) best = i;
            }
            if (best < 0 || smooth[best] <= 0 || smooth[best] < total * 0.015) break;
            picked.Add(best);
            double w = 0, r = 0, g = 0, b = 0;
            for (int d = -1; d <= 1; d++)
            {
                int i = (best + d + Bins) % Bins;
                w += weight[i]; r += sumR[i]; g += sumG[i]; b += sumB[i];
            }
            if (w <= 0) continue;
            result.Add(new SKColor((byte)(r / w), (byte)(g / w), (byte)(b / w)));
        }
        return result;
    }

    /// <summary>
    /// 色相を shift 度回す。白・黒・灰色のような無彩色は変えない。
    /// 彩度はキャラの色に少し寄せる（くすんだキャラなら落ち着いた配色に）。
    /// </summary>
    public static string Rotate(string hex, double shift, SKColor main)
    {
        if (!SKColor.TryParse(hex, out var c)) return hex;
        var h = ToHsl(c);
        if (h.S < 0.1) return hex;
        var m = ToHsl(main);
        double s = Math.Clamp(h.S * 0.75 + m.S * 0.25, 0, 1);
        if (h.S < 0.35) s = Math.Min(s, h.S + 0.1);
        var n = FromHsl(new Hsl(h.H + shift, s, h.L)).WithAlpha(c.Alpha);
        return c.Alpha == 255 ? $"#{n.Red:X2}{n.Green:X2}{n.Blue:X2}" : $"#{n.Alpha:X2}{n.Red:X2}{n.Green:X2}{n.Blue:X2}";
    }

    public static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    public static Hsl ToHsl(SKColor c)
    {
        double r = c.Red / 255.0, g = c.Green / 255.0, b = c.Blue / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max - min > 1e-6)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return new Hsl(h, s, l);
    }

    public static SKColor FromHsl(Hsl hsl)
    {
        double h = (hsl.H % 360 + 360) % 360 / 360, s = hsl.S, l = hsl.L;
        double r, g, b;
        if (s < 1e-6) r = g = b = l;
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3); g = Hue(p, q, h); b = Hue(p, q, h - 1.0 / 3);
        }
        return new SKColor((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));

        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
