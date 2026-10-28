namespace Asgard.Hermod.UI.Controls;

/// <summary>可选择、复制的只读 Markdown 正文；只创建原生文档元素，不加载外部内容或执行模型输出。</summary>
public sealed class MarkdownView : MarkdownDocumentReader
{
    /// <summary>模型或用户原文，变化时重建当前控件独占的文档。</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(MarkdownView), new PropertyMetadata("", OnSourceChanged));

    /// <summary>仅助手回复启用 Markdown；用户输入及命令输出保持原文。</summary>
    public static readonly DependencyProperty IsMarkdownEnabledProperty = DependencyProperty.Register(
        nameof(IsMarkdownEnabled), typeof(bool), typeof(MarkdownView), new PropertyMetadata(true, OnSourceChanged));

    /// <summary>使用共享主题中的原生内容模板，避免动态解析任何 XAML。</summary>
    static MarkdownView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MarkdownView), new FrameworkPropertyMetadata(typeof(MarkdownView)));

    /// <summary>移除编辑器边框与独立滚动区域，正文随会话整体滚动，保留原生选择和复制。</summary>
    public MarkdownView()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        // 仅允许程序创建的只读代码/表格视口接收选择和滚动；解析器不会创建链接、图片或模型指定的控件。
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        FocusVisualStyle = null;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SetResourceReference(ForegroundProperty, "TextBodyBrush");
        RefreshDocument();
    }

    /// <summary>绑定未经翻译或拼接的消息原文。</summary>
    public string Source { get => (string)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>通过稳定的消息来源控制格式，不能比较本地化角色标签。</summary>
    public bool IsMarkdownEnabled { get => (bool)GetValue(IsMarkdownEnabledProperty); set => SetValue(IsMarkdownEnabledProperty, value); }

    /// <summary>属性绑定或消息内容更新后同步重建文档。</summary>
    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((MarkdownView)sender).RefreshDocument();

    /// <summary>文档只属于当前视图；宽度随控件排版，避免共享文档父节点或固定页宽。</summary>
    private void RefreshDocument()
    {
        Document = MarkdownDocumentBuilder.Build(Source ?? "", IsMarkdownEnabled);
        // RichTextBox 接管新文档时会注入左右五 DIP 插入符留白，接管后再恢复正文起点。
        Document.PagePadding = new(0);
        if (IsMarkdownEnabled) MarkdownOverflowLayout.Wrap(Document.Blocks, this);
        Document.FontSize = FontSize;
        Document.FontFamily = FontFamily;
        Document.LineHeight = FontSize * (IsMarkdownEnabled ? 1.75 : 1.7);
    }
    /// <summary>正文随阅读组件字号变化，代码块自身的独立字号不受影响。</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == FontSizeProperty && Document is { } document) { document.FontSize = FontSize; document.LineHeight = FontSize * (IsMarkdownEnabled ? 1.75 : 1.7); }
        if (args.Property == FontFamilyProperty && Document is { } current) current.FontFamily = FontFamily;
    }
}
