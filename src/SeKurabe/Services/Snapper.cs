namespace SeKurabe.Services;

/// <summary>吸い付いた先を示すガイド線（cm）。Horizontal なら高さ Pos の横線、そうでなければ X = Pos の縦線。</summary>
public sealed record SnapGuide(bool Horizontal, double Pos, double From, double To, string Label);

/// <summary>配置を動かすときの吸い付き（地面・ほかのキャラの頭や肩・中心・隣・最初の位置）。</summary>
public static class Snapper
{
    /// <summary>体の中の高さ（身長に対する割合）。立ち絵の頭身によって多少ずれるので目安。</summary>
    static readonly (double Ratio, string Name)[] BodyLevels =
    [
        (1.0, "頭頂"), (0.93, "目線"), (0.82, "肩"), (0.70, "胸"), (0.55, "腰"), (0.0, "足元"),
    ];

    public static (double X, double Lift, List<SnapGuide> Guides) Snap(
        CharData moving, double x, double lift, IReadOnlyList<CharData> others, double tolCm, double? defaultX)
    {
        var guides = new List<SnapGuide>();
        double h = moving.HeightCm, halfW = moving.WidthCm / 2;

        // ---- 高さ ----
        // 動かしているキャラの「足元」「頭頂」「中心」を、ほかのキャラの体の高さや地面に合わせる
        double bestDy = double.MaxValue, snappedLift = lift;
        SnapGuide? hGuide = null;
        void TryLift(double candidateLift, double lineY, string label, double from, double to)
        {
            var d = Math.Abs(candidateLift - lift);
            if (d <= tolCm && d < bestDy)
            {
                bestDy = d;
                snappedLift = candidateLift;
                hGuide = new SnapGuide(true, lineY, from, to, label);
            }
        }

        TryLift(0, 0, "地面", x - halfW, x + halfW);
        foreach (var o in others)
        {
            double oh = o.HeightCm;
            double from = Math.Min(x - halfW, o.X - o.WidthCm / 2), to = Math.Max(x + halfW, o.X + o.WidthCm / 2);
            foreach (var (ratio, name) in BodyLevels)
            {
                double level = o.Lift + oh * ratio;
                // 足元をその高さに（頭や肩に乗る・腕に抱えられる）
                if (ratio > 0) TryLift(level, level, $"足元 → {o.Name}の{name}", from, to);
                else TryLift(level, level, $"{o.Name}と同じ足元", from, to);
                // 頭頂をその高さに（頭の高さをそろえる・目線に合わせる）
                if (ratio >= 0.8) TryLift(level - h, level, $"頭頂 → {o.Name}の{name}", from, to);
                // 体の中心をその高さに
                if (ratio is > 0.5 and < 0.9) TryLift(level - h / 2, level, $"中心 → {o.Name}の{name}", from, to);
            }
        }
        if (hGuide is not null) guides.Add(hGuide);

        // ---- 横 ----
        double bestDx = double.MaxValue, snappedX = x;
        SnapGuide? vGuide = null;
        double bottom = snappedLift, top = snappedLift + h;
        void TryX(double candidateX, double lineX, string label, double from, double to)
        {
            var d = Math.Abs(candidateX - x);
            if (d <= tolCm && d < bestDx)
            {
                bestDx = d;
                snappedX = candidateX;
                vGuide = new SnapGuide(false, lineX, from, to, label);
            }
        }
        if (defaultX is { } dx) TryX(dx, dx, "最初の位置", bottom, top);
        foreach (var o in others)
        {
            double ow = o.WidthCm / 2;
            double from = Math.Min(bottom, o.Lift), to = Math.Max(top, o.Lift + o.HeightCm);
            TryX(o.X, o.X, $"{o.Name}と中心をそろえる", from, to);
            TryX(o.X + ow + halfW, o.X + ow, $"{o.Name}の右隣", from, to);
            TryX(o.X - ow - halfW, o.X - ow, $"{o.Name}の左隣", from, to);
        }
        if (vGuide is not null) guides.Add(vGuide);

        return (snappedX, Math.Max(-500, snappedLift), guides);
    }
}
