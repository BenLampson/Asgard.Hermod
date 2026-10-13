namespace Asgard.Hermod.UI.Controls;

/// <summary>封装可绑定的密码输入，默认遮蔽并允许用户主动切换明文显示。</summary>
[TemplatePart(Name = EditorPart, Type = typeof(PasswordBox))]
[TemplatePart(Name = PlainEditorPart, Type = typeof(TextBox))]
[TemplatePart(Name = RevealButtonPart, Type = typeof(Button))]
public class SecretInput : Control
{
    private const string EditorPart = "PART_Editor";
    private const string PlainEditorPart = "PART_PlainEditor";
    private const string RevealButtonPart = "PART_RevealButton";
    private PasswordBox? _editor;
    private TextBox? _plainEditor;
    private Button? _revealButton;
    private bool _synchronizing;

    /// <summary>供连接和保存逻辑使用的内存值，仅用户主动显示时呈现明文。</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(SecretInput),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    /// <summary>服务商可限定凭据长度，零代表不限制。</summary>
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength), typeof(int), typeof(SecretInput), new PropertyMetadata(0),
        value => value is int length && length >= 0);

    private static readonly DependencyPropertyKey IsPasswordVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsPasswordVisible), typeof(bool), typeof(SecretInput), new PropertyMetadata(false));

    /// <summary>当前是否由用户主动开启明文显示，不保存到任何配置。</summary>
    public static readonly DependencyProperty IsPasswordVisibleProperty = IsPasswordVisiblePropertyKey.DependencyProperty;

    /// <summary>统一密码控件的模板键与主题响应。</summary>
    static SecretInput() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SecretInput),
        new FrameworkPropertyMetadata(typeof(SecretInput)));

    /// <summary>获取或设置遮蔽输入的值；禁止将此属性写入日志。</summary>
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>共享密码编辑器的最大长度。</summary>
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }

    /// <summary>读取当前显示状态；关闭窗口或重新应用模板后恢复遮蔽。</summary>
    public bool IsPasswordVisible => (bool)GetValue(IsPasswordVisibleProperty);

    /// <summary>控件离开视图时清除明文编辑器，重新显示仍默认遮蔽。</summary>
    public SecretInput()
    {
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>重新应用模板时释放旧编辑器，避免重复事件和旧控件引用。</summary>
    public override void OnApplyTemplate()
    {
        if (_editor is not null)
            _editor.PasswordChanged -= OnPasswordChanged;
        if (_plainEditor is not null)
        {
            _plainEditor.TextChanged -= OnTextChanged;
            _plainEditor.Clear();
        }
        if (_revealButton is not null)
            _revealButton.Click -= OnRevealClick;
        SetValue(IsPasswordVisiblePropertyKey, false);
        base.OnApplyTemplate();
        _editor = GetTemplateChild(EditorPart) as PasswordBox;
        _plainEditor = GetTemplateChild(PlainEditorPart) as TextBox;
        _revealButton = GetTemplateChild(RevealButtonPart) as Button;
        if (_editor is not null)
            _editor.PasswordChanged += OnPasswordChanged;
        if (_plainEditor is not null)
            _plainEditor.TextChanged += OnTextChanged;
        if (_revealButton is not null)
            _revealButton.Click += OnRevealClick;
        SynchronizeEditor();
    }

    /// <summary>业务值变化后同步遮蔽编辑器。</summary>
    private static void OnValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        ((SecretInput)target).SynchronizeEditor();

    /// <summary>使用 SetCurrentValue 保留 TwoWay 绑定，不替换绑定表达式。</summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (!_synchronizing && _editor is not null)
            SetCurrentValue(ValueProperty, _editor.Password);
    }

    /// <summary>明文编辑同样保留原有 TwoWay 绑定，不依赖业务窗口同步。</summary>
    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (!_synchronizing && IsPasswordVisible && _plainEditor is not null)
            SetCurrentValue(ValueProperty, _plainEditor.Text);
    }

    /// <summary>切换时保留密码，隐藏后立即清空明文副本。</summary>
    private void OnRevealClick(object sender, RoutedEventArgs args)
    {
        SetValue(IsPasswordVisiblePropertyKey, !IsPasswordVisible);
        SynchronizeEditor();
        if (IsPasswordVisible && _plainEditor is not null)
            _plainEditor.CaretIndex = _plainEditor.Text.Length;
    }

    /// <summary>卸载不改变业务值，只恢复遮蔽状态并清空明文。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        SetValue(IsPasswordVisiblePropertyKey, false);
        SynchronizeEditor();
    }

    /// <summary>窗口隐藏或关闭时立即清除明文，不等待 WPF 延后的卸载事件。</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is false)
        {
            SetValue(IsPasswordVisiblePropertyKey, false);
            SynchronizeEditor();
        }
    }

    /// <summary>仅在值有差异时赋值，保持编辑器光标与事件顺序。</summary>
    private void SynchronizeEditor()
    {
        _synchronizing = true;
        try
        {
            string value = Value ?? "";
            if (_editor is not null && _editor.Password != value)
                _editor.Password = value;
            string plainValue = IsPasswordVisible ? value : "";
            if (_plainEditor is not null && _plainEditor.Text != plainValue)
                _plainEditor.Text = plainValue;
            // 共享模板先于窗口创建并缓存，提示必须按当前实例的界面语言解析。
            if (_revealButton is not null)
            {
                string action = Text.Get(IsPasswordVisible ? "HidePassword" : "ShowPassword");
                _revealButton.ToolTip = action;
                System.Windows.Automation.AutomationProperties.SetName(_revealButton, action);
            }
        }
        finally { _synchronizing = false; }
    }
}
