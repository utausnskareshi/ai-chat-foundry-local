using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AIChatFoundryLocal.Models;

namespace AIChatFoundryLocal.Converters;

/// <summary>
/// <see cref="ChatRole"/> を水平配置（<see cref="HorizontalAlignment"/>）に変換するコンバーター。
/// ユーザーメッセージを右寄せ、AI メッセージを左寄せにするために使用する。
/// </summary>
public class ChatRoleToAlignmentConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ChatRole role && role == ChatRole.User
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

    /// <summary>逆変換は未サポート。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// <see cref="ChatRole"/> をメッセージバルーンの背景色に変換するコンバーター。
/// ユーザー: 青系（#0078D4）、AI: ダークグレー（#3B3B3B）。
/// </summary>
public class ChatRoleToBackgroundConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ChatRole role && role == ChatRole.User
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))   // ユーザー: Windows アクセントブルー
            : new SolidColorBrush(Color.FromRgb(0x3B, 0x3B, 0x3B));  // AI: ダークグレー

    /// <summary>逆変換は未サポート。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// <see cref="ChatRole"/> をメッセージテキストの前景色に変換するコンバーター。
/// ユーザー・AI ともに白色を返す。
/// </summary>
public class ChatRoleToForegroundConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush(Colors.White);

    /// <summary>逆変換は未サポート。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// <see cref="ChatRole"/> をメッセージの送信者ラベル文字列に変換するコンバーター。
/// ユーザー → "You"、AI → "AI"、その他 → 空文字。
/// </summary>
public class ChatRoleToLabelConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ChatRole role
            ? role switch
            {
                ChatRole.User      => "You",
                ChatRole.Assistant => "AI",
                _                  => ""
            }
            : "";

    /// <summary>逆変換は未サポート。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
