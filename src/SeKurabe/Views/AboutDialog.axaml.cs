using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SeKurabe.Services;

namespace SeKurabe.Views;

/// <summary>ソフトウェア情報（バージョン・アップデートの確認）。DataContext は MainViewModel。</summary>
public partial class AboutDialog : Window
{
    public AboutDialog() => InitializeComponent();

    void OnClose(object? sender, RoutedEventArgs e) => Close();
    void OnOpenRepo(object? sender, RoutedEventArgs e) => Open(Updater.RepoUrl);
    void OnOpenReleases(object? sender, RoutedEventArgs e) => Open(Updater.RepoUrl + "/releases");

    async void OnLicenses(object? sender, RoutedEventArgs e)
    {
        // アプリ本体のライセンス（MIT と、作った画像の権利について）→ 利用しているソフトウェアの表記
        async Task<string> Read(string name)
        {
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://SeKurabe/Assets/{name}"));
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch (Exception ex) { return $"{name} を読み込めませんでした: {ex.Message}"; }
        }
        var text = "■ SeKurabe のライセンス\n\n" + await Read("LICENSE.txt")
                   + "\n\n========================================================================\n"
                   + "■ 利用しているソフトウェア\n\n" + await Read("THIRD-PARTY-NOTICES.txt");

        var body = new SelectableTextBlock { Text = text, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(20, 16) };
        body.FontFamily = new Avalonia.Media.FontFamily(OperatingSystem.IsMacOS() ? "Menlo, Hiragino Sans" : "Consolas, Yu Gothic UI");
        var dlg = new Window
        {
            Title = "ライセンス",
            Width = 640, Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new ScrollViewer { Content = body },
        };
        dlg[!BackgroundProperty] = this.GetResourceObservable("Surface0").ToBinding();
        await dlg.ShowDialog(this);
    }

    void Open(string url) => _ = Launcher.LaunchUriAsync(new Uri(url));
}
