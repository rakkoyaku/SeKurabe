using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SeKurabe.Models;

/// <summary>
/// 比べるキャラ 1 人。立ち絵の「地面」と「頭頂」の位置（画像のピクセル）と身長から縮尺を決める。
/// 配置は cm 単位（X = 横の位置、Lift = 地面からの浮き）。
/// </summary>
public sealed partial class Character : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] public partial string ImagePath { get; set; } = "";
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial int ImageWidth { get; set; }
    [ObservableProperty] public partial int ImageHeight { get; set; }

    /// <summary>足の裏の位置（画像の上からのピクセル）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PxPerCm))]
    public partial double GroundY { get; set; }

    /// <summary>身長のてっぺんの位置（画像の上からのピクセル）。角や帽子は含めない。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PxPerCm))]
    public partial double TopY { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PxPerCm))]
    public partial double HeightCm { get; set; } = 150;

    /// <summary>横の位置（cm）。立ち絵の絵の部分の中心。</summary>
    [ObservableProperty] public partial double X { get; set; }
    /// <summary>地面から足の裏までの高さ（cm）。0 なら地面に立つ。</summary>
    [ObservableProperty] public partial double Lift { get; set; }
    [ObservableProperty] public partial bool Flip { get; set; }
    /// <summary>身長の表記を出すか。</summary>
    [ObservableProperty] public partial bool ShowLabel { get; set; } = true;
    /// <summary>キャラの色（表記などに使う）。空なら立ち絵から自動。</summary>
    [ObservableProperty] public partial string Color { get; set; } = "";
    /// <summary>地面・頭頂・身長をユーザーが確認したか。</summary>
    [ObservableProperty] public partial bool Calibrated { get; set; }
    /// <summary>手で配置を動かしたか（並べ直すときに上書きしてよいかの判断に使う）。</summary>
    [ObservableProperty] public partial bool Placed { get; set; }

    // ---- 読み込み後に作る情報（保存しない） ----

    /// <summary>縮小した立ち絵（画面表示とプレビュー用）。</summary>
    [JsonIgnore] public SKBitmap? Proxy { get; set; }
    [JsonIgnore] public double ProxyScale => Proxy is null || ImageWidth == 0 ? 1 : (double)Proxy.Width / ImageWidth;
    /// <summary>絵の部分（透明でない範囲、画像のピクセル）。</summary>
    [JsonIgnore] public SKRectI Opaque { get; set; }
    [ObservableProperty, JsonIgnore] public partial AvBitmap? Thumbnail { get; set; }
    /// <summary>立ち絵から取り出した色（Color が空のときに使う）。</summary>
    [JsonIgnore] public string AutoColor { get; set; } = "#7A8A99";
    /// <summary>立ち絵から取り出した色の候補（目立つ順）。ほかのキャラと色がかぶらないように選ぶのに使う。</summary>
    [JsonIgnore] public List<string> Palette { get; set; } = new();
    [ObservableProperty, JsonIgnore] public partial string? LoadError { get; set; }

    /// <summary>この立ち絵の 1cm あたりのピクセル数。</summary>
    [JsonIgnore] public double PxPerCm => HeightCm > 0 ? Math.Max(0.0001, (GroundY - TopY) / HeightCm) : 1;

    [JsonIgnore] public string EffectiveColor => string.IsNullOrWhiteSpace(Color) ? AutoColor : Color;

    /// <summary>絵の部分の幅（cm）。</summary>
    [JsonIgnore] public double WidthCm => Opaque.Width / PxPerCm;

    /// <summary>絵の部分の上端が、足の裏から何 cm 上か（帽子や角も含む高さ）。</summary>
    [JsonIgnore] public double ImageTopCm => (GroundY - Opaque.Top) / PxPerCm;
    /// <summary>絵の部分の下端が、足の裏から何 cm 下か（影や台座など）。</summary>
    [JsonIgnore] public double ImageBottomCm => (Opaque.Bottom - GroundY) / PxPerCm;

    /// <summary>絵の中心の、画像上の X（ピクセル）。X（cm）はここに合わせる。</summary>
    [JsonIgnore] public double CenterPx => Opaque.Left + Opaque.Width / 2.0;

    partial void OnColorChanged(string value) => OnPropertyChanged(nameof(EffectiveColor));
    public void RaiseColor() => OnPropertyChanged(nameof(EffectiveColor));
}
