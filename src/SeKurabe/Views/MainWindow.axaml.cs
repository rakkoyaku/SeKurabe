using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SeKurabe.Services;
using SeKurabe.ViewModels;

namespace SeKurabe.Views;

public partial class MainWindow : Window, IDialogs
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += (_, _) => Vm?.SaveLastNow();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                Vm?.SaveProjectCommand.Execute(null);
                e.Handled = true;
            }
        };

        // Windows では Avalonia がタイトルを描くので、自前のタイトルは Mac だけ
        TitleContent.IsVisible = OperatingSystem.IsMacOS();
        if (OperatingSystem.IsMacOS()) TitleContent.Margin = new Avalonia.Thickness(80, 0, 0, 0);
#if DEBUG
        if (Environment.GetEnvironmentVariable("SEKURABE_THEME") is { Length: > 0 } theme)
            Avalonia.Application.Current!.RequestedThemeVariant =
                theme == "Light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        if (Environment.GetEnvironmentVariable("SEKURABE_TEST") is { Length: > 0 } testDir)
            Opened += async (_, _) => await DebugTestAsync(testDir);
#endif
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is { } vm)
        {
            vm.Dialogs = this;
            Stage.ViewModel = vm;
        }
    }

#if DEBUG
    /// <summary>
    /// 開発用の自動確認。SEKURABE_TEST=出力フォルダ、SEKURABE_IMAGES=画像;画像、
    /// SEKURABE_CALIB=身長:地面Y:頭頂Y;…（画像の順）、SEKURABE_MOVES=番号:横cm:浮きcm;…（吸い付きあり）
    /// </summary>
    async Task DebugTestAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var vm = Vm!;
        async Task Shot(string name)
        {
            await Task.Delay(700);
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height));
            rtb.Render(this);
#pragma warning disable CS0618
            rtb.Save(Path.Combine(dir, name + ".png"));
#pragma warning restore CS0618
        }
        async Task Settle()
        {
            await Task.Delay(400);
            for (int i = 0; i < 50 && vm.IsBusy; i++) await Task.Delay(100);
            await Task.Delay(500);
        }

        await Task.Delay(1500);
        await Shot("00_empty");
        var images = (Environment.GetEnvironmentVariable("SEKURABE_IMAGES") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        await vm.AddImagesAsync(images);
        await Settle();
        await Shot("01_calibrate_initial");

        var calib = (Environment.GetEnvironmentVariable("SEKURABE_CALIB") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < Math.Min(calib.Length, vm.Characters.Count); i++)
        {
            var p = calib[i].Split(':');
            var c = vm.Characters[i];
            c.HeightCm = double.Parse(p[0]);
            if (p.Length > 1 && p[1].Length > 0) c.GroundY = double.Parse(p[1]);
            if (p.Length > 2 && p[2].Length > 0) c.TopY = double.Parse(p[2]);
            if (p.Length > 3) c.Name = p[3];
        }
        vm.Selected = vm.Characters.FirstOrDefault();
        await Settle();
        await Shot("02_calibrate_done");

        vm.Mode = "arrange";
        await Settle();
        await Shot("03_arrange_default");

        var moves = (Environment.GetEnvironmentVariable("SEKURABE_MOVES") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var mv in moves)
        {
            var p = mv.Split(':');
            var c = vm.Characters[int.Parse(p[0])];
            vm.Selected = c;
            vm.BeginDrag();
            vm.DragTo(c, double.Parse(p[1]), double.Parse(p[2]), snap: true, tolCm: 6);
            Console.WriteLine($"[move] {c.Name}: X={c.X:0.#} Lift={c.Lift:0.#} guides={string.Join(" / ", vm.Guides.Select(g => g.Label))}");
            await Task.Delay(300);
            await Shot($"04_drag_{c.Name}");
            vm.EndDrag();
        }
        await Settle();
        await Shot("05_arrange_final");
        if (Environment.GetEnvironmentVariable("SEKURABE_SPEC") is { Length: > 0 } spec)
            foreach (var kv in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = kv.Split('=', 2);
                var prop = typeof(SeKurabe.Models.ChartSettings).GetProperty(p[0]);
                if (prop is null) continue;
                prop.SetValue(vm.Settings, prop.PropertyType == typeof(bool) ? bool.Parse(p[1])
                    : prop.PropertyType == typeof(int) ? int.Parse(p[1])
                    : prop.PropertyType == typeof(double) ? double.Parse(p[1]) : p[1]);
            }
        await Settle();
#pragma warning disable CS0618
        vm.Preview?.Save(Path.Combine(dir, "06_preview.png"));
#pragma warning restore CS0618
        _saveImageOverride = Path.Combine(dir, "07_export.png");
        var sw = Stopwatch.StartNew();
        await vm.ExportCommand.ExecuteAsync(null);
        Console.WriteLine($"[export] {sw.ElapsedMilliseconds}ms {vm.SizeText}");
        await Shot("08_after_export");
        Close();
    }

    string? _saveImageOverride;
