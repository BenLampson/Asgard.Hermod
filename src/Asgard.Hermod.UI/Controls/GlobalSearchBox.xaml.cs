namespace Asgard.Hermod.UI.Controls;

/// <summary>
/// 搜索输入与结果弹出层；结果筛选和打开操作由 ViewModel 提供。
/// </summary>
public partial class GlobalSearchBox : UserControl
{
    /// <summary>
    /// 创建搜索组件。
    /// </summary>
    public GlobalSearchBox()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 供外壳的搜索入口转移键盘焦点。
    /// </summary>
    public void FocusSearch()
    {
        SearchInput.Focus();
        SearchInput.SelectAll();
    }

    /// <summary>
    /// 重新进入搜索框时显示已有查询的结果。
    /// </summary>
    private void OnSearchFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdatePopup();
    }

    /// <summary>
    /// 查询文本变化只影响弹出层可见性，不在控件中筛选数据。
    /// </summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePopup();
    }

    /// <summary>
    /// 非空查询且输入框有焦点时打开结果层。
    /// </summary>
    private void UpdatePopup()
    {
        if (ResultsPopup is not null && SearchInput is not null)
        {
            ResultsPopup.IsOpen = SearchInput.IsKeyboardFocusWithin
                && !string.IsNullOrWhiteSpace(SearchInput.Text);
        }
    }

    /// <summary>
    /// Escape 收起结果；Enter 的打开操作交给声明式命令绑定。
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            ResultsPopup.IsOpen = false;
            e.Handled = e.Key == Key.Escape;
        }
    }

    /// <summary>
    /// 点击结果后收起弹出层，保留按钮自身的命令执行。
    /// </summary>
    private void OnResultClick(object sender, RoutedEventArgs e)
    {
        ResultsPopup.IsOpen = false;
    }

    /// <summary>
    /// 控件离开窗口时关闭附属弹出层。
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ResultsPopup.IsOpen = false;
    }
}
