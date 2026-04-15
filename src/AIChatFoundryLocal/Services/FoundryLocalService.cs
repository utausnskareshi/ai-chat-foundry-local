using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChatFoundryLocal.Models;

namespace AIChatFoundryLocal.Services;

/// <summary>
/// Foundry Local SDK をプロセス分離で使用するサービスクラス。
/// <para>
/// <b>設計の背景:</b><br/>
/// Foundry Local SDK の推論エンジン（OpenVINO 等）はネイティブコードで実装されており、
/// 推論中に 0xC0000409（Stack Buffer Overrun）等のネイティブクラッシュが発生することがある。
/// .NET の例外機構ではネイティブクラッシュを捕捉できず、プロセスが強制終了してしまう。
/// このため、推論処理を別プロセス（ServiceHost）で実行し、クラッシュがメインアプリに
/// 波及しないようにプロセス分離アーキテクチャを採用している。
/// </para>
/// <para>
/// <b>通信方式:</b><br/>
/// - チャット推論: ServiceHost が公開する OpenAI 互換 HTTP API（POST /v1/chat/completions）<br/>
/// - カタログ操作（一覧・ダウンロード等）: stdin/stdout テキストプロトコル
/// </para>
/// </summary>
public sealed class FoundryLocalService : IDisposable
{
    /// <summary>ServiceHost の子プロセス。</summary>
    private Process? _serviceProcess;

    /// <summary>ServiceHost が公開する Web サービスのベース URL（例: "http://127.0.0.1:12345"）。</summary>
    private string? _baseUrl;

    /// <summary>現在メモリにロードされているモデルのエイリアス名。</summary>
    private string? _currentModelAlias;

    /// <summary>初期化完了フラグ。</summary>
    private bool _initialized;

    /// <summary>チャット API への HTTP リクエストに使用するクライアント（タイムアウト5分）。</summary>
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// stdin コマンドの同時送信を防ぐための排他ロック。
    /// 複数のコマンドが並行して送信されると RESPONSE の対応関係が崩れるため1つずつ処理する。
    /// </summary>
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    /// <summary>ServiceHost プロセスが終了したときにキャンセルするトークンソース。</summary>
    private CancellationTokenSource? _processCts;

    /// <summary>
    /// ServiceHost の stdout を専用バックグラウンドタスクで読み取り、行ごとに格納するキュー。
    /// <para>
    /// 非同期読み取り（ReadLineAsync + Task.WhenAny）を使うと、プロセス終了後に
    /// ストリームがビジー状態になりデッドロックが発生することがある。
    /// そのため専用スレッドによる同期 ReadLine を採用している。
    /// </para>
    /// </summary>
    private readonly BlockingCollection<string> _outputLines = new();

    /// <summary>サービス動作ログの保存先パス。</summary>
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIChatFoundryLocal", "service.log");

    /// <summary>
    /// JSON シリアライズ/デシリアライズのオプション。
    /// <list type="bullet">
    /// <item>SnakeCaseLower: C# PascalCase プロパティ → JSON スネークケース（OpenAI API 互換）</item>
    /// <item>PropertyNameCaseInsensitive: 大文字小文字を無視してデシリアライズ（堅牢性向上）</item>
    /// <item>WhenWritingNull: null プロパティを JSON に含めない</item>
    /// </list>
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>サービスが初期化済みかどうか。</summary>
    public bool IsInitialized => _initialized;

    /// <summary>現在メモリにロードされているモデルのエイリアス名。未選択時は null。</summary>
    public string? CurrentModelAlias => _currentModelAlias;

