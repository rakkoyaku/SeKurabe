using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeKurabe.Models;

/// <summary>身長比較の画像全体の設定。</summary>
public sealed partial class ChartSettings : ObservableObject
{
    // ---- 単位と目盛り ----
    /// <summary>"cm" / "ftin" / "both"</summary>
    [ObservableProperty] public partial string Unit { get; set; } = "cm";
    /// <summary>"left" / "right" / "both" / "none"</summary>
    [ObservableProperty] public partial string RulerSide { get; set; } = "left";
    [ObservableProperty] public partial bool ShowGrid { get; set; } = true;

    // ---- 表記 ----
    /// <summary>身長の表記の位置: "side"（絵の横）/ "above"（絵の上）</summary>
    [ObservableProperty] public partial string LabelPlacement { get; set; } = "side";
    /// <summary>頭頂から定規までの点線。</summary>
    [ObservableProperty] public partial bool ShowHeadLines { get; set; } = true;
    /// <summary>頭頂の点線を、キャラの後ろに通す（キャラの体の上を横切らない）。</summary>
    [ObservableProperty] public partial bool HeadLinesBehind { get; set; } = true;
    [ObservableProperty] public partial bool ShowNames { get; set; } = true;
    [ObservableProperty] public partial bool ShowNameHeight { get; set; }

    // ---- 見た目 ----
    [ObservableProperty] public partial string BgColor { get; set; } = "#F6F4FA";
    [ObservableProperty] public partial string GridColor { get; set; } = "#D6D0E4";
    [ObservableProperty] public partial string TextColor { get; set; } = "#3C3258";
    [ObservableProperty] public partial bool ShowFloor { get; set; } = true;
    [ObservableProperty] public partial string FloorColor { get; set; } = "#E8E4F2";
    [ObservableProperty] public partial bool FootShadow { get; set; } = true;
    [ObservableProperty] public partial bool Outline { get; set; } = true;
    [ObservableProperty] public partial string OutlineColor { get; set; } = "#FFFFFF";
    [ObservableProperty] public partial double OutlineWidth { get; set; } = 3;
    [ObservableProperty] public partial string Font { get; set; } = "";

    // ---- 画像サイズ ----
    [ObservableProperty] public partial int Width { get; set; } = 2400;
    /// <summary>"auto"（中身に合わせる）/ "16:9" / "4:3" / "3:2" / "1:1"</summary>
    [ObservableProperty] public partial string Aspect { get; set; } = "auto";
    /// <summary>"png" / "jpg"</summary>
    [ObservableProperty] public partial string Format { get; set; } = "png";

    // ---- 並べ方 ----
    /// <summary>自動で並べるときのキャラの間隔（いちばん背の高いキャラの身長に対する %）。</summary>
    [ObservableProperty] public partial double GapPercent { get; set; } = 8;

    static readonly PropertyInfo[] Props = typeof(ChartSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0).ToArray();

    public void CopyFrom(ChartSettings other)
    {
        foreach (var p in Props) p.SetValue(this, p.GetValue(other));
    }

    public ChartSettings Clone()
    {
        var c = new ChartSettings();
        c.CopyFrom(this);
        return c;
    }
}

/// <summary>保存するプロジェクト（.sekurabe）。</summary>
public sealed class ProjectFile
{
    public int Version { get; set; } = 1;
    public List<Character> Characters { get; set; } = new();
    public ChartSettings Settings { get; set; } = new();
}
