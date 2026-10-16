namespace Asgard.Hermod.UI.Controls;

/// <summary>纯文本编辑和只读代码预览共用行号与滚动，格式解释仍由调用模块负责。</summary>
public partial class CodeEditor : UserControl
{
    /// <summary>日志阅读使用满幅表面、二十三 DIP 行高，仍保留原生连续选区。</summary>
    public static readonly DependencyProperty IsLogReaderProperty = DependencyProperty.Register(nameof(IsLogReader), typeof(bool), typeof(CodeEditor), new PropertyMetadata(false, (target, _) => ((CodeEditor)target).ApplyLogLayout()));
    /// <summary>独立日志阅读器排版。</summary>
    public bool IsLogReader { get => (bool)GetValue(IsLogReaderProperty); set => SetValue(IsLogReaderProperty, value); }
    /// <summary>只读原生文本，用于本地选区、搜索和字体调整。</summary>
    public TextBox TextInputControl => Input;
    private void ApplyLogLayout()
    {
        if (EditorFrame is null) return;
        LogPaint.Visibility = IsLogReader ? Visibility.Visible : Visibility.Collapsed; Input.AcceptsTab = !IsLogReader;
        LineNumbers.Visibility = IsLogReader ? Visibility.Hidden : Visibility.Visible;
        if (!IsLogReader) { ApplySqlLayout(); return; }
        EditorFrame.CornerRadius = new(0); EditorFrame.BorderThickness = new(0);
        EditorFrame.SetResourceReference(Border.BackgroundProperty, "LogBackgroundBrush");
        Input.Foreground = Brushes.Transparent; LineNumbers.SetResourceReference(TextBlock.ForegroundProperty, "LogGutterBrush");
        GutterColumn.Width = new(68); Input.Margin = new(-2, 0, 0, 0); Input.Padding = new(0, 14, 16, 14);
        if (LineNumbers.Parent is Border gutter) gutter.Padding = new(0, 14, 16, 0);
        LineNumbers.FontSize = 11; LineNumbers.LineHeight = 23; LineNumbers.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        TextBlock.SetLineHeight(Input, 23); TextBlock.SetLineStackingStrategy(Input, LineStackingStrategy.BlockLineHeight);
    }
    /// <summary>SQL 工作区使用无圆角满幅代码表面与二十四 DIP 行高。</summary>
    public static readonly DependencyProperty IsSqlWorkspaceProperty = DependencyProperty.Register(nameof(IsSqlWorkspace), typeof(bool), typeof(CodeEditor), new PropertyMetadata(false, (target, _) => ((CodeEditor)target).ApplySqlLayout()));
    /// <summary>是否采用 SQL 母组件的代码排版。</summary>
    public bool IsSqlWorkspace { get => (bool)GetValue(IsSqlWorkspaceProperty); set => SetValue(IsSqlWorkspaceProperty, value); }
    private void ApplySqlLayout()
    {
        if (EditorFrame is null) return;
        EditorFrame.CornerRadius = new(IsSqlWorkspace ? 0 : 6);
        EditorFrame.BorderThickness = new(IsSqlWorkspace ? 0 : 1);
        EditorFrame.SetResourceReference(Border.BackgroundProperty, IsSqlWorkspace ? "DatabaseEditorBrush" : "AppBackgroundBrush");
        GutterColumn.Width = new(IsSqlWorkspace ? 56 : 48);
        // 原生 TextBoxView 有两 DIP 的插入符内边距，抵消后正文落在设计的五十六 DIP 起点。
        Input.Margin = new(IsSqlWorkspace ? -2 : 0, 0, 0, 0);
        if (LineNumbers.Parent is Border gutter) gutter.Padding = new(0, 16, IsSqlWorkspace ? 14 : 12, 0);
        LineNumbers.FontSize = IsSqlWorkspace ? 11 : 13;
        LineNumbers.LineHeight = IsSqlWorkspace ? 24 : double.NaN;
        LineNumbers.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        TextBlock.SetLineHeight(Input, IsSqlWorkspace ? 24 : double.NaN);
        TextBlock.SetLineStackingStrategy(Input, LineStackingStrategy.BlockLineHeight);
        Input.SetResourceReference(ForegroundProperty, IsSqlWorkspace ? "AccentBrush" : "TextBodyBrush");
    }
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
    /// <summary>完整文稿中的选区起点，用于发送范围与错误位置映射。</summary>
    public int SelectionStart => Input.SelectionStart;
    /// <summary>显式定位只移动选择与滚动，不插入代码或执行；纯离屏挂载不请求键盘焦点。</summary>
    public void SelectCharacter(int index)
    {
        if (index < 0 || index > Input.Text.Length) return;
        Input.Select(index, index < Input.Text.Length ? char.IsHighSurrogate(Input.Text[index]) && index + 1 < Input.Text.Length && char.IsLowSurrogate(Input.Text[index + 1]) ? 2 : 1 : 0);
        RevealCharacter(index);
        if (Window.GetWindow(this) is not null) Input.Focus();
    }
    /// <summary>光标字符位置由调用方定位当前语句。</summary>
    public int CaretIndex => Input.CaretIndex;
    /// <summary>按真实字符矩形定位日志命中，自定义行高不依赖默认字体的行号估算。</summary>
    public void RevealCharacter(int index)
    {
        if (index < 0 || index >= Input.Text.Length) return;
        var rect = Input.GetRectFromCharacterIndex(index);
        if (rect.IsEmpty) return;
        if (rect.Top < Input.Padding.Top || rect.Bottom > Input.ActualHeight - Input.Padding.Bottom)
            Input.ScrollToVerticalOffset(Math.Max(0, Input.VerticalOffset + rect.Top - Input.Padding.Top));
        if (rect.Left < 0 || rect.Right > Input.ViewportWidth)
            Input.ScrollToHorizontalOffset(Math.Max(0, Input.HorizontalOffset + rect.Left));
    }
    /// <summary>公共输入控件保持原生选择和键盘焦点。</summary>
    public void FocusInput() => Input.Focus();
    /// <summary>替换当前选区或插入光标处，沿用原生撤销历史；不执行插入的代码。</summary>
    public void InsertText(string text)
    {
        if (IsReadOnly) return;
        CloseCompletion(); _applyingCompletion = true;
        try { int start = Input.SelectionStart; Input.SelectedText = text; Input.CaretIndex = start + text.Length; Input.Focus(); }
        finally { _applyingCompletion = false; }
    }
    /// <summary>只渲染可视行的行号，避免大值创建完整行号集合。</summary>
    public CodeEditor() { InitializeComponent(); LogPaint.Attach(Input, LineNumbers); Loaded += (_, _) => UpdateNumbers(); Unloaded += (_, _) => CloseCompletion(); Input.LostKeyboardFocus += (_, _) => CloseCompletion(); Input.PreviewMouseLeftButtonDown += (_, _) => CloseCompletion(); Input.LayoutUpdated += (_, _) => { if (IsSqlWorkspace) UpdateNumbers(); }; }
    private void OnTextChanged(object sender, TextChangedEventArgs args) { UpdateNumbers(); OnCompletionTextChanged(); }
    private void OnScrollChanged(object sender, ScrollChangedEventArgs args) => UpdateNumbers();
    private void OnInputSizeChanged(object sender, SizeChangedEventArgs args) => UpdateNumbers();
    private string? _numberText;
    private int[] _numberStarts = [0];
    private object? _numberLayout;
    private void UpdateNumbers()
    {
        if (Input is null || LineNumbers is null || !Input.IsMeasureValid) return;
        if (IsLogReader) { LineNumbers.Text = ""; return; }
        if (IsSqlWorkspace)
        {
            if (!Input.IsArrangeValid) return;
            object layout = (Input.Text, Input.VerticalOffset, Input.HorizontalOffset, Input.ViewportHeight, Input.ViewportWidth, Input.TextWrapping, Input.FontSize);
            if (Equals(_numberLayout, layout)) return;
            _numberLayout = layout;
            if (!ReferenceEquals(_numberText, Input.Text)) { _numberText = Input.Text; _numberStarts = LogTextLayout.LineStarts(Input.Text); }
            var rows = LogTextLayout.VisibleLines(Input, _numberStarts).ToArray();
            LineNumbers.Text = Input.Text.Length == 0 ? "1" : string.Join(Environment.NewLine, rows.Select(row => row.Start == row.PhysicalStart || Input.TextWrapping == TextWrapping.NoWrap
                ? row.PhysicalNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""));
            LineNumbers.RenderTransform = new TranslateTransform(0, rows.Length == 0 ? 0 : rows[0].Bounds.Top - Input.GetRectFromCharacterIndex(0).Top - Input.VerticalOffset);
            return;
        }
        int first = Input.GetFirstVisibleLineIndex(), last = Input.GetLastVisibleLineIndex();
        if (first < 0 || last < first) { LineNumbers.Text = ""; return; }
        // 文本替换事件先于滚动视口更新；可视行索引可能仍来自旧缓冲。
        int count = Input.LineCount;
        if (count < 1) { LineNumbers.Text = ""; return; }
        first = Math.Min(first, count - 1); last = Math.Min(last, count - 1);
        LineNumbers.Text = string.Join(Environment.NewLine, Enumerable.Range(first + 1, Math.Min(200, last - first + 1)).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var rect = Input.GetRectFromCharacterIndex(Input.GetCharacterIndexFromLineIndex(first));
        LineNumbers.RenderTransform = new TranslateTransform(0, IsSqlWorkspace ? first * 24 - Input.VerticalOffset : rect.IsEmpty ? 0 : rect.Y - Input.Padding.Top);
    }
}
