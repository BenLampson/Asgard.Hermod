namespace Asgard.Hermod.UI.ViewModels;

/// <summary>常用颜色按钮的预览与选中标记，支持输入任意合法 HEX。</summary>
public sealed class TagColorSwatchViewModel(string color) : ObservableObject
{
    private bool _isSelected;

    /// <summary>规范的六位 HEX 颜色。</summary>
    public string Color { get; } = color;
    /// <summary>当前有效颜色是否与此快捷颜色相同。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}
