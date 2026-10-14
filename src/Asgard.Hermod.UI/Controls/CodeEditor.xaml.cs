namespace Asgard.Hermod.UI.Controls;

/// <summary>纯文本编辑和只读代码预览共用行号与滚动，格式解释仍由调用模块负责。</summary>
public partial class CodeEditor : UserControl
{
    /// <summary>文本缓冲双向绑定，不自动格式化或保存。</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(CodeEditor), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>只读状态保留选择、滚动和复制。</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(CodeEditor), new PropertyMetadata(true));
    /// <summary>最大文本长度由模块的字节策略决定。</summary>
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(CodeEditor), new PropertyMetadata(0));
    /// <summary>原始编辑文本。</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    /// <summary>是否只读。</summary>
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    /// <summary>允许的文本长度，零为不限制。</summary>
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    /// <summary>提供 SQL 等调用方的选区范围，不解释编辑器文本。</summary>
    public string SelectedText => Input.SelectedText;
    /// <summary>光标字符位置由调用方定位当前语句。</summary>
    public int CaretIndex => Input.CaretIndex;
    /// <summary>公共输入控件保持原生选择和键盘焦点。</summary>
    public void FocusInput() => Input.Focus();
    /// <summary>只渲染可视行的行号，避免大值创建完整行号集合。</summary>
    public CodeEditor() { InitializeComponent(); Loaded += (_, _) => UpdateNumbers(); }
    private void OnTextChanged(object sender, TextChangedEventArgs args) => UpdateNumbers();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs args) => UpdateNumbers();
    private void OnInputSizeChanged(object sender, SizeChangedEventArgs args) => UpdateNumbers();
    private void UpdateNumbers()
    {
        if (Input is null || LineNumbers is null || !Input.IsMeasureValid) return;
        int first = Input.GetFirstVisibleLineIndex(), last = Input.GetLastVisibleLineIndex();
        if (first < 0 || last < first) { LineNumbers.Text = ""; return; }
        // 文本替换事件先于滚动视口更新；可视行索引可能仍来自旧缓冲。
        int count = Input.LineCount;
        if (count < 1) { LineNumbers.Text = ""; return; }
        first = Math.Min(first, count - 1); last = Math.Min(last, count - 1);
        LineNumbers.Text = string.Join(Environment.NewLine, Enumerable.Range(first + 1, Math.Min(200, last - first + 1)).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var rect = Input.GetRectFromCharacterIndex(Input.GetCharacterIndexFromLineIndex(first));
        LineNumbers.RenderTransform = new TranslateTransform(0, rect.IsEmpty ? 0 : rect.Y - Input.Padding.Top);
    }
}