#endif

    // ---- ドラッグ＆ドロップ ----

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        DropOverlay.IsVisible = e.DragEffects != DragDropEffects.None;
    }

    void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (Vm is null || e.DataTransfer.TryGetFiles() is not { } items) return;
        await Vm.OpenPathsAsync(items.Select(i => i.TryGetLocalPath()).OfType<string>());
    }

    // ---- ソフトウェア情報 ----

    async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var dlg = new AboutDialog { DataContext = DataContext };
        await dlg.ShowDialog(this);
    }

    // ---- 表示 ----

    void OnFit(object? sender, RoutedEventArgs e) { if (Vm?.IsCalibrate == true) Calib.FitToView(); else Stage.FitToView(); }
    void OnZoomIn(object? sender, RoutedEventArgs e) { if (Vm?.IsCalibrate == true) Calib.ZoomBy(1.25); else Stage.ZoomBy(1.25); }
    void OnZoomOut(object? sender, RoutedEventArgs e) { if (Vm?.IsCalibrate == true) Calib.ZoomBy(0.8); else Stage.ZoomBy(0.8); }

    void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is { } vm) vm.Mode = "calibrate";
    }

    // ---- IDialogs ----

    static readonly FilePickerFileType ImageType = new("画像")
    {
        Patterns = ImageOps.SupportedExtensions.Select(x => "*" + x).ToArray(),
        AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "org.webmproject.webp", "com.microsoft.bmp"],
        MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/bmp"],
    };
    static readonly FilePickerFileType ProjectType = new("SeKurabe プロジェクト") { Patterns = ["*.sekurabe"] };

    public async Task<IReadOnlyList<string>> PickImagesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "立ち絵を選択（1 枚 = 1 人）", AllowMultiple = true, FileTypeFilter = [ImageType],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "プロジェクトを開く", AllowMultiple = false, FileTypeFilter = [ProjectType],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveProjectAsync(string suggestedName)
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "プロジェクトを保存", SuggestedFileName = suggestedName, DefaultExtension = "sekurabe",
            FileTypeChoices = [ProjectType], ShowOverwritePrompt = true,
        });
        return f?.TryGetLocalPath();
    }

    public async Task<string?> SaveImageAsync(string suggestedName, string ext)
    {
#if DEBUG
        if (_saveImageOverride is { } o) return o;
#endif
        var type = ext == "jpg"
            ? new FilePickerFileType("JPEG 画像") { Patterns = ["*.jpg", "*.jpeg"], AppleUniformTypeIdentifiers = ["public.jpeg"] }
            : new FilePickerFileType("PNG 画像") { Patterns = ["*.png"], AppleUniformTypeIdentifiers = ["public.png"] };
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "画像を書き出す", SuggestedFileName = suggestedName, DefaultExtension = ext,
            FileTypeChoices = [type], ShowOverwritePrompt = true,
        });
        return f?.TryGetLocalPath();
    }

    public async Task<int> ChooseAsync(string title, string message, params string[] buttons)
    {
        var dlg = new ChoiceDialog(title, message, null, buttons, DialogKind.Question);
        return await dlg.ShowDialog<int?>(this) ?? -1;
    }

    public void OpenFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start("open", [path]);
            else Process.Start("xdg-open", [path]);
        }
        catch { }
    }
}
