using Avalonia;
using Avalonia.Media;

namespace SeKurabe;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 自動更新（Velopack）の処理。インストール・更新の直後などは、ここで処理して終了することがある。
        // ダウンロード済みの更新があれば、ここで適用してから起動する（＝再起動で反映）
        Velopack.VelopackApp.Build().Run();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { WriteCrashLog(e.Exception); e.SetObserved(); };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>落ちたときの原因を設定フォルダの crash.log に残す（Mac での不具合報告用）。</summary>
    static void WriteCrashLog(object ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SeKurabe");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTimeOffset.Now:O}]\n{ex}\n\n");
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        // 既定フォントのままだと日本語が中国語字形で出ることがあるため、OS の日本語フォントを明示する
        var family = OperatingSystem.IsMacOS() ? "Hiragino Sans" : "Yu Gothic UI";
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .With(new FontManagerOptions
            {
                DefaultFamilyName = family,
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily(family) }],
            })
            .LogToTrace();
    }
}
