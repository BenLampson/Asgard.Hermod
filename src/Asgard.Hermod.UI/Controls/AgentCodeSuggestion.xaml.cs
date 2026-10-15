namespace Asgard.Hermod.UI.Controls;

/// <summary>统一代码建议外观，插入、确认与执行完全委托给模块原有命令。</summary>
public partial class AgentCodeSuggestion : UserControl
{
    /// <summary>未经解释或翻译的代码原文。</summary>
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(string), typeof(AgentCodeSuggestion), new PropertyMetadata(""));
    /// <summary>模块提供的本地化建议标题。</summary>
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(AgentCodeSuggestion), new PropertyMetadata(""));
    /// <summary>插入按钮的完整本地化文本。</summary>
    public static readonly DependencyProperty InsertTextProperty = DependencyProperty.Register(nameof(InsertText), typeof(string), typeof(AgentCodeSuggestion), new PropertyMetadata(""));
    /// <summary>执行按钮的完整本地化文本。</summary>
    public static readonly DependencyProperty ExecuteTextProperty = DependencyProperty.Register(nameof(ExecuteText), typeof(string), typeof(AgentCodeSuggestion), new PropertyMetadata(""));
    /// <summary>模块原有的插入命令。</summary>
    public static readonly DependencyProperty InsertCommandProperty = DependencyProperty.Register(nameof(InsertCommand), typeof(ICommand), typeof(AgentCodeSuggestion));
    /// <summary>模块原有的执行与确认命令。</summary>
    public static readonly DependencyProperty ExecuteCommandProperty = DependencyProperty.Register(nameof(ExecuteCommand), typeof(ICommand), typeof(AgentCodeSuggestion));
    /// <summary>保留连接代次、数据库范围等业务证据的原始建议对象。</summary>
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(AgentCodeSuggestion));

    /// <summary>加载只读代码与动作区。</summary>
    public AgentCodeSuggestion() => InitializeComponent();
    /// <summary>代码原文。</summary>
    public string Code { get => (string)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    /// <summary>建议标题。</summary>
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    /// <summary>插入按钮文本。</summary>
    public string InsertText { get => (string)GetValue(InsertTextProperty); set => SetValue(InsertTextProperty, value); }
    /// <summary>执行按钮文本。</summary>
    public string ExecuteText { get => (string)GetValue(ExecuteTextProperty); set => SetValue(ExecuteTextProperty, value); }
    /// <summary>插入入口。</summary>
    public ICommand? InsertCommand { get => (ICommand?)GetValue(InsertCommandProperty); set => SetValue(InsertCommandProperty, value); }
    /// <summary>执行入口。</summary>
    public ICommand? ExecuteCommand { get => (ICommand?)GetValue(ExecuteCommandProperty); set => SetValue(ExecuteCommandProperty, value); }
    /// <summary>原始建议对象。</summary>
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
}
