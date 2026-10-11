namespace Asgard.Hermod.UI.Controls;

/// <summary>封装可绑定的密码输入，所有业务窗体统一使用遮蔽编辑器。</summary>
[TemplatePart(Name = EditorPart, Type = typeof(PasswordBox))]
public class SecretInput : Control
{
    private const string EditorPart = "PART_Editor";
    private PasswordBox? _editor;
    private bool _synchronizing;

    /// <summary>仅供连接和保存逻辑使用的内存值，模板不会呈现明文。</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(SecretInput),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    /// <summary>服务商可限定凭据长度，零代表不限制。</summary>
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength), typeof(int), typeof(SecretInput), new PropertyMetadata(0),
        value => value is int length && length >= 0);

    /// <summary>统一密码控件的模板键与主题响应。</summary>
    static SecretInput() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SecretInput),
        new FrameworkPropertyMetadata(typeof(SecretInput)));

    /// <summary>获取或设置遮蔽输入的值；禁止将此属性写入日志。</summary>
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>共享密码编辑器的最大长度。</summary>
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }

    /// <summary>重新应用模板时释放旧编辑器，避免重复事件和旧控件引用。</summary>
    public override void OnApplyTemplate()
    {
        if (_editor is not null)
            _editor.PasswordChanged -= OnPasswordChanged;
        base.OnApplyTemplate();
        _editor = GetTemplateChild(EditorPart) as PasswordBox;
        if (_editor is null)
            return;
        _editor.PasswordChanged += OnPasswordChanged;
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

    /// <summary>仅在值有差异时赋值，保持编辑器光标与事件顺序。</summary>
    private void SynchronizeEditor()
    {
        if (_editor is null || _editor.Password == Value)
            return;
        _synchronizing = true;
        try { _editor.Password = Value ?? ""; }
        finally { _synchronizing = false; }
    }
}
