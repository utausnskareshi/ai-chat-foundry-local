using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIChatFoundryLocal.Helpers;
using AIChatFoundryLocal.Models;
using AIChatFoundryLocal.Services;

namespace AIChatFoundryLocal.ViewModels;

/// <summary>
/// AIチャット画面の ViewModel。
/// <para>
/// ユーザーからのメッセージ送受信、チャット履歴の管理、
/// モデルのロード状態表示を担う。
/// </para>
/// <para>
/// チャット応答は非ストリーミング方式で取得する。
/// ストリーミング方式は ServiceHost プロセスのクラッシュ検知が遅延するため使用しない。
/// </para>
/// </summary>
public partial class ChatViewModel : ViewModelBase
{
    /// <summary>Foundry Local サービス（ServiceHost プロセスとの通信を担う）。</summary>
    private readonly FoundryLocalService _service;

    /// <summary>ユーザーが入力中のテキスト。テキストボックスに双方向バインドされる。</summary>
    [ObservableProperty]
    public partial string UserInput { get; set; } = string.Empty;

    /// <summary>画面下部のステータスバーに表示するメッセージ。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "モデルが選択されていません";

    /// <summary>現在メッセージ送信・応答待機中かどうか。送信ボタンの有効/無効制御に使用。</summary>
    [ObservableProperty]
    public partial bool IsSending { get; set; }

    /// <summary>チャット可能なモデルがロード済みかどうか。送信ボタンの有効/無効制御に使用。</summary>
    [ObservableProperty]
    public partial bool IsModelLoaded { get; set; }

    /// <summary>現在ロードされているモデルのエイリアス名。ステータス表示に使用。</summary>
    [ObservableProperty]
    public partial string CurrentModelName { get; set; } = string.Empty;

    /// <summary>
    /// AI へ送るシステムプロンプト。
    /// モデルの応答スタイルや言語を指示する。
    /// </summary>
    [ObservableProperty]
    public partial string SystemPrompt { get; set; } = "You are a helpful assistant. Respond in the same language the user uses.";

    /// <summary>画面に表示するチャットメッセージの一覧。ListBox にバインドされる。</summary>
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    /// <summary>
    /// チャット一覧を最下部へスクロールさせるイベント。
    /// View 側（ChatView.xaml.cs）が購読してスクロール処理を実行する。
    /// </summary>
    public event Action? ScrollToBottomRequested;

    /// <summary>
    /// コンストラクター。
    /// </summary>
    /// <param name="service">Foundry Local サービスのインスタンス。</param>
    public ChatViewModel(FoundryLocalService service)
    {
        _service = service;
    }

    /// <summary>
    /// 現在ロードされているモデルの状態をサービスから取得して UI に反映する。
    /// モデル管理画面でのロード/アンロード完了後に呼び出される。
    /// </summary>
    public void UpdateModelStatus()
    {
        var alias = _service.CurrentModelAlias;
        IsModelLoaded = alias != null;
        CurrentModelName = alias ?? string.Empty;
        StatusText = IsModelLoaded
            ? $"モデル: {CurrentModelName} (準備完了)"
            : "モデルが選択されていません。「モデル管理」からモデルをロードしてください。";
    }

    /// <summary>
    /// メッセージ送信コマンド。
    /// ユーザーの入力をチャット履歴に追加し、AI への問い合わせを非同期で実行する。
    /// CanExecute は <see cref="CanSend"/> で制御される。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        var text = UserInput.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // ユーザーメッセージをチャット履歴に追加
        Messages.Add(new ChatMessageItem
        {
            Role = ChatRole.User,
            Content = text,
            Timestamp = DateTime.Now
        });
        UserInput = string.Empty;
        ScrollToBottomRequested?.Invoke();

        // AI 応答用のプレースホルダーメッセージを追加（応答受信後に内容を置き換える）
        var assistantMsg = new ChatMessageItem
        {
            Role = ChatRole.Assistant,
            Content = string.Empty,
            Timestamp = DateTime.Now,
        };
        Messages.Add(assistantMsg);
        ScrollToBottomRequested?.Invoke();

