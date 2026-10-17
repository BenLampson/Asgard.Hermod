namespace Asgard.Hermod.UI.Common;

/// <summary>为界面状态提供属性变更通知。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>通知绑定刷新指定属性。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>值确实变化时赋值并通知绑定。</summary>
    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>刷新计算属性或关联属性。</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
