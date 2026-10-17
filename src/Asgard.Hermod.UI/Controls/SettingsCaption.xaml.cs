namespace Asgard.Hermod.UI.Controls;

/// <summary>设置与模型编辑器的六十像素标题，支持不创建窗口的离屏校验。</summary>
public partial class SettingsCaption : UserControl
{
    /// <summary>本地化标题。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingsCaption), new PropertyMetadata(""));
    /// <summary>编辑器使用十八像素标题并隐藏设置图标。</summary>
    public static readonly DependencyProperty IsEditorProperty = DependencyProperty.Register(nameof(IsEditor), typeof(bool), typeof(SettingsCaption));
    /// <summary>标题。</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    /// <summary>编辑器变体。</summary>
    public bool IsEditor { get => (bool)GetValue(IsEditorProperty); set => SetValue(IsEditorProperty, value); }
    /// <summary>只加载控件，不创建原生窗口。</summary>
    public SettingsCaption() => InitializeComponent();
    private void OnClose(object sender, RoutedEventArgs args) => Window.GetWindow(this)?.Close();
}