    /// <summary>
    /// ServiceHost プロセスを起動して Foundry Local SDK を初期化する。
    /// READY メッセージを受信すると完了とみなす。
    /// </summary>
    /// <param name="statusCallback">初期化の進捗状況を UI に通知するコールバック。</param>
    /// <exception cref="InvalidOperationException">ServiceHost の起動に失敗した場合。</exception>
    /// <exception cref="TimeoutException">3分以内に READY メッセージを受信できなかった場合。</exception>
    public async Task InitializeAsync(Action<string>? statusCallback = null)
    {
        if (_initialized) return;

        statusCallback?.Invoke("サービスプロセスを起動中...");
        WriteLog("InitializeAsync: starting service host process");

        var serviceHostPath = FindServiceHostExe();
        if (serviceHostPath == null)
        {
            throw new InvalidOperationException(
                "AIChatFoundryLocal.ServiceHost.exe が見つかりません。\n"
                + "ソリューション全体をリビルドしてください。");
        }

        WriteLog($"ServiceHost path: {serviceHostPath}");

        // stdin/stdout/stderr をすべてリダイレクトしてウィンドウなしで起動する
        var psi = new ProcessStartInfo
        {
            FileName               = serviceHostPath,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = Path.GetDirectoryName(serviceHostPath) ?? ""
        };

        _serviceProcess = new Process { StartInfo = psi };
        _serviceProcess.Start();

        WriteLog($"ServiceHost PID: {_serviceProcess.Id}");

        // プロセス終了時に CancellationToken をキャンセルしてクラッシュを検知できるようにする
        _processCts = new CancellationTokenSource();
        _serviceProcess.EnableRaisingEvents = true;
        _serviceProcess.Exited += (_, _) =>
        {
            int exitCode = -1;
            try { exitCode = _serviceProcess.ExitCode; } catch { }
            WriteLog($"ServiceHost process exited. Exit code: {exitCode}");
            try { _processCts.Cancel(); } catch { }
        };

        // stdout の読み取りを専用バックグラウンドタスクに委譲する（デッドロック防止）
        _ = Task.Run(() => ReadOutputLoop(_serviceProcess.StandardOutput));

        // READY メッセージが届くまで最大3分待機する
        var readyTimeout = TimeSpan.FromMinutes(3);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < readyTimeout)
        {
            // プロセスが異常終了した場合は即座にエラーとする
            if (_serviceProcess.HasExited)
            {
                var stderr = await _serviceProcess.StandardError.ReadToEndAsync();
                WriteLog($"ServiceHost exited prematurely. Exit code: {_serviceProcess.ExitCode}, stderr: {stderr}");
                throw new InvalidOperationException(
                    $"サービスプロセスが予期せず終了しました (exit code: {_serviceProcess.ExitCode})\n{stderr}");
            }

            // 最大5秒待って次の行を取得する
            if (_outputLines.TryTake(out var line, TimeSpan.FromSeconds(5)))
            {
                WriteLog($"ServiceHost: {line}");

                if (line.StartsWith("READY|"))
                {
                    // Web サービスのベース URL を保存して初期化完了とする
                    _baseUrl = line["READY|".Length..].Trim().TrimEnd('/');
                    _initialized = true;
                    statusCallback?.Invoke("初期化完了");
                    WriteLog($"Service ready at: {_baseUrl}");
                    return;
                }
                else if (line.StartsWith("ERROR|"))
                {
                    throw new InvalidOperationException(
                        $"サービス初期化エラー: {line["ERROR|".Length..]}");
                }
                else if (line.StartsWith("STATUS|"))
                {
                    // 進捗メッセージを UI に転送する
                    statusCallback?.Invoke(line["STATUS|".Length..]);
                }
            }
        }

