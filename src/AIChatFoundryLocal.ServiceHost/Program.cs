// =============================================================================
// AIChatFoundryLocal.ServiceHost
// =============================================================================
// 役割:
//   Foundry Local SDK をメイン WPF アプリとは別プロセスで実行するホストプログラム。
//   推論エンジン（OpenVINO 等）のネイティブクラッシュが発生しても、
//   メインアプリ（WPF）が影響を受けないようにプロセス分離を実現する。
//
// 通信プロトコル (stdout):
//   起動フェーズ:
//     "STATUS|<メッセージ>"  - 初期化の進捗状況
//     "READY|<ベースURL>"    - Web サービス起動完了（例: "READY|http://127.0.0.1:12345"）
//     "ERROR|<メッセージ>"   - 致命的エラーで起動失敗
//   コマンド応答:
//     "RESPONSE|<JSON または OK または ERROR:…>" - stdin コマンドへの応答
//     "PROGRESS|<進捗率>"   - ダウンロード進捗（0.0〜100.0）
//
// 通信プロトコル (stdin):
//   "LIST_MODELS"            - チャット対応モデルの一覧を JSON で返す
//   "DOWNLOAD_MODEL|<alias>" - 指定モデルをダウンロードする
//   "LOAD_MODEL|<alias>"     - 指定モデルをメモリにロードする
//   "UNLOAD_MODEL|<alias>"   - 指定モデルをメモリからアンロードする
//   "DELETE_MODEL|<alias>"   - 指定モデルのキャッシュを削除する
//   "QUIT"                   - サービスを正常終了する
//
// チャット API:
//   ロードされたモデルへの推論は HTTP POST で呼び出す:
//   POST <baseUrl>/v1/chat/completions  (OpenAI 互換 API)
// =============================================================================

using System.Text.Json;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;

// Ctrl+C によるキャンセルを受け付けるトークンソース
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

FoundryLocalManager? manager = null;
ICatalog? catalog = null;

try
{
    Console.WriteLine("STATUS|Foundry Local SDK を初期化中...");

    // SDK 内部ログを Warning 以上のみ出力する（不要な情報ログを抑制）
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
    });
    var logger = loggerFactory.CreateLogger("ServiceHost");

    // Foundry Local の設定
    // Web サービスはポート 0（OS が空きポートを自動割り当て）でバインドする
    var config = new Configuration
    {
        AppName = "AIChatFoundryLocal",
        LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning,
        Web = new Configuration.WebService
        {
            Urls = "http://127.0.0.1:0"   // ポート 0 = OS が空きポートを自動割り当て
        }
    };

    // FoundryLocalManager のシングルトンインスタンスを生成・初期化する
    await FoundryLocalManager.CreateAsync(config, logger);
    manager = FoundryLocalManager.Instance;

    // 利用可能な実行プロバイダー（GPU ドライバー等）を確認・ダウンロードする
    Console.WriteLine("STATUS|実行プロバイダーを確認中...");
    var eps = manager.DiscoverEps();
    if (eps.Length > 0)
    {
        Console.WriteLine("STATUS|実行プロバイダーをダウンロード中...");
        await manager.DownloadAndRegisterEpsAsync((epName, percent) => { });
    }

    // OpenAI 互換 Web サービスを起動する
    Console.WriteLine("STATUS|Web サービスを起動中...");
    await manager.StartWebServiceAsync();

    // 実際にバインドされた URL を取得してメインアプリに通知する
    var urls = manager.Urls;
    if (urls == null || urls.Length == 0)
    {
        Console.WriteLine("ERROR|Web サービスの URL を取得できませんでした");
        return 1;
    }

    catalog = await manager.GetCatalogAsync();
    var baseUrl = urls[0].TrimEnd('/');

    // "READY|<url>" を出力してメインアプリに起動完了を通知する
    Console.WriteLine($"READY|{baseUrl}");

    // メインアプリからの stdin コマンドを処理するループ
    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line == null) break;   // stdin が閉じられた場合（親プロセス終了）
            if (line == "QUIT") break; // 明示的な終了命令

            await HandleCommandAsync(line, catalog, manager);
        }
    }
    catch (OperationCanceledException) { }

    // 正常終了: Web サービスを停止してリソースを解放する
    try { await manager.StopWebServiceAsync(); } catch { }
    manager.Dispose();
    return 0;
}
catch (Exception ex)
{
    // 初期化中の致命的エラーをメインアプリに通知して終了する
    Console.WriteLine($"ERROR|{ex.Message}");
    return 1;
}

