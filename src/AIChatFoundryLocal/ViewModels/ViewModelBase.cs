using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChatFoundryLocal.ViewModels;

/// <summary>
/// すべての ViewModel の基底クラス。
/// CommunityToolkit.Mvvm の <see cref="ObservableObject"/> を継承し、
/// INotifyPropertyChanged を実装することで WPF バインディングに対応する。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
