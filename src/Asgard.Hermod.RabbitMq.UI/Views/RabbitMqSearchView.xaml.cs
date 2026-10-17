namespace Asgard.Hermod.RabbitMq.UI.Views;

/// <summary>按原稿呈现搜索图标与占位文案，Enter 才提交名称筛选。</summary>
public partial class RabbitMqSearchView : UserControl
{
    /// <summary>侧栏与列表使用各自完整的本地化搜索提示。</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(RabbitMqSearchView));
    /// <summary>搜索框未输入时的占位文案。</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    /// <summary>默认采用当前启动语言的名称筛选提示。</summary>
    public RabbitMqSearchView() { Placeholder = Text.Get("RabbitMqSearchPlaceholder"); InitializeComponent(); }
}