        IsSending = true;
        StatusText = "応答を生成中...";

        try
        {
            // システムメッセージを除いた会話履歴を API 送信用に構築する
            var historyForApi = Messages
                .Where(m => m != assistantMsg && m.Role != ChatRole.System)
                .Select(m => new ChatMessageItem
                {
                    Role = m.Role,
                    Content = m.Content
                })
                .ToList();

            // ChatAsync はブロッキング HTTP 呼び出しを含むため Task.Run でスレッドプールへオフロード
            var response = await Task.Run(
                () => _service.ChatAsync(historyForApi, SystemPrompt));

            // UI スレッドで応答内容をメッセージに反映する
            Application.Current.Dispatcher.Invoke(() =>
            {
                assistantMsg.Content = response ?? "(応答なし)";
                ReplaceMessage(assistantMsg);
                ScrollToBottomRequested?.Invoke();
            });

            StatusText = $"モデル: {CurrentModelName} (準備完了)";
        }
        catch (OperationCanceledException)
        {
            // ユーザーによるキャンセル操作
            assistantMsg.Content += "\n\n[キャンセルされました]";
            StatusText = $"モデル: {CurrentModelName} (準備完了)";
            ReplaceMessageAsync(assistantMsg);
        }
        catch (Exception ex)
        {
            // エラー詳細をチャットメッセージ内に表示する
            var errorDetail = ex.InnerException != null
                ? $"{ex.Message}\n内部: {ex.InnerException.Message}"
                : ex.Message;

            assistantMsg.Content = string.IsNullOrEmpty(assistantMsg.Content)
                ? $"[エラー: {errorDetail}]"
                : assistantMsg.Content + $"\n\n[エラー: {errorDetail}]";

            StatusText = $"エラー: {ex.Message}";
            ReplaceMessageAsync(assistantMsg);

            // ServiceHost プロセスがクラッシュした場合は再起動を促すダイアログを表示
            if (ServiceCrashHelper.IsServiceCrashError(ex))
                ServiceCrashHelper.ShowCrashDialogAndPromptRestart();
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>
    /// ObservableCollection の変更通知を発火させるため、指定した要素を同じ位置に置き換える。
    /// WPF の ListBox は要素の内部プロパティ変更を自動検知しないため、明示的な置き換えが必要。
    /// </summary>
    /// <param name="msg">置き換えるメッセージオブジェクト。</param>
    private void ReplaceMessage(ChatMessageItem msg)
    {
        var idx = Messages.IndexOf(msg);
        if (idx >= 0)
        {
            Messages.RemoveAt(idx);
            Messages.Insert(idx, msg);
        }
    }

    /// <summary>
    /// UI スレッド上で非同期にメッセージを置き換える。
    /// エラー・キャンセル時など、既に UI スレッド外にいる場合に使用する。
    /// </summary>
    /// <param name="msg">置き換えるメッセージオブジェクト。</param>
    private void ReplaceMessageAsync(ChatMessageItem msg)
    {
        Application.Current.Dispatcher.InvokeAsync(() => ReplaceMessage(msg));
    }

    /// <summary>
    /// 送信ボタンの有効条件。
    /// モデルがロード済み・送信中でない・入力欄が空でない場合のみ送信可能。
    /// </summary>
    private bool CanSend() => IsModelLoaded && !IsSending && !string.IsNullOrWhiteSpace(UserInput);

    // プロパティ変更時に送信ボタンの有効状態を再評価する
    partial void OnUserInputChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();
    partial void OnIsSendingChanged(bool value) => SendMessageCommand.NotifyCanExecuteChanged();
    partial void OnIsModelLoadedChanged(bool value) => SendMessageCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// チャット履歴をすべて消去するコマンド。
    /// </summary>
    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        StatusText = IsModelLoaded
            ? $"モデル: {CurrentModelName} (準備完了)"
            : "モデルが選択されていません";
    }
}
