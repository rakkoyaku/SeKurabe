using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using SeKurabe.Models;
using SeKurabe.Services;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SeKurabe.ViewModels;

/// <summary>ViewModel から呼ぶ画面側の機能。</summary>
public interface IDialogs
{
    Task<IReadOnlyList<string>> PickImagesAsync();
    Task<string?> PickProjectAsync();
    Task<string?> SaveProjectAsync(string suggestedName);
    Task<string?> SaveImageAsync(string suggestedName, string ext);
    Task<int> ChooseAsync(string title, string message, params string[] buttons);
    void OpenFolder(string path);
}

public sealed partial class MainViewModel : ObservableObject
{
    const int ProxyMaxSide = 2000;
    const int PreviewMaxWidth = 1800;

    public IDialogs? Dialogs { get; set; }
    public ObservableCollection<Character> Characters { get; } = new();
    public ChartSettings Settings { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedImage), nameof(SelectedHeightText))]
    public partial Character? Selected { get; set; }

    /// <summary>"calibrate"（地面・頭頂の調整）/ "arrange"（並べる）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalibrate), nameof(IsArrange))]
    public partial string Mode { get; set; } = "arrange";
    public bool IsCalibrate => Mode == "calibrate";
    public bool IsArrange => Mode == "arrange";

    [ObservableProperty] public partial AvBitmap? Preview { get; set; }
    [ObservableProperty] public partial ChartLayout? PreviewLayout { get; set; }
    /// <summary>プレビューの縮尺（書き出し画像のピクセル → プレビューのピクセル）。</summary>
    public double PreviewScale { get; private set; } = 1;
    [ObservableProperty] public partial string SizeText { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string ProjectPath { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<SnapGuide> Guides { get; set; } = [];

    // 通知
    [ObservableProperty] public partial bool ToastVisible { get; set; }
    [ObservableProperty] public partial string ToastTitle { get; set; } = "";
    [ObservableProperty] public partial string ToastMessage { get; set; } = "";
    [ObservableProperty] public partial bool ToastIsError { get; set; }
    string? _toastFolder;
    DispatcherTimer? _toastTimer;

    public bool HasCharacters => Characters.Count > 0;
    public bool HasSelection => Selected is not null;
    public string ProjectName => string.IsNullOrEmpty(ProjectPath) ? "新しいプロジェクト" : Path.GetFileNameWithoutExtension(ProjectPath);
    public int UncalibratedCount => Characters.Count(c => !c.Calibrated && c.LoadError is null);
    public bool HasUncalibrated => UncalibratedCount > 0;

    /// <summary>調整画面に出す、選択中のキャラの画像（縮小版）。</summary>
    public AvBitmap? SelectedImage => Selected is { } c ? _displayBitmaps.GetValueOrDefault(c.Id) : null;
    readonly Dictionary<string, AvBitmap> _displayBitmaps = new();

    public string SelectedHeightText => Selected is { } c ? Units.Format(c.HeightCm, Settings.Unit) : "";

    DispatcherTimer? _renderTimer;
    DispatcherTimer? _saveTimer;
    CancellationTokenSource? _renderCts;
    bool _dragging;
    Extents? _frozen;
    bool _loading;

    public MainViewModel()
    {
        Characters.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (Character c in e.NewItems) c.PropertyChanged += OnCharChanged;
            if (e.OldItems is not null) foreach (Character c in e.OldItems) c.PropertyChanged -= OnCharChanged;
            OnPropertyChanged(nameof(HasCharacters));
            RaiseCalibration();
            AssignAutoColors();
            ScheduleRender();
            ScheduleSave();
        };
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChartSettings.Unit)) OnPropertyChanged(nameof(SelectedHeightText));
            if (e.PropertyName == nameof(ChartSettings.GapPercent)) ArrangeUnplaced();
            ScheduleRender();
            ScheduleSave();
        };
    }

    void OnCharChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Character c) return;
        switch (e.PropertyName)
        {
            case nameof(Character.Thumbnail) or nameof(Character.LoadError):
                return;
            case nameof(Character.GroundY) or nameof(Character.TopY) or nameof(Character.HeightCm):
                if (!_loading) c.Calibrated = true;
                if (c == Selected) OnPropertyChanged(nameof(SelectedHeightText));
                RaiseHeightFields();
                // 縮尺が変わると幅も変わるので、手で動かしていないキャラは並べ直す
                ArrangeUnplaced();
                break;
            case nameof(Character.Calibrated):
                RaiseCalibration();
                break;
        }
        if (!_dragging) ScheduleRender();
        ScheduleSave();
    }

    void RaiseCalibration()
    {
        OnPropertyChanged(nameof(UncalibratedCount));
        OnPropertyChanged(nameof(HasUncalibrated));
    }

    partial void OnSelectedChanged(Character? value) => RaiseHeightFields();
    partial void OnProjectPathChanged(string value) => OnPropertyChanged(nameof(ProjectName));

    // ---- 身長の入力（ft/in） ----

    public double SelectedFeet
    {
        get => Selected is { } c ? Units.ToFeetInches(c.HeightCm).Feet : 0;
        set { if (Selected is { } c) c.HeightCm = Math.Round(Units.FromFeetInches(value, SelectedInches), 1); }
    }

    public double SelectedInches
    {
        get => Selected is { } c ? Units.ToFeetInches(c.HeightCm).Inches : 0;
        set { if (Selected is { } c) c.HeightCm = Math.Round(Units.FromFeetInches(SelectedFeet, value), 1); }
    }

    void RaiseHeightFields()
    {
        OnPropertyChanged(nameof(SelectedFeet));
        OnPropertyChanged(nameof(SelectedInches));
    }

    // ---- 読み込み ----

    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        var project = list.FirstOrDefault(p => p.EndsWith(".sekurabe", StringComparison.OrdinalIgnoreCase));
        if (project is not null) { await LoadProjectAsync(project); return; }
        await AddImagesAsync(list);
    }

    public async Task AddImagesAsync(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p).Where(ImageOps.IsSupported).OrderBy(f => f, NaturalComparer.Instance));
            else if (File.Exists(p) && ImageOps.IsSupported(p)) files.Add(p);
        }
        if (files.Count == 0) { StatusText = "対応する画像が見つかりませんでした。"; return; }

        IsBusy = true;
        try
        {
            Character? first = null;
            foreach (var f in files)
            {
                var c = new Character { ImagePath = Path.GetFullPath(f), Name = Path.GetFileNameWithoutExtension(f) };
                if (!await LoadImageAsync(c, resetCalibration: true)) continue;
                c.HeightCm = DefaultHeight();
                c.Calibrated = false;
                Characters.Add(c);
                first ??= c;
            }
            ArrangeUnplaced();
            if (first is not null)
            {
                Selected = first;
                // 追加したら、まず地面と頭頂を合わせてもらう
                Mode = "calibrate";
            }
            StatusText = $"{files.Count}人を追加しました。それぞれ地面と頭頂の位置、身長を合わせてください。";
        }
        finally { IsBusy = false; }
    }

    /// <summary>新しく追加するキャラの仮の身長（いる人の平均。いなければ 150cm）。</summary>
    double DefaultHeight() => Characters.Count(c => c.Calibrated) is > 0 and var n
        ? Math.Round(Characters.Where(c => c.Calibrated).Average(c => c.HeightCm)) : 150;

    /// <summary>画像を読み込み、絵の範囲・縮小版・色を用意する。resetCalibration なら地面・頭頂を自動で置く。</summary>
    async Task<bool> LoadImageAsync(Character c, bool resetCalibration)
    {
        try
        {
            var (proxy, opaque, w, h, color, thumb, display) = await Task.Run(() =>
            {
                using var full = ImageOps.Decode(c.ImagePath);
                var op = ImageOps.AlphaBounds(full, 8);
                if (op.Width <= 0) op = new SKRectI(0, 0, full.Width, full.Height);
                var px = ImageOps.Downscale(full, ProxyMaxSide);
                // 表記に使うので、くすみすぎ・明るすぎ・暗すぎを避けた色にしておく
                var palette = ColorAnalyzer.ExtractPalette(px, 5).Select(Vivid).ToList();
                using var t = ImageOps.Downscale(px, 128);
                return (px, op, full.Width, full.Height, palette, ImageOps.ToAvalonia(t), ImageOps.ToAvalonia(px));
            });
            c.Proxy?.Dispose();
            c.Proxy = proxy;
            c.Opaque = opaque;
            c.Palette = color;
            c.Thumbnail = thumb;
            _displayBitmaps[c.Id] = display;
            _loading = true;
            try
            {
                c.ImageWidth = w;
                c.ImageHeight = h;
                if (resetCalibration)
                {
                    c.GroundY = opaque.Bottom;
                    c.TopY = opaque.Top;
                }
            }
            finally { _loading = false; }
            c.LoadError = null;
            AssignAutoColors();
            if (c == Selected) OnPropertyChanged(nameof(SelectedImage));
            return true;
        }
        catch (Exception ex)
        {
            c.LoadError = ex.Message;
            StatusText = $"{Path.GetFileName(c.ImagePath)} を読み込めませんでした: {ex.Message}";
            return false;
        }
    }

    static string Vivid(SKColor c)
    {
        var h = ColorAnalyzer.ToHsl(c);
        var v = ColorAnalyzer.FromHsl(new ColorAnalyzer.Hsl(h.H, Math.Clamp(Math.Max(h.S, 0.5), 0, 0.85), Math.Clamp(h.L, 0.42, 0.58)));
        return $"#{v.Red:X2}{v.Green:X2}{v.Blue:X2}";
    }

    /// <summary>
    /// 色を指定していないキャラに、立ち絵の色を割り当てる。前のキャラと色合いが近いときは、その立ち絵の次の候補を使う。
    /// </summary>
    void AssignAutoColors()
    {
        var used = new List<double>();
        foreach (var c in Characters)
        {
            string pick = c.Palette.FirstOrDefault() ?? "#7A8A99";
            foreach (var hex in c.Palette)
            {
                var hue = ColorAnalyzer.ToHsl(SKColor.Parse(hex)).H;
                if (used.All(u => ColorAnalyzer.HueDistance(u, hue) >= 35)) { pick = hex; break; }
            }
            if (string.IsNullOrWhiteSpace(c.Color)) used.Add(ColorAnalyzer.ToHsl(SKColor.Parse(pick)).H);
            else if (SKColor.TryParse(c.Color, out var own)) used.Add(ColorAnalyzer.ToHsl(own).H);
            if (c.AutoColor != pick)
            {
                c.AutoColor = pick;
                c.RaiseColor();
            }
        }
    }

    // ---- 並べ方 ----

    /// <summary>一覧の順に、ちょうどよい間隔で地面に並べたときの X（cm）。</summary>
    public Dictionary<string, double> ComputeSlots()
    {
        var result = new Dictionary<string, double>();
        var list = Characters.Where(c => c.LoadError is null && c.Opaque.Width > 0).ToList();
        if (list.Count == 0) return result;
        double maxH = list.Max(c => c.HeightCm);
        double gap = Math.Max(4, maxH * Settings.GapPercent / 100.0);
        double x = 0;
        foreach (var c in list)
        {
            double w = c.WidthCm;
            result[c.Id] = x + w / 2;
            x += w + gap;
        }
        return result;
    }

    /// <summary>手で動かしていないキャラを、最初の並びの位置に置く。</summary>
    void ArrangeUnplaced()
    {
        if (_dragging) return;
        var slots = ComputeSlots();
        foreach (var c in Characters)
            if (!c.Placed && slots.TryGetValue(c.Id, out var x))
            {
                c.X = x;
                c.Lift = 0;
            }
    }

    /// <summary>全員を地面に並べ直す（手で動かした配置も戻す）。byHeight なら背の高い順に並べ替える。</summary>
    [RelayCommand]
    void Rearrange(string? order)
    {
        if (order == "height")
        {
            var sorted = Characters.OrderByDescending(c => c.HeightCm).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var cur = Characters.IndexOf(sorted[i]);
                if (cur != i) Characters.Move(cur, i);
            }
        }
        foreach (var c in Characters) c.Placed = false;
        ArrangeUnplaced();
    }

    [RelayCommand]
    void ResetPosition()
    {
        if (Selected is not { } c) return;
        c.Placed = false;
        ArrangeUnplaced();
    }

    [RelayCommand]
    void ToGround()
    {
        if (Selected is { } c) c.Lift = 0;
    }

    [RelayCommand]
    void BringForward() => MoveSelected(+1);
    [RelayCommand]
    void SendBackward() => MoveSelected(-1);

    /// <summary>一覧の順番（＝重なりの順、並びの順）を入れ替える。手前 = 一覧の下。</summary>
    void MoveSelected(int delta)
    {
        if (Selected is not { } c) return;
        int i = Characters.IndexOf(c), j = Math.Clamp(i + delta, 0, Characters.Count - 1);
        if (i == j) return;
        Characters.Move(i, j);
        Selected = c;
        ArrangeUnplaced();
    }

    [RelayCommand]
    void RemoveSelected()
    {
        if (Selected is not { } c) return;
        int i = Characters.IndexOf(c);
        Characters.Remove(c);
        c.Proxy?.Dispose();
        _displayBitmaps.Remove(c.Id);
        Selected = Characters.Count > 0 ? Characters[Math.Clamp(i, 0, Characters.Count - 1)] : null;
        ArrangeUnplaced();
    }

    [RelayCommand]
    void AutoGround()
    {
        if (Selected is { } c) c.GroundY = c.Opaque.Bottom;
    }

    [RelayCommand]
    void AutoTop()
    {
        if (Selected is { } c) c.TopY = c.Opaque.Top;
    }

    [RelayCommand]
    void SetMode(string mode) => Mode = mode;

    // ---- ドラッグでの配置（画面から呼ぶ） ----

    public void BeginDrag()
    {
        _dragging = true;
        _frozen = PreviewLayout?.Extents;
    }

    /// <summary>ドラッグ中の位置を反映する。snap なら吸い付き先に合わせる。tolCm は吸い付く距離。</summary>
    public void DragTo(Character c, double x, double lift, bool snap, double tolCm)
    {
        var others = Characters.Where(o => o != c && o.LoadError is null).Select(CharData.From).ToList();
        if (snap)
        {
            var slots = ComputeSlots();
            var (sx, sl, guides) = Snapper.Snap(CharData.From(c), x, lift, others, tolCm, slots.GetValueOrDefault(c.Id, double.NaN) is var d && !double.IsNaN(d) ? d : null);
            x = sx; lift = sl;
            Guides = guides;
        }
        else Guides = [];
        c.X = x;
        c.Lift = Math.Max(0, Math.Abs(lift) < 0.05 ? 0 : lift);   // 地面より下には行かない
        c.Placed = true;
        ScheduleRender(immediate: true);
    }

    public void EndDrag()
    {
        _dragging = false;
        _frozen = null;
        Guides = [];
        ScheduleRender();
        ScheduleSave();
    }

    // ---- プレビュー ----

    void ScheduleRender(bool immediate = false)
    {
        _renderTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(70), DispatcherPriority.Background, (_, _) =>
        {
            _renderTimer!.Stop();
            _ = RenderPreviewAsync();
        });
        if (immediate && !_renderInFlight)
        {
            _renderTimer.Stop();
            _ = RenderPreviewAsync();
            return;
        }
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    bool _renderInFlight;

    async Task RenderPreviewAsync()
    {
        var chars = Characters.Where(c => c.LoadError is null && c.Proxy is not null).Select(CharData.From).ToList();
        var settings = Settings.Clone();
        var proxies = Characters.Where(c => c.Proxy is not null).ToDictionary(c => c.Id, c => (c.Proxy!, c.ProxyScale));
        bool fast = _dragging;
        var frozen = _frozen;
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        _renderInFlight = true;
        try
        {
            double scale = Math.Min(1.0, PreviewMaxWidth / (double)Math.Max(1, settings.Width));
            var (bmp, layout) = await Task.Run(() =>
            {
                var (sk, l) = ChartRenderer.Render(chars, settings, new ProxySource(proxies), scale, fast, frozen, cts.Token);
                using (sk) return (ImageOps.ToAvalonia(sk), l);
            }, cts.Token);
            if (cts.IsCancellationRequested) return;
            PreviewScale = scale;
            Preview = bmp;
            PreviewLayout = layout;
            var full = ChartRenderer.Layout(chars, settings, 1.0, frozen);
            SizeText = $"{full.Width} × {full.Height} px";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText = $"プレビューを作れませんでした: {ex.Message}"; }
        finally
        {
            _renderInFlight = false;
            if (_dragging && _pendingWhileDragging) { _pendingWhileDragging = false; ScheduleRender(immediate: true); }
        }
    }

    bool _pendingWhileDragging;

    sealed class ProxySource(Dictionary<string, (SKBitmap Bmp, double Scale)> proxies) : ICharImageSource
    {
        public SKBitmap? Get(CharData c, out double scale)
        {
            if (proxies.TryGetValue(c.Id, out var v)) { scale = v.Scale; return v.Bmp; }
            scale = 1;
            return null;
        }
        public void Release(SKBitmap bmp) { }
    }

    /// <summary>書き出し用: 原寸で読み込み、使い終わったら解放する。</summary>
    sealed class FullSource : ICharImageSource
    {
        public SKBitmap? Get(CharData c, out double scale)
        {
            scale = 1;
            try { return ImageOps.Decode(c.ImagePath); } catch { return null; }
        }
        public void Release(SKBitmap bmp) => bmp.Dispose();
    }

    // ---- 書き出し ----

    [RelayCommand]
    async Task Export()
    {
        if (Dialogs is null || Characters.Count == 0) return;
        if (HasUncalibrated)
        {
            var r = await Dialogs.ChooseAsync("まだ調整していないキャラがいます",
                $"{UncalibratedCount}人の地面・頭頂・身長がまだ確認されていません。このまま書き出しますか？", "書き出す", "キャンセル");
            if (r != 0) return;
        }
        var ext = Settings.Format == "jpg" ? "jpg" : "png";
        var path = await Dialogs.SaveImageAsync($"{ProjectName}.{ext}", ext);
        if (path is null) return;
        IsBusy = true;
        try
        {
            var chars = Characters.Where(c => c.LoadError is null).Select(CharData.From).ToList();
            var settings = Settings.Clone();
            await Task.Run(() =>
            {
                var (bmp, _) = ChartRenderer.Render(chars, settings, new FullSource(), 1.0);
                using (bmp)
                {
                    if (ext == "jpg") ImageOps.SaveJpeg(bmp, path, 92);
                    else ImageOps.SavePng(bmp, path);
                }
            });
            ShowToast("画像を書き出しました", Path.GetFileName(path), folder: Path.GetDirectoryName(path));
        }
        catch (Exception ex)
        {
            ShowToast("書き出せませんでした", ex.Message, isError: true);
        }
        finally { IsBusy = false; }
    }

    // ---- プロジェクト ----

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static string AppDir => Environment.GetEnvironmentVariable("SEKURABE_SETTINGS_DIR") is { Length: > 0 } d
        ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SeKurabe");
    static string LastPath => Path.Combine(AppDir, "last.sekurabe");

    ProjectFile Snapshot() => new() { Characters = Characters.ToList(), Settings = Settings.Clone() };

    void WriteProject(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Snapshot(), JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    void ScheduleSave()
    {
        if (_loading) return;
        _saveTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1.2), DispatcherPriority.Background, (_, _) =>
        {
            _saveTimer!.Stop();
            try { WriteProject(LastPath); } catch { }
        });
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>終了時などに、最後の状態をすぐ保存する。</summary>
    public void SaveLastNow()
    {
        try { WriteProject(LastPath); } catch { }
    }

    public async Task RestoreLastAsync()
    {
        if (File.Exists(LastPath)) await LoadProjectAsync(LastPath, remember: false);
    }

    async Task LoadProjectAsync(string path, bool remember = true)
    {
        ProjectFile? p;
        try { p = JsonSerializer.Deserialize<ProjectFile>(await File.ReadAllTextAsync(path), JsonOptions); }
        catch (Exception ex) { ShowToast("プロジェクトを開けませんでした", ex.Message, isError: true); return; }
        if (p is null) return;
        IsBusy = true;
        _loading = true;
        try
        {
            foreach (var c in Characters) c.Proxy?.Dispose();
            Characters.Clear();
            _displayBitmaps.Clear();
            Settings.CopyFrom(p.Settings);
            foreach (var c in p.Characters)
            {
                // 画像が見つからなければ、プロジェクトと同じフォルダも探す
                if (!File.Exists(c.ImagePath))
                {
                    var alt = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileName(c.ImagePath));
                    if (File.Exists(alt)) c.ImagePath = alt;
                }
                Characters.Add(c);
                await LoadImageAsync(c, resetCalibration: false);
            }
            ProjectPath = remember ? path : (p.Characters.Count > 0 ? ProjectPath : "");
            Selected = Characters.FirstOrDefault();
            Mode = "arrange";
        }
        finally
        {
            _loading = false;
            IsBusy = false;
        }
        ArrangeUnplaced();
        ScheduleRender(immediate: true);
        var missing = Characters.Count(c => c.LoadError is not null);
        if (missing > 0) ShowToast("見つからない画像があります", $"{missing}人の画像を読み込めませんでした。", isError: true);
    }

    [RelayCommand]
    async Task OpenProject()
    {
        if (Dialogs is null) return;
        var path = await Dialogs.PickProjectAsync();
        if (path is not null) await LoadProjectAsync(path);
    }

    [RelayCommand]
    async Task SaveProject()
    {
        if (Dialogs is null) return;
        var path = ProjectPath;
        if (string.IsNullOrEmpty(path))
        {
            path = await Dialogs.SaveProjectAsync($"{ProjectName}.sekurabe") ?? "";
            if (path.Length == 0) return;
        }
        try
        {
            WriteProject(path);
            ProjectPath = path;
            ShowToast("保存しました", Path.GetFileName(path));
        }
        catch (Exception ex) { ShowToast("保存できませんでした", ex.Message, isError: true); }
    }

    [RelayCommand]
    async Task SaveProjectAs()
    {
        if (Dialogs is null) return;
        var path = await Dialogs.SaveProjectAsync($"{ProjectName}.sekurabe");
        if (path is null) return;
        ProjectPath = "";
        try
        {
            WriteProject(path);
            ProjectPath = path;
            ShowToast("保存しました", Path.GetFileName(path));
        }
        catch (Exception ex) { ShowToast("保存できませんでした", ex.Message, isError: true); }
    }

    [RelayCommand]
    async Task NewProject()
    {
        if (Dialogs is not null && Characters.Count > 0)
        {
            var r = await Dialogs.ChooseAsync("新しいプロジェクト", "今の内容を閉じて、新しく始めますか？（保存していない変更は消えます）", "新しく始める", "キャンセル");
            if (r != 0) return;
        }
        foreach (var c in Characters) c.Proxy?.Dispose();
        Characters.Clear();
        _displayBitmaps.Clear();
        Settings.CopyFrom(new ChartSettings());
        ProjectPath = "";
        Selected = null;
        Preview = null;
    }

    [RelayCommand]
    async Task AddImages()
    {
        if (Dialogs is null) return;
        var files = await Dialogs.PickImagesAsync();
        if (files.Count > 0) await AddImagesAsync(files);
    }

    // ---- 通知 ----

    void ShowToast(string title, string message, bool isError = false, string? folder = null)
    {
        (ToastTitle, ToastMessage, ToastIsError, _toastFolder) = (title, message, isError, folder);
        OnPropertyChanged(nameof(ToastHasFolder));
        ToastVisible = true;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) =>
        {
            _toastTimer?.Stop();
            ToastVisible = false;
        });
        _toastTimer.Start();
    }

    public bool ToastHasFolder => _toastFolder is not null;

    [RelayCommand]
    void DismissToast() { _toastTimer?.Stop(); ToastVisible = false; }

    [RelayCommand]
    void OpenToastFolder()
    {
        if (_toastFolder is not null) Dialogs?.OpenFolder(_toastFolder);
        DismissToast();
    }
}
