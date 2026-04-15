using System.Windows;
using AIChatFoundryLocal.ViewModels;

namespace AIChatFoundryLocal;

/// <summary>
/// アプリケーションのメインウィンドウ。
/// <para>
/// ナビゲーションの切り替え（チャット画面 ↔ モデル管理画面）と
/// アプリ起動・終了時の処理を担う。
/// 各画面のコンテンツは <see cref="Views.ChatView"/> と
/// <see cref="Views.ModelManagementView"/> に分離されている。
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>アプリ全体の ViewModel。DataContext にバインドされる。</summary>
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// コンストラクター。
    /// ViewModel を生成して DataContext に設定し、各種イベントを購読する。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // チャット画面の「最下部へスクロール」イベントを View に接続する
        ChatViewControl.SubscribeScrollToBottom(_viewModel.ChatViewModel);

        Loaded  += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// ウィンドウ表示後に Foundry Local SDK の初期化（ServiceHost 起動）を開始する。
    /// </summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// ウィンドウを閉じるときに ServiceHost プロセスを終了してリソースを解放する。
    /// </summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Cleanup();
    }

    /// <summary>
    /// ナビゲーションの「チャット」ボタンが選択されたときにチャット画面を表示する。
    /// </summary>
    private void NavChat_Checked(object sender, RoutedEventArgs e)
    {
        if (ChatViewControl != null && ModelManagementViewControl != null)
        {
            ChatViewControl.Visibility          = Visibility.Visible;
            ModelManagementViewControl.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// ナビゲーションの「モデル管理」ボタンが選択されたときにモデル管理画面を表示する。
    /// </summary>
    private void NavModels_Checked(object sender, RoutedEventArgs e)
    {
        if (ChatViewControl != null && ModelManagementViewControl != null)
        {
            ChatViewControl.Visibility          = Visibility.Collapsed;
            ModelManagementViewControl.Visibility = Visibility.Visible;
        }
    }
}
