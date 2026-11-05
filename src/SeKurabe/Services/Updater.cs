using Velopack;
using Velopack.Sources;

namespace SeKurabe.Services;

/// <summary>
/// GitHub Releases からの自動更新（Velopack）。
/// 確認 → ダウンロードはアプリ内で行い、反映はアプリの再起動時（起動時に Velopack が適用する）。
/// </summary>
public sealed class Updater
{
    public const string RepoUrl = "https://github.com/rakkoyaku/SeKurabe";

    readonly UpdateManager? _mgr;
    UpdateInfo? _pending;

    public Updater()
    {
        try
        {
            // 動作確認用: SEKURABE_UPDATE_URL にフォルダや URL を指定すると、そこから更新を探す
            var overrideUrl = Environment.GetEnvironmentVariable("SEKURABE_UPDATE_URL");
            _mgr = string.IsNullOrEmpty(overrideUrl)
                ? new UpdateManager(new GithubSource(RepoUrl, null, false))
                : new UpdateManager(overrideUrl);
        }
        catch { _mgr = null; }
    }

    /// <summary>インストール版（Velopack で配布したもの）として動いているか。開発中の実行では false。</summary>
    public bool IsInstalled => _mgr?.IsInstalled == true;

    public string CurrentVersion =>
        (IsInstalled ? _mgr!.CurrentVersion?.ToString() : null)
        ?? typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>ダウンロード済みで、再起動を待っている更新のバージョン。</summary>
    public string? PendingRestartVersion => IsInstalled ? _mgr!.UpdatePendingRestart?.Version.ToString() : null;

    /// <summary>新しいバージョンがあればそのバージョン文字列、なければ null。</summary>
    public async Task<string?> CheckAsync()
    {
        if (!IsInstalled) return null;
        _pending = await _mgr!.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAsync(Action<int> progress, CancellationToken ct = default)
    {
        if (!IsInstalled || _pending is null) throw new InvalidOperationException("ダウンロードする更新がありません。");
        await _mgr!.DownloadUpdatesAsync(_pending, progress, cancelToken: ct).ConfigureAwait(false);
    }

    /// <summary>アプリを終了し、更新を適用して起動し直す。</summary>
    public void ApplyAndRestart()
    {
        if (!IsInstalled) return;
        var target = _pending?.TargetFullRelease ?? _mgr!.UpdatePendingRestart;
        _mgr!.ApplyUpdatesAndRestart(target);
    }
}
