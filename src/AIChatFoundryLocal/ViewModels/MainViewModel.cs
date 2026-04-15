using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIChatFoundryLocal.Services;

namespace AIChatFoundryLocal.ViewModels;

/// <summary>
/// アプリケーション全体を統括するメイン ViewModel。
/// <para>
/// 初期化処理の制御と、チャット画面・モデル管理画面それぞれの
/// ViewModel を生成・保持する責務を持つ。
/// </para>
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Foundry Local サービス。ServiceHost プロセスとの通信を担う。</summary>
    private readonly FoundryLocalService _service;

    /// <summary>起動・初期化中に表示するステータスメッセージ。</summary>
    [ObservableProperty]
    public partial string InitializationStatus { get; set; } = "Foundry Local を起動中...";

    /// <summary>初期化処理が進行中かどうか（スプラッシュ画面の表示制御に使用）。</summary>
    [ObservableProperty]
    public partial bool IsInitializing { get; set; } = true;

    /// <summary>初期化が正常に完了したかどうか（メイン UI の表示制御に使用）。</summary>
    [ObservableProperty]
    public partial bool IsInitialized { get; set; }

    /// <summary>現在選択されているナビゲーションタブのインデックス。</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>チャット画面の ViewModel。</summary>
    public ChatViewModel ChatViewModel { get; }

    /// <summary>モデル管理画面の ViewModel。</summary>
    public ModelManagementViewModel ModelManagementViewModel { get; }

    /// <summary>
    /// コンストラクター。
    /// サービスと各画面の ViewModel を生成し、モデルロード完了時の通知を設定する。
    /// </summary>
    public MainViewModel()
    {
        _service = new FoundryLocalService();
        ChatViewModel = new ChatViewModel(_service);
        ModelManagementViewModel = new ModelManagementViewModel(_service);

        // モデルがロード/アンロードされたとき、チャット画面のモデル状態を更新する
        ModelManagementViewModel.ModelLoadCompleted += () =>
        {
            ChatViewModel.UpdateModelStatus();
        };
    }

    /// <summary>
    /// アプリケーション起動時に呼び出す初期化コマンド。
    /// ServiceHost プロセスを起動し、Foundry Local SDK を準備する。
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        try
        {
            // ServiceHost の起動状況をステータスバーに反映しながら初期化を実行
            await _service.InitializeAsync(status =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    InitializationStatus = status;
                });
            });

            IsInitialized = true;
            IsInitializing = false;
            InitializationStatus = "準備完了";

            // チャット画面のモデル状態を更新し、モデル一覧を取得する
            ChatViewModel.UpdateModelStatus();
            await ModelManagementViewModel.RefreshModelsAsync();
        }
        catch (Exception ex)
        {
            // 初期化失敗時はスプラッシュ画面にエラーメッセージを表示する
            InitializationStatus = $"初期化エラー: {ex.Message}\n\nFoundry Local SDKがインストールされているか確認してください。";
            IsInitializing = false;
        }
    }

    /// <summary>
    /// アプリケーション終了時のクリーンアップ処理。
    /// ServiceHost プロセスに終了命令を送り、リソースを解放する。
    /// </summary>
    public void Cleanup()
    {
        _service.Dispose();
    }
}
