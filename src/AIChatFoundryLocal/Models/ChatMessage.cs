namespace AIChatFoundryLocal.Models;

/// <summary>
/// チャットメッセージの送信者ロールを表す列挙型。
/// </summary>
public enum ChatRole
{
    /// <summary>ユーザー（人間）が送信したメッセージ。</summary>
    User,

    /// <summary>AI アシスタントが生成したメッセージ。</summary>
    Assistant,

    /// <summary>システムプロンプト（AI の動作方針を指示するメッセージ）。</summary>
    System
}

/// <summary>
/// チャット画面に表示される1件のメッセージを表すモデルクラス。
/// </summary>
public class ChatMessageItem
{
    /// <summary>メッセージの送信者ロール（User / Assistant / System）。</summary>
    public ChatRole Role { get; set; }

    /// <summary>メッセージの本文テキスト。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>メッセージが作成された日時。</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
