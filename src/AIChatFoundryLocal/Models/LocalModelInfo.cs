namespace AIChatFoundryLocal.Models;

/// <summary>
/// Foundry Local カタログに登録されているローカル AI モデルの情報を表すモデルクラス。
/// モデル管理画面の一覧表示に使用される。
/// </summary>
public class LocalModelInfo
{
    /// <summary>モデルの一意識別子（例: "qwen2.5-coder-0.5b-instruct-openvino-gpu:2"）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// モデルの短縮名（エイリアス）。
    /// API 呼び出しやコマンド送信時にはこの値を使用する（例: "qwen2.5-coder-0.5b"）。
    /// </summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>UI に表示するモデルの表示名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>モデルの説明文。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>モデルがローカルにダウンロード済みかどうか。</summary>
    public bool IsCached { get; set; }

    /// <summary>モデルが現在メモリにロードされて推論可能な状態かどうか。</summary>
    public bool IsLoaded { get; set; }

    /// <summary>現在のダウンロード進捗率（0.0〜100.0）。</summary>
    public double DownloadProgress { get; set; }

    /// <summary>現在ダウンロード中かどうか。</summary>
    public bool IsDownloading { get; set; }

    /// <summary>モデルのファイルサイズ情報（例: "365 MB"）。</summary>
    public string SizeInfo { get; set; } = string.Empty;

    /// <summary>推論デバイスの種類（例: "GPU", "CPU"）。</summary>
    public string DeviceType { get; set; } = string.Empty;
}
