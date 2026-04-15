using System.Windows.Controls;
using AIChatFoundryLocal.ViewModels;

namespace AIChatFoundryLocal.Views;

/// <summary>
/// AI チャット画面の View（コードビハインド）。
/// <para>
/// XAML 側で定義できない「メッセージ一覧の自動スクロール」処理を実装する。
/// ViewModel からのイベント（<see cref="ChatViewModel.ScrollToBottomRequested"/>）を
/// 購読し、新着メッセージを受け取ったときに ListBox を最下部へスクロールさせる。
/// </para>
/// </summary>
public partial class ChatView : UserControl
{
    /// <summary>
    /// コンストラクター。XAML コンポーネントを初期化する。
    /// </summary>
    public ChatView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 指定した ViewModel の <see cref="ChatViewModel.ScrollToBottomRequested"/> イベントを購読し、
    /// メッセージ一覧（ListBox）を常に最新メッセージが見えるようスクロールさせる。
    /// </summary>
    /// <param name="vm">購読対象の <see cref="ChatViewModel"/>。</param>
    public void SubscribeScrollToBottom(ChatViewModel vm)
    {
        vm.ScrollToBottomRequested += () =>
        {
            // メッセージが1件以上あれば末尾の要素が見えるようにスクロールする
            if (MessagesListBox.Items.Count > 0)
            {
                MessagesListBox.ScrollIntoView(MessagesListBox.Items[^1]);
            }
        };
    }
}
