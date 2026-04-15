using System.Diagnostics;
using System.Windows;

namespace AIChatFoundryLocal.Helpers;

/// <summary>
/// AI 推論 ServiceHost プロセスのクラッシュ検出と、
/// ユーザーへの通知・再起動誘導を一元管理する静的ヘルパークラス。
/// <para>
/// ServiceHost プロセスは Foundry Local SDK のネイティブ推論エンジンをホストしており、
/// 推論中に 0xC0000409 (Stack Buffer Overrun) 等のネイティブクラッシュが発生することがある。
/// このヘルパーはそのクラッシュを検知してユーザーに分かりやすく通知する。
/// </para>
/// </summary>
public static class ServiceCrashHelper
{
    /// <summary>
    /// 例外メッセージを検査して、ServiceHost プロセスのクラッシュに起因するエラーかどうかを判定する。
    /// </summary>
    /// <param name="ex">判定する例外。</param>
    /// <returns>ServiceHost クラッシュ由来のエラーであれば <c>true</c>。</returns>
    public static bool IsServiceCrashError(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("クラッシュ") || msg.Contains("停止しています")
            || msg.Contains("crashed") || msg.Contains("exit code");
    }

    /// <summary>
    /// ServiceHost プロセスがクラッシュした際に目立つエラーダイアログを表示し、
    /// ユーザーにアプリケーションの再起動を促す。
    /// <para>
    /// Dispatcher.InvokeAsync を使用するため、UI スレッド・バックグラウンドスレッドの
    /// どちらからでも安全に呼び出せる。
    /// </para>
    /// </summary>
    public static void ShowCrashDialogAndPromptRestart()
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                "⚠ AI 推論サービスプロセスが予期せず停止しました。\n\n"
                + "これはモデルの推論エンジン内部で発生した問題です。\n"
                + "チャットを続けるにはアプリケーションの再起動が必要です。\n\n"
                + "今すぐアプリケーションを再起動しますか？",
                "サービス停止 - AI Chat Foundry Local",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
                RestartApplication();
        });
    }

    /// <summary>
    /// 現在の実行ファイルを新しいプロセスで起動してから、現在のプロセスを終了する。
    /// </summary>
    private static void RestartApplication()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
            Application.Current.Shutdown();
        }
        catch
        {
            // 新プロセス起動に失敗しても現プロセスは終了する
            Application.Current.Shutdown();
        }
    }
}
