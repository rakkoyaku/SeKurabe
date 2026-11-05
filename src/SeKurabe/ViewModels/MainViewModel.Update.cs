using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SeKurabe.Services;

namespace SeKurabe.ViewModels;

/// <summary>自動更新（GitHub Releases から確認・ダウンロードし、再起動で反映）。</summary>
public sealed partial class MainViewModel
{
    readonly Updater _updater = new();
    public string AppVersion => _updater.CurrentVersion;
    /// <summary>インストール版のときだけ更新を確認できる（開発中の実行では不可）。</summary>
#if DEBUG
    public bool CanCheckUpdates => _updater.IsInstalled || Environment.GetEnvironmentVariable("SEKURABE_FAKESTATE") is { Length: > 0 };
#else
    public bool CanCheckUpdates => _updater.IsInstalled;
#endif

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(IsUpdateDownloading), nameof(IsUpdateReady),
        nameof(IsUpdateError), nameof(HasUpdateBanner), nameof(CanCheckNow))]
    public partial string UpdateState { get; set; } = "none";   // none / available / downloading / ready / error
    [ObservableProperty] public partial string UpdateVersion { get; set; } = "";
    [ObservableProperty] public partial int UpdateProgress { get; set; }
    [ObservableProperty] public partial string UpdateMessage { get; set; } = "";
    [ObservableProperty] public partial string UpdateCheckResult { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckNow))]
    public partial bool IsCheckingUpdates { get; set; }
    [ObservableProperty] public partial bool IsUpToDate { get; set; }

    public bool IsUpdateAvailable => UpdateState == "available";
    public bool IsUpdateDownloading => UpdateState == "downloading";
    public bool IsUpdateReady => UpdateState == "ready";
    public bool IsUpdateError => UpdateState == "error";
    public bool HasUpdateBanner => UpdateState != "none";
    public bool CanCheckNow => CanCheckUpdates && !IsCheckingUpdates && UpdateState is "none" or "available" or "error";
    public string RepoUrl => Updater.RepoUrl;

    // ---- アプリ全体の設定（prefs.json） ----

    sealed class AppPrefs
    {
        public bool GuideExpanded { get; set; } = true;
        public bool CheckUpdatesOnStartup { get; set; } = true;
    }

    static string AppPrefsPath => Path.Combine(AppDir, "prefs.json");

    static AppPrefs ReadPrefs()
    {
        try { return JsonSerializer.Deserialize<AppPrefs>(File.ReadAllText(AppPrefsPath)) ?? new(); }
        catch { return new(); }
    }

    void WritePrefs()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(AppPrefsPath, JsonSerializer.Serialize(new AppPrefs { GuideExpanded = GuideExpanded, CheckUpdatesOnStartup = _checkOnStartup }));
        }
        catch { }
    }

    bool _checkOnStartup = ReadPrefs().CheckUpdatesOnStartup;

    /// <summary>起動時に新しいバージョンを確認するか（ソフトウェア情報で切り替える）。</summary>
    public bool CheckUpdatesOnStartup
    {
        get => _checkOnStartup;
        set
        {
            if (_checkOnStartup == value) return;
            _checkOnStartup = value;
            WritePrefs();
            OnPropertyChanged();
        }
    }

    /// <summary>起動時に裏で更新を確認する。失敗しても何も出さない。</summary>
    public async Task CheckUpdatesOnStartupAsync()
    {
        if (!_updater.IsInstalled || !_checkOnStartup) return;
        try
        {
            if (_updater.PendingRestartVersion is { } pending)
            {
                (UpdateVersion, UpdateState) = (pending, "ready");
                return;
            }
            if (await _updater.CheckAsync() is { } v)
                (UpdateVersion, UpdateState) = (v, "available");
        }
        catch { /* オフラインなど。起動時は黙っておく */ }
    }

    [RelayCommand]
    async Task CheckUpdates()
    {
        if (!_updater.IsInstalled || IsCheckingUpdates || UpdateState is "downloading" or "ready") return;
        IsCheckingUpdates = true;
        IsUpToDate = false;
        UpdateCheckResult = "";
        try
        {
            await Task.WhenAll(Task.Delay(400), Run());   // 一瞬で終わっても「確認した」ことが分かるよう少し待つ
            async Task Run()
            {
                if (await _updater.CheckAsync() is { } v)
                    (UpdateVersion, UpdateState) = (v, "available");
                else
                {
                    if (UpdateState == "error") UpdateState = "none";
                    IsUpToDate = true;
                    UpdateCheckResult = $"最新のバージョンです（{DateTime.Now:HH:mm} に確認）";
                }
            }
        }
        catch (Exception ex) { UpdateCheckResult = $"確認できませんでした: {ex.Message}"; }
        finally { IsCheckingUpdates = false; }
    }

    [RelayCommand]
    async Task DownloadUpdate()
    {
        if (UpdateState != "available" && UpdateState != "error") return;
        UpdateState = "downloading";
        UpdateProgress = 0;
        try
        {
            await _updater.DownloadAsync(p => Dispatcher.UIThread.Post(() => UpdateProgress = p));
            UpdateState = "ready";
        }
        catch (Exception ex)
        {
            UpdateMessage = ex.Message;
            UpdateState = "error";
        }
    }

    /// <summary>作業を保存してから終了し、更新を適用して起動し直す。</summary>
    [RelayCommand]
    void RestartToUpdate()
    {
        if (IsBusy) return;
        SaveLastNow();
        _updater.ApplyAndRestart();
    }

    [RelayCommand]
    void DismissUpdate()
    {
        if (UpdateState != "downloading") UpdateState = "none";
    }
}