        throw new TimeoutException("サービスプロセスの起動がタイムアウトしました。");
    }

    /// <summary>
    /// ServiceHost の stdout を同期的に読み取り続け、1行ずつ <see cref="_outputLines"/> に追加するループ。
    /// 専用バックグラウンドスレッドで実行することで、複数箇所から非同期読み取りを行う際の
    /// "Stream currently in use" エラーを防ぐ。
    /// </summary>
    /// <param name="reader">ServiceHost の StandardOutput ストリームリーダー。</param>
    private void ReadOutputLoop(StreamReader reader)
    {
        try
        {
            while (true)
            {
                // 同期 ReadLine はブロッキングだが専用スレッドなので問題ない
                var line = reader.ReadLine();
                if (line == null) break; // プロセスが終了して EOF になった
                _outputLines.Add(line);
            }
        }
        catch (Exception ex)
        {
            WriteLog($"ReadOutputLoop error: {ex.Message}");
        }
        finally
        {
            // コレクションを完了状態にして、TryTake の待機を解除する
            _outputLines.CompleteAdding();
        }
    }

    /// <summary>
    /// ServiceHost の stdin にコマンドを送信し、対応する "RESPONSE|..." 行が返るまで待機する。
    /// コマンドの同時実行を防ぐため <see cref="_commandLock"/> で排他制御している。
    /// </summary>
    /// <param name="command">送信するコマンド文字列（例: "LOAD_MODEL|qwen2.5-0.5b"）。</param>
    /// <param name="timeout">応答待機のタイムアウト（省略時は5分）。</param>
    /// <returns>RESPONSE| プレフィックスを除いたレスポンス文字列。</returns>
    private async Task<string> SendCommandAsync(string command, TimeSpan? timeout = null)
    {
        if (_serviceProcess == null || _serviceProcess.HasExited)
            throw new InvalidOperationException("サービスプロセスが停止しています");

        await _commandLock.WaitAsync();
        try
        {
            await _serviceProcess.StandardInput.WriteLineAsync(command);
            await _serviceProcess.StandardInput.FlushAsync();

            var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);

            // BlockingCollection.TryTake はスレッドをブロックするため、
            // UI スレッドをブロックしないよう Task.Run でスレッドプールに委譲する
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();

                while (sw.Elapsed < effectiveTimeout)
                {
                    if (_serviceProcess.HasExited)
                        throw new InvalidOperationException("サービスプロセスがコマンド実行中にクラッシュしました");

                    // 最大2秒待って次の行を取得する
                    if (_outputLines.TryTake(out var line, TimeSpan.FromSeconds(2)))
                    {
                        WriteLog($"ServiceHost: {line}");

                        if (line.StartsWith("RESPONSE|"))
                            return line["RESPONSE|".Length..];

                        // ダウンロード進捗行は無視して次の行を待つ
                        if (line.StartsWith("PROGRESS|"))
                            continue;
                    }
                }

                throw new TimeoutException("コマンドがタイムアウトしました");
            });
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// ServiceHost からチャット対応モデルの一覧を取得する。
    /// チャット非対応モデル（Whisper 等）は ServiceHost 側で除外済み。
    /// </summary>
    /// <returns>利用可能なモデルの一覧。</returns>
    public async Task<List<LocalModelInfo>> GetAvailableModelsAsync()
    {
        var response = await SendCommandAsync("LIST_MODELS");
        if (response.StartsWith("ERROR:"))
            throw new InvalidOperationException(response["ERROR:".Length..]);

        var dtos = JsonSerializer.Deserialize<List<ModelDto>>(response) ?? new();
        return dtos.Select(d => new LocalModelInfo
        {
            Id             = d.Id,
            Alias          = d.Alias,
            DisplayName    = d.DisplayName,
            Description    = d.Description,
            IsCached       = d.IsCached,
            IsLoaded       = d.IsLoaded,
            SizeInfo       = d.SizeInfo,
            DeviceType     = d.DeviceType
        }).ToList();
    }

    /// <summary>
    /// 指定したモデルをローカルにダウンロードする。
    /// </summary>
    /// <param name="alias">ダウンロードするモデルのエイリアス名。</param>
    /// <param name="progressCallback">進捗率（0.0〜100.0）を受け取るコールバック（省略可）。</param>
    public async Task DownloadModelAsync(string alias, Action<double>? progressCallback = null)
    {
        var response = await SendCommandAsync($"DOWNLOAD_MODEL|{alias}", TimeSpan.FromMinutes(30));
        if (response.StartsWith("ERROR:"))
            throw new InvalidOperationException(response["ERROR:".Length..]);
    }

    /// <summary>
    /// 指定したモデルをメモリにロードして推論可能な状態にする。
    /// 未ダウンロードの場合は ServiceHost 側で自動的にダウンロードしてからロードする。
    /// </summary>
    /// <param name="alias">ロードするモデルのエイリアス名。</param>
    /// <param name="statusCallback">進捗メッセージを受け取るコールバック（省略可）。</param>
    public async Task LoadModelAsync(string alias, Action<string>? statusCallback = null)
    {
        statusCallback?.Invoke($"モデル '{alias}' をロード中... (初回は時間がかかる場合があります)");
        WriteLog($"LoadModel: {alias}");

        var response = await SendCommandAsync($"LOAD_MODEL|{alias}", TimeSpan.FromMinutes(10));
        if (response.StartsWith("ERROR:"))
            throw new InvalidOperationException(response["ERROR:".Length..]);

        _currentModelAlias = alias;
        statusCallback?.Invoke($"モデル '{alias}' のロード完了");
    }

    /// <summary>
    /// 現在メモリにロードされているモデルをアンロードする。
    /// </summary>
    public async Task UnloadCurrentModelAsync()
    {
        if (_currentModelAlias != null)
        {
            await SendCommandAsync($"UNLOAD_MODEL|{_currentModelAlias}");
            _currentModelAlias = null;
        }
    }

    /// <summary>
    /// 非ストリーミング方式でチャット応答を取得する。
    /// <para>
    /// HTTP リクエストと ServiceHost プロセス監視タスクを並行実行し、
    /// プロセスがクラッシュした場合は即座にエラーをスローする。
    /// ストリーミング（SSE）方式はサーバークラッシュの検知が遅延するため使用しない。
    /// </para>
    /// </summary>
    /// <param name="history">送信する会話履歴（システムメッセージを除く）。</param>
    /// <param name="systemPrompt">AI の動作方針を指示するシステムプロンプト。</param>
    /// <returns>AI の応答テキスト。応答が空の場合は "(応答なし)"。</returns>
    public async Task<string> ChatAsync(
        List<ChatMessageItem> history,
        string systemPrompt = "You are a helpful assistant. Respond in the same language the user uses.")
    {
        EnsureServiceRunning();

        if (string.IsNullOrEmpty(_currentModelAlias))
            throw new InvalidOperationException("モデルが選択されていません");

        var messages = BuildOpenAiMessages(history, systemPrompt);
        var requestBody = new OpenAiChatRequest
        {
            Model    = _currentModelAlias,
            Messages = messages,
            Stream   = false  // 非ストリーミング（クラッシュ検知の確実性を優先）
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        WriteLog($"ChatAsync: model={_currentModelAlias}, messages={messages.Count}");

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // タイムアウト3分の CancellationTokenSource を作成する
        using var requestCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        // HTTP リクエストとプロセスクラッシュ監視タスクを並行実行する
        var httpTask  = _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, requestCts.Token);
        var crashTask = WaitForProcessExitAsync(requestCts.Token);

        // どちらか先に完了した方を判定する
        var completedTask = await Task.WhenAny(httpTask, crashTask);

        if (completedTask == crashTask)
        {
            // プロセスクラッシュが先に検知された場合は HTTP リクエストをキャンセルしてエラーを返す
            requestCts.Cancel();
            WriteLog("ChatAsync: Service process crashed during request");
            throw new InvalidOperationException(
                "推論プロセスがクラッシュしました (exit code: 0xC0000409)。\n"
                + "このモデルは現在の環境では動作しない可能性があります。\n"
                + "別のモデルを試すか、アプリを再起動してください。");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpTask;
        }
        catch (OperationCanceledException)
        {
            if (IsServiceCrashed())
            {
                WriteLog("ChatAsync: Request timed out + service crashed");
                throw new InvalidOperationException(
                    "推論プロセスがクラッシュしました。\n"
                    + "別のモデルを試すか、アプリを再起動してください。");
            }
            throw new InvalidOperationException(
                "応答がタイムアウトしました (3分)。\nモデルの応答に時間がかかりすぎています。");
        }
        catch (HttpRequestException ex)
        {
            WriteLog($"ChatAsync: HTTP error. {ex.Message}");
            if (IsServiceCrashed())
                throw new InvalidOperationException(
                    "推論プロセスがクラッシュしました。\n"
                    + "別のモデルを試すか、アプリを再起動してください。", ex);
            throw;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        WriteLog($"ChatAsync: HTTP {(int)response.StatusCode}, len={responseJson.Length}");

        if (!response.IsSuccessStatusCode)
        {
            // レスポンスボディが空の場合はチャット非対応モデルである可能性を示す
            var detail = string.IsNullOrWhiteSpace(responseJson)
                ? "このモデルはチャットに対応していない可能性があります。別のモデルをお試しください。"
                : responseJson;
            throw new InvalidOperationException($"Chat API エラー (HTTP {(int)response.StatusCode}): {detail}");
        }

        var result = JsonSerializer.Deserialize<OpenAiChatResponse>(responseJson, JsonOptions);
        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? "(応答なし)";
    }

    /// <summary>
    /// ServiceHost プロセスが終了するまで 500ms ポーリングで待機するタスク。
    /// <see cref="ChatAsync"/> でクラッシュ検知に使用する。
    /// </summary>
    /// <param name="ct">キャンセルトークン（HTTP 応答完了後にキャンセルされる）。</param>
    private async Task WaitForProcessExitAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_serviceProcess == null || _serviceProcess.HasExited)
                return;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 指定したモデルのローカルキャッシュ（ダウンロードファイル）を削除する。
    /// </summary>
    /// <param name="alias">削除するモデルのエイリアス名。</param>
    public async Task DeleteModelCacheAsync(string alias)
    {
        // 現在ロード中のモデルを削除する場合はロード状態をクリアする
        if (_currentModelAlias == alias)
            _currentModelAlias = null;

        var response = await SendCommandAsync($"DELETE_MODEL|{alias}");
        if (response.StartsWith("ERROR:"))
            throw new InvalidOperationException(response["ERROR:".Length..]);
    }

    /// <summary>
    /// サービスが利用可能な状態かどうかを確認する。
    /// 未初期化またはプロセスがクラッシュしている場合は例外をスローする。
    /// </summary>
    private void EnsureServiceRunning()
    {
        if (!_initialized || string.IsNullOrEmpty(_baseUrl))
            throw new InvalidOperationException("サービスが初期化されていません");

        if (IsServiceCrashed())
            throw new InvalidOperationException(
                "推論サービスプロセスが停止しています。アプリを再起動してください。");
    }

    /// <summary>ServiceHost プロセスが既に終了しているかどうかを確認する。</summary>
    private bool IsServiceCrashed() =>
        _serviceProcess != null && _serviceProcess.HasExited;

    /// <summary>
    /// ServiceHost の実行ファイルパスを検索する。
    /// <list type="bullet">
    /// <item>開発時: ソリューションの bin ディレクトリを親フォルダをたどって検索する。</item>
    /// <item>発行時: メインアプリと同じディレクトリを確認する。</item>
    /// </list>
    /// .dll ファイルが同居しているパスのみ有効とする（.NET の実行に必要なため）。
    /// </summary>
    /// <returns>ServiceHost の実行ファイルの絶対パス。見つからない場合は null。</returns>
    private static string? FindServiceHostExe()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // 開発時: アプリの起動ディレクトリから親をたどりソリューションルートを探す
        var current = new DirectoryInfo(appDir);
        for (int i = 0; i < 10 && current != null; i++)
        {
            var hostBin = Path.Combine(current.FullName,
                "src", "AIChatFoundryLocal.ServiceHost", "bin");
            if (Directory.Exists(hostBin))
            {
                // .dll が同じフォルダに存在するパスのみ有効とする
                var found = Directory.GetFiles(hostBin,
                    "AIChatFoundryLocal.ServiceHost.exe",
                    SearchOption.AllDirectories)
                    .FirstOrDefault(p => File.Exists(
                        Path.Combine(Path.GetDirectoryName(p)!, "AIChatFoundryLocal.ServiceHost.dll")));
                if (found != null) return found;
            }
            current = current.Parent;
        }

        // 発行時: メインアプリと同じディレクトリに配置されている場合
        var candidate = Path.Combine(appDir, "AIChatFoundryLocal.ServiceHost.exe");
        if (File.Exists(candidate)
            && File.Exists(Path.Combine(appDir, "AIChatFoundryLocal.ServiceHost.dll")))
            return candidate;

        return null;
    }

    /// <summary>
    /// リソースを解放する。
    /// ServiceHost プロセスに QUIT コマンドを送って正常終了を試み、
    /// 3秒以内に終了しない場合は強制終了する。
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (_serviceProcess != null && !_serviceProcess.HasExited)
            {
                try { _serviceProcess.StandardInput.WriteLine("QUIT"); } catch { }
                if (!_serviceProcess.WaitForExit(3000))
                    _serviceProcess.Kill(); // 3秒以内に終了しない場合は強制終了
            }
            _serviceProcess?.Dispose();
        }
        catch { }
        _processCts?.Dispose();
        _outputLines.Dispose();
        _httpClient.Dispose();
        _commandLock.Dispose();
    }

    /// <summary>
    /// チャット履歴とシステムプロンプトから OpenAI API 形式のメッセージリストを構築する。
    /// </summary>
    /// <param name="history">アプリ内部の会話履歴。</param>
    /// <param name="systemPrompt">AI の動作方針を指示するシステムプロンプト。</param>
    /// <returns>OpenAI API に送信するメッセージリスト。</returns>
    private static List<OpenAiMessage> BuildOpenAiMessages(
        List<ChatMessageItem> history, string systemPrompt)
    {
        var messages = new List<OpenAiMessage>();

        // システムプロンプトを先頭に追加する
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new OpenAiMessage { Role = "system", Content = systemPrompt });

        // 会話履歴を OpenAI 形式に変換して追加する
        foreach (var msg in history)
        {
            messages.Add(new OpenAiMessage
            {
                Role = msg.Role switch
                {
                    ChatRole.User      => "user",
                    ChatRole.Assistant => "assistant",
                    ChatRole.System    => "system",
                    _                  => "user"
                },
                Content = msg.Content
            });
        }
        return messages;
    }

    /// <summary>
    /// タイムスタンプ付きでサービスログファイルにメッセージを追記する。
    /// ファイル書き込み失敗は無視する。
    /// </summary>
    /// <param name="message">記録するメッセージ。</param>
    private static void WriteLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    // -------------------------------------------------------------------------
    // OpenAI 互換 API の JSON モデル（プライベート内部クラス）
    // -------------------------------------------------------------------------

    /// <summary>POST /v1/chat/completions のリクエストボディ。</summary>
    private sealed class OpenAiChatRequest
    {
        /// <summary>使用するモデルのエイリアス名。</summary>
        public string Model { get; set; } = "";

        /// <summary>送信するメッセージの一覧。</summary>
        public List<OpenAiMessage> Messages { get; set; } = new();

        /// <summary>ストリーミングを使用するかどうか（常に false）。</summary>
        public bool Stream { get; set; }
    }

    /// <summary>OpenAI API の1件のメッセージ（role + content）。</summary>
    private sealed class OpenAiMessage
    {
        /// <summary>メッセージのロール（"system" / "user" / "assistant"）。</summary>
        public string Role { get; set; } = "";

        /// <summary>メッセージの本文テキスト。</summary>
        public string Content { get; set; } = "";
    }

    /// <summary>POST /v1/chat/completions のレスポンスボディ。</summary>
    private sealed class OpenAiChatResponse
    {
        /// <summary>生成された応答の候補一覧（通常は1件）。</summary>
        public List<OpenAiChoice>? Choices { get; set; }
    }

    /// <summary>OpenAI API のレスポンス候補の1件。</summary>
    private sealed class OpenAiChoice
    {
        /// <summary>AI が生成したメッセージ。</summary>
        public OpenAiMessage? Message { get; set; }
    }

    /// <summary>ServiceHost から受信する LIST_MODELS レスポンスの1件（JSON デシリアライズ用）。</summary>
    private sealed class ModelDto
    {
        public string Id          { get; set; } = "";
        public string Alias       { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool   IsCached    { get; set; }
        public bool   IsLoaded    { get; set; }
        public string SizeInfo    { get; set; } = "";
        public string DeviceType  { get; set; } = "";
    }
}
