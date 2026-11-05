using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SeKurabe.ViewModels;
using SeKurabe.Views;

namespace SeKurabe;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            // 起動引数（画像やプロジェクトファイル）は、画面の準備ができてから読み込む
            if (desktop.Args is { Length: > 0 } args)
                desktop.MainWindow.Opened += (_, _) => _ = vm.OpenPathsAsync(args);
            else
                desktop.MainWindow.Opened += (_, _) => _ = vm.RestoreLastAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
