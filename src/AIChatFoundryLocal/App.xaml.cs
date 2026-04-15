using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AIChatFoundryLocal;

/// <summary>
/// WPF アプリケーションのエントリポイントクラス。
/// <para>
/// アプリケーション全体の未処理例外をハンドリングし、
/// クラッシュログの記録とユーザーへのエラー通知を担う。
/// </para>
/// <para>
/// Foundry Local SDK のネイティブライブラリ（OpenVINO 等）は
/// .NET の例外機構では捕捉できないネイティブクラッシュ（0xC0000409 等）を
/// 発生させる場合があるため、Win32 API による低レベルハンドラも設置している。
/// ただし、このアプリでは推論処理を別プロセス（ServiceHost）に分離しているため、
/// メインプロセス側でネイティブクラッシュが発生することは通常ない。
/// </para>
/// </summary>
public partial class App : Application
{
    /// <summary>クラッシュログの保存先パス。</summary>
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIChatFoundryLocal", "crash.log");

    /// <summary>Win32 の未処理例外フィルター関数を設定する P/Invoke 宣言。</summary>
    [DllImport("kernel32.dll")]
    private static extern IntPtr SetUnhandledExceptionFilter(IntPtr lpTopLevelExceptionFilter);

    /// <summary>ネイティブコールバックデリゲートの型定義。</summary>
    private delegate int UnhandledExceptionFilterDelegate(IntPtr exceptionPointers);

    /// <summary>
    /// ネイティブクラッシュハンドラーへの参照。
    /// GC による回収を防ぐためフィールドとして保持する。
    /// </summary>
    private static UnhandledExceptionFilterDelegate? _nativeFilter;

    /// <summary>
    /// アプリケーション起動時の初期化処理。
    /// 各種例外ハンドラーを登録する。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ネイティブ（アンマネージド）未処理例外フィルタを設定
        InstallNativeCrashHandler();

        // WPF UI スレッドで発生した未処理例外（Dispatcher 経由）
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // バックグラウンドスレッドで発生した未処理例外
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // await されなかった Task 内で発生した未観測例外
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // アプリケーション終了時のログ記録
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>
    /// Win32 の SetUnhandledExceptionFilter を使ってネイティブクラッシュハンドラーを設置する。
    /// </summary>
    private static void InstallNativeCrashHandler()
    {
        _nativeFilter = NativeExceptionFilter;
        var ptr = Marshal.GetFunctionPointerForDelegate(_nativeFilter);
        SetUnhandledExceptionFilter(ptr);
    }

    /// <summary>
    /// ネイティブ未処理例外が発生したときに呼び出されるコールバック。
    /// クラッシュ情報をログに記録してユーザーに通知する。
    /// </summary>
    /// <param name="exceptionPointers">例外情報ポインター（EXCEPTION_POINTERS 構造体）。</param>
    /// <returns>EXCEPTION_EXECUTE_HANDLER (1) を返してデフォルトのクラッシュ処理を継続する。</returns>
    private static int NativeExceptionFilter(IntPtr exceptionPointers)
    {
        try
        {
            var message = $"[ネイティブクラッシュ検出]\n"
                        + $"ネイティブコード内で致命的な例外が発生しました。\n"
                        + $"ExceptionPointers: 0x{exceptionPointers:X}";
            WriteLog(message);

            MessageBox.Show(
                $"ネイティブコード内でクラッシュが発生しました。\n\n"
                + $"これは Foundry Local SDK のネイティブライブラリ内部で発生した問題です。\n"
                + $"アプリケーションを再起動してください。\n\n"
                + $"ログ: {LogPath}",
                "致命的エラー - AI Chat Foundry Local",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // クラッシュ状態での書き込み失敗は無視する
        }

        // EXCEPTION_EXECUTE_HANDLER = 1: OS のデフォルトクラッシュダイアログを表示して終了
        return 1;
    }

    /// <summary>
    /// WPF Dispatcher スレッド（UI スレッド）で未処理例外が発生した場合のハンドラー。
    /// ログ記録後にエラーダイアログを表示し、アプリの継続を試みる。
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var message = FormatException("UI スレッド例外", e.Exception);
        WriteLog(message);
        ShowErrorDialog(message);
        e.Handled = true; // アプリを継続させる
    }

    /// <summary>
    /// バックグラウンドスレッドで未処理例外が発生した場合のハンドラー。
    /// この例外は通常アプリをクラッシュさせるため、ログ記録後に終了ダイアログを表示する。
    /// </summary>
    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var message = FormatException("未処理例外 (AppDomain)", ex);
        WriteLog(message);

        MessageBox.Show(
            $"重大なエラーが発生しました。アプリケーションを終了します。\n\n{message}\n\nログ: {LogPath}",
            "致命的エラー - AI Chat Foundry Local",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// 非同期タスク内で発生した未観測例外（await されなかった例外）のハンドラー。
    /// ログ記録後に観測済みとしてマークし、アプリのクラッシュを防ぐ。
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var message = FormatException("非同期タスク例外", e.Exception?.InnerException ?? e.Exception);
        WriteLog(message);
        e.SetObserved(); // アプリのクラッシュを防止する
    }

    /// <summary>アプリケーション終了時のログ記録。</summary>
    private static void OnProcessExit(object? sender, EventArgs e)
    {
        WriteLog("[プロセス終了] アプリケーションが終了しました。");
    }

    /// <summary>
    /// 例外情報を人間が読みやすい文字列にフォーマットする。
    /// </summary>
    /// <param name="context">例外が発生したコンテキストの説明。</param>
    /// <param name="ex">フォーマットする例外オブジェクト。</param>
    /// <returns>フォーマットされた例外情報文字列。</returns>
    private static string FormatException(string context, Exception? ex)
    {
        if (ex == null) return $"[{context}] 不明なエラー";

        var inner = ex.InnerException != null
            ? $"\n内部例外: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
            : "";

        return $"[{context}]\n"
             + $"種類: {ex.GetType().FullName}\n"
             + $"メッセージ: {ex.Message}{inner}\n"
             + $"スタックトレース:\n{ex.StackTrace}";
    }

    /// <summary>
    /// ログメッセージをファイルに追記する。
    /// ファイル書き込み失敗は無視する（ログのためにアプリを落とさない）。
    /// </summary>
    /// <param name="message">記録するメッセージ。</param>
    private static void WriteLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath,
                $"\n========== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========\n{message}\n");
        }
        catch { }
    }

    /// <summary>
    /// 回復可能なエラーが発生したときにユーザーへ通知するダイアログを表示する。
    /// アプリは継続して動作する。
    /// </summary>
    /// <param name="message">表示するエラーメッセージ。</param>
    private static void ShowErrorDialog(string message)
    {
        MessageBox.Show(
            $"エラーが発生しました。操作を続行できます。\n\n{message}\n\nログ: {LogPath}",
            "エラー - AI Chat Foundry Local",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
