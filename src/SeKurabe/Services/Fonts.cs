using SkiaSharp;

namespace SeKurabe.Services;

/// <summary>描画に使うフォントの用意（OS の日本語フォント、足りない文字は持っているフォントに替える）。</summary>
public static class Fonts
{
    public static string DefaultFamily => OperatingSystem.IsMacOS() ? "Hiragino Sans" : "Yu Gothic UI";

    static readonly Dictionary<(string, bool), SKTypeface> Cache = new();

    public static SKFont Make(string? family, bool bold, float size, string sample)
    {
        var tf = Resolve(string.IsNullOrWhiteSpace(family) ? DefaultFamily : family!, bold, sample);
        return new SKFont(tf, Math.Max(1, size)) { Edging = SKFontEdging.Antialias, Subpixel = true };
    }

    static SKTypeface Resolve(string family, bool bold, string sample)
    {
        SKTypeface tf;
        lock (Cache)
        {
            if (!Cache.TryGetValue((family, bold), out tf!))
            {
                var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
                tf = SKFontManager.Default.MatchFamily(family, style)
                     ?? SKFontManager.Default.MatchFamily(DefaultFamily, style)
                     ?? SKTypeface.Default;
                Cache[(family, bold)] = tf;
            }
        }
        foreach (var ch in sample)
        {
            if (char.IsWhiteSpace(ch) || char.IsSurrogate(ch)) continue;
            if (!tf.ContainsGlyph(ch))
            {
                var alt = SKFontManager.Default.MatchCharacter(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal, ["ja-JP"], ch);
                if (alt is not null) return alt;
                break;
            }
        }
        return tf;
    }
}

/// <summary>身長の単位の換算と表記。</summary>
public static class Units
{
    public const double CmPerInch = 2.54;

    /// <summary>cm → (フィート, インチ)。インチは四捨五入し、12 になったら繰り上げる。</summary>
    public static (int Feet, int Inches) ToFeetInches(double cm)
    {
        var totalIn = (int)Math.Round(cm / CmPerInch);
        return (totalIn / 12, totalIn % 12);
    }

    public static double FromFeetInches(double feet, double inches) => (feet * 12 + inches) * CmPerInch;

    public static string Cm(double cm) => cm >= 100 || Math.Abs(cm - Math.Round(cm)) < 0.05 ? $"{cm:0.#} cm" : $"{cm:0.#} cm";

    public static string FtIn(double cm)
    {
        var (f, i) = ToFeetInches(cm);
        return f > 0 ? $"{f}'{i}\"" : $"{i}\"";
    }

    /// <summary>設定の単位に合わせた身長の表記。</summary>
    public static string Format(double cm, string unit) => unit switch
    {
        "ftin" => FtIn(cm),
        "both" => $"{Cm(cm)} / {FtIn(cm)}",
        _ => Cm(cm),
    };
}
