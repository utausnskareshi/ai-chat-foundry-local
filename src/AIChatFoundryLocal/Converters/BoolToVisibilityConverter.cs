using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIChatFoundryLocal.Converters;

/// <summary>
/// bool 値を <see cref="Visibility"/> に変換する WPF バリューコンバーター。
/// <para>
/// ConverterParameter に "Invert" を指定すると変換結果を反転できる。
/// </para>
/// <example>
/// 通常: True → Visible、False → Collapsed<br/>
/// 反転: True → Collapsed、False → Visible
/// </example>
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// bool 値を <see cref="Visibility"/> に変換する。
    /// </summary>
    /// <param name="value">変換元の bool 値。</param>
    /// <param name="targetType">変換先の型（未使用）。</param>
    /// <param name="parameter">"Invert" を指定すると結果を反転する。</param>
    /// <param name="culture">カルチャ情報（未使用）。</param>
    /// <returns><see cref="Visibility.Visible"/> または <see cref="Visibility.Collapsed"/>。</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool boolValue = value is bool b && b;
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>逆変換は未サポート。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