/// <summary>
/// stdin から受信した1行のコマンドを解釈して処理し、結果を stdout に出力する。
/// </summary>
/// <param name="commandLine">stdin から受信したコマンド文字列（例: "LOAD_MODEL|qwen2.5-0.5b"）。</param>
/// <param name="catalog">Foundry Local モデルカタログ。</param>
/// <param name="manager">Foundry Local マネージャー。</param>
static async Task HandleCommandAsync(string commandLine, ICatalog catalog, FoundryLocalManager manager)
{
    try
    {
        // コマンド名と引数を "|" で分割する（引数なしの場合は空文字列）
        var parts = commandLine.Split('|', 2);
        var cmd = parts[0];
        var arg = parts.Length > 1 ? parts[1] : "";

        switch (cmd)
        {
            // モデル一覧の取得（チャット非対応モデルを除外して返す）
            case "LIST_MODELS":
            {
                var models       = await catalog.ListModelsAsync();
                var cachedModels = await catalog.GetCachedModelsAsync();
                var loadedModels = await catalog.GetLoadedModelsAsync();

                // キャッシュ済みモデルのエイリアスセット（大文字小文字を無視）
                var cachedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in cachedModels)
                {
                    cachedAliases.Add(m.Alias);
                    foreach (var v in m.Variants) cachedAliases.Add(v.Alias);
                }

                // メモリにロード済みモデルのエイリアスセット（大文字小文字を無視）
                var loadedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in loadedModels)
                {
                    loadedAliases.Add(m.Alias);
                    foreach (var v in m.Variants) loadedAliases.Add(v.Alias);
                }

                // チャット非対応モデル（音声認識用 Whisper 等）を除外するキーワード一覧
                var nonChatKeywords = new[] { "whisper" };

                var result = new List<ModelDto>();
                foreach (var model in models)
                {
                    // キーワードに一致するモデル（チャット非対応）をスキップする
                    var aliasLower = model.Alias.ToLowerInvariant();
                    if (nonChatKeywords.Any(k => aliasLower.Contains(k)))
                        continue;

                    var info = model.Info;
                    result.Add(new ModelDto
                    {
                        Id          = model.Id,
                        Alias       = model.Alias,
                        DisplayName = info?.DisplayName ?? model.Alias,
                        Description = info?.Name ?? $"Model: {model.Id}",
                        IsCached    = cachedAliases.Contains(model.Alias),
                        IsLoaded    = loadedAliases.Contains(model.Alias),
                        SizeInfo    = info?.FileSizeMb.HasValue == true ? $"{info.FileSizeMb.Value:F0} MB" : "",
                        DeviceType  = info?.Runtime?.DeviceType.ToString() ?? "Auto"
                    });
                }

                var json = JsonSerializer.Serialize(result);
                Console.WriteLine($"RESPONSE|{json}");
                break;
            }

            // モデルのダウンロード（進捗を PROGRESS| 形式で逐次出力する）
            case "DOWNLOAD_MODEL":
            {
                var model = await catalog.GetModelAsync(arg);
                if (model == null)
                {
                    Console.WriteLine($"RESPONSE|ERROR:Model '{arg}' not found");
                    break;
                }
                await model.DownloadAsync(progress =>
                {
                    // ダウンロード進捗をメインアプリへ送信する（例: "PROGRESS|42.3"）
                    Console.WriteLine($"PROGRESS|{progress:F1}");
                });
                Console.WriteLine("RESPONSE|OK");
                break;
            }

            // モデルのロード（未ダウンロードの場合は自動的にダウンロードしてからロードする）
            case "LOAD_MODEL":
            {
                var model = await catalog.GetModelAsync(arg);
                if (model == null)
                {
                    Console.WriteLine($"RESPONSE|ERROR:Model '{arg}' not found");
                    break;
                }
                await model.DownloadAsync(_ => { }); // ダウンロード済みなら即完了
                await model.LoadAsync();             // メモリにロードして推論可能状態にする
                Console.WriteLine("RESPONSE|OK");
                break;
            }

            // モデルのアンロード（メモリから解放する）
            case "UNLOAD_MODEL":
            {
                var model = await catalog.GetModelAsync(arg);
                if (model != null)
                    await model.UnloadAsync();
                Console.WriteLine("RESPONSE|OK");
                break;
            }

            // モデルキャッシュの削除（ローカルファイルを削除する）
            case "DELETE_MODEL":
            {
                var model = await catalog.GetModelAsync(arg);
                if (model != null)
                    await model.RemoveFromCacheAsync();
                Console.WriteLine("RESPONSE|OK");
                break;
            }

            default:
                Console.WriteLine($"RESPONSE|ERROR:Unknown command '{cmd}'");
                break;
        }
    }
    catch (Exception ex)
    {
        // コマンド処理中の例外はメインアプリにエラーとして返す
        Console.WriteLine($"RESPONSE|ERROR:{ex.Message}");
    }
}

/// <summary>
/// モデル一覧の JSON シリアライズ用 DTO（Data Transfer Object）。
/// ServiceHost → メインアプリ間の LIST_MODELS レスポンスに使用する。
/// </summary>
record ModelDto
{
    /// <summary>モデルの一意識別子（例: "qwen2.5-coder-0.5b-instruct-openvino-gpu:2"）。</summary>
    public string Id { get; init; } = "";

    /// <summary>モデルの短縮名（エイリアス）（例: "qwen2.5-coder-0.5b"）。</summary>
    public string Alias { get; init; } = "";

    /// <summary>UI に表示するモデルの表示名。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>モデルの説明文。</summary>
    public string Description { get; init; } = "";

    /// <summary>モデルがローカルにダウンロード済みかどうか。</summary>
    public bool IsCached { get; init; }

    /// <summary>モデルが現在メモリにロードされて推論可能な状態かどうか。</summary>
    public bool IsLoaded { get; init; }

    /// <summary>モデルのファイルサイズ情報（例: "365 MB"）。</summary>
    public string SizeInfo { get; init; } = "";

    /// <summary>推論デバイスの種類（例: "GPU", "CPU"）。</summary>
    public string DeviceType { get; init; } = "";
}
