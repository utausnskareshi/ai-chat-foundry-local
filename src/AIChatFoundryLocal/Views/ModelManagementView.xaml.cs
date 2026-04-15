using System.Windows.Controls;

namespace AIChatFoundryLocal.Views;

/// <summary>
/// モデル管理画面の View（コードビハインド）。
/// <para>
/// すべての操作ロジックは <see cref="ViewModels.ModelManagementViewModel"/> に委譲しており、
/// このコードビハインドは XAML コンポーネントの初期化のみを行う。
/// </para>
/// </summary>
public partial class ModelManagementView : UserControl
{
    /// <summary>
    /// コンストラクター。XAML コンポーネントを初期化する。
    /// </summary>
    public ModelManagementView()
    {
        InitializeComponent();
    }
}
