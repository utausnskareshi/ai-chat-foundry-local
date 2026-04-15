using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIChatFoundryLocal.Helpers;
using AIChatFoundryLocal.Models;
using AIChatFoundryLocal.Services;

namespace AIChatFoundryLocal.ViewModels;

/// <summary>
/// モデル管理画面の ViewModel。
/// <para>
/// Foundry Local カタログからモデル一覧を取得し、
/// ダウンロード・ロード・アンロード・削除の各操作を提供する。
/// </para>
/// </summary>
public partial class ModelManagementViewModel : ViewModelBase
{
    /// <summary>Foundry Local サービス（ServiceHost プロセスとの通信を担う）。</summary>
    private readonly FoundryLocalService _service;

    /// <summary>画面下部のステータスバーに表示するメッセージ。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>非同期処理（一覧取得・ダウンロード・ロード等）が進行中かどうか。UI のローディング表示に使用。</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>一覧で現在選択されているモデル。操作ボタンの有効/無効制御に使用。</summary>
    [ObservableProperty]
    public partial LocalModelInfo? SelectedModel { get; set; }

    /// <summary>モデル一覧。DataGrid にバインドされる。</summary>
    public ObservableCollection<LocalModelInfo> Models { get; } = new();

    /// <summary>
    /// モデルのロード・アンロード・削除が完了したときに発火するイベント。
    /// チャット画面のモデル状態更新（<see cref="ChatViewModel.UpdateModelStatus"/>）に使用される。
    /// </summary>
    public event Action? ModelLoadCompleted;

    /// <summary>
    /// コンストラクター。
    /// </summary>
    /// <param name="service">Foundry Local サービスのインスタンス。</param>
    public ModelManagementViewModel(FoundryLocalService service)
    {
        _service = service;
    }

    /// <summary>
    /// ServiceHost からモデル一覧を取得して画面を更新するコマンド。
    /// チャット非対応モデル（音声認識用の Whisper 等）は ServiceHost 側で除外済み。
    /// </summary>
    [RelayCommand]
    public async Task RefreshModelsAsync()
    {
        if (!_service.IsInitialized) return;

        IsLoading = true;
        StatusText = "モデル一覧を取得中...";

        try
        {
            var models = await _service.GetAvailableModelsAsync();

            // UI スレッドで ObservableCollection を更新する
            Application.Current.Dispatcher.Invoke(() =>
            {
                Models.Clear();
                foreach (var m in models)
                    Models.Add(m);
            });

            StatusText = $"{models.Count} 個のモデルが見つかりました";
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            if (ServiceCrashHelper.IsServiceCrashError(ex))
                ServiceCrashHelper.ShowCrashDialogAndPromptRestart();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 指定したモデルをローカルにダウンロードするコマンド。
    /// ダウンロード進捗率をリアルタイムで画面に反映する。
    /// </summary>
    /// <param name="model">ダウンロードするモデル情報。</param>
    [RelayCommand]
    private async Task DownloadModelAsync(LocalModelInfo? model)
    {
        if (model == null) return;

        model.IsDownloading = true;
        StatusText = $"'{model.Alias}' をダウンロード中...";

        try
        {
            await _service.DownloadModelAsync(model.Alias, progress =>
            {
                // ダウンロード進捗を UI スレッドで更新する
                Application.Current.Dispatcher.Invoke(() =>
                {
                    model.DownloadProgress = progress;
                    StatusText = $"'{model.Alias}' をダウンロード中... {progress:F1}%";
                    // ObservableCollection の変更通知を発火させるため要素を置き換える
                    var idx = Models.IndexOf(model);
                    if (idx >= 0)
                        Models[idx] = model;
                });
            });

            model.IsCached = true;
            model.IsDownloading = false;
            StatusText = $"'{model.Alias}' のダウンロードが完了しました";

            // ダウンロード完了後に一覧を最新状態に更新する
            await RefreshModelsAsync();
        }
        catch (Exception ex)
        {
            model.IsDownloading = false;
            StatusText = $"ダウンロードエラー: {ex.Message}";
            if (ServiceCrashHelper.IsServiceCrashError(ex))
                ServiceCrashHelper.ShowCrashDialogAndPromptRestart();
        }
    }

    /// <summary>
    /// 指定したモデルをメモリにロードして推論可能な状態にするコマンド。
    /// モデルが未ダウンロードの場合は自動的にダウンロードしてからロードする。
    /// 初回ロード時はモデルの最適化処理が行われるため時間がかかる場合がある。
    /// </summary>
    /// <param name="model">ロードするモデル情報。</param>
    [RelayCommand]
    private async Task LoadModelAsync(LocalModelInfo? model)
    {
        if (model == null) return;

        IsLoading = true;
        StatusText = $"'{model.Alias}' をロード中... (初回は時間がかかる場合があります)";

        try
        {
            await _service.LoadModelAsync(model.Alias, status =>
            {
                Application.Current.Dispatcher.Invoke(() => StatusText = status);
            });

            StatusText = $"'{model.Alias}' のロードが完了しました";
            await RefreshModelsAsync();
            // チャット画面のモデル状態更新イベントを発火
            ModelLoadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"ロードエラー: {ex.Message}";
            if (ServiceCrashHelper.IsServiceCrashError(ex))
                ServiceCrashHelper.ShowCrashDialogAndPromptRestart();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 現在メモリにロードされているモデルをアンロードするコマンド。
    /// アンロード後はチャットが使用不可になる。
    /// </summary>
    [RelayCommand]
    private async Task UnloadModelAsync()
    {
        IsLoading = true;
        StatusText = "モデルをアンロード中...";

        try
        {
            await _service.UnloadCurrentModelAsync();
            StatusText = "モデルをアンロードしました";
            await RefreshModelsAsync();
            ModelLoadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"アンロードエラー: {ex.Message}";
            if (ServiceCrashHelper.IsServiceCrashError(ex))
                ServiceCrashHelper.ShowCrashDialogAndPromptRestart();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 指定したモデルのローカルキャッシュ（ダウンロード済みファイル）を削除するコマンド。
    /// 削除前に確認ダイアログを表示する。再度使用する場合は再ダウンロードが必要になる。
    /// </summary>
    /// <param name="model">キャッシュを削除するモデル情報。</param>
    [RelayCommand]
    private async Task DeleteModelAsync(LocalModelInfo? model)
    {
        if (model == null) return;

        // 誤操作防止のため削除前に確認ダイアログを表示する
        var result = MessageBox.Show(
            $"モデル '{model.Alias}' のキャッシュを削除しますか？\n再度使用するにはダウンロードが必要になります。",
            "確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        StatusText = $"'{model.Alias}' を削除中...";

        try
        {
            await _service.DeleteModelCacheAsync(model.Alias);
            StatusText = $"'{model.Alias}' を削除しました";
            await RefreshModelsAsync();
            ModelLoadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"削除エラー: {ex.Message}";
            if (ServiceCrashHelper.IsServiceCrashError(ex))
                ServiceCrashHelper.ShowCrashDialogAndPromptRestart();
        }
        finally
        {
            IsLoading = false;
        }
    }
}
