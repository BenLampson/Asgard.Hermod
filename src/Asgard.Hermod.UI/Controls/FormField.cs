namespace Asgard.Hermod.UI.Controls;

/// <summary>统一表单的字段名称、必填提示、帮助文案和就地错误。</summary>
public class FormField : ContentControl
{
    /// <summary>字段名称。</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(FormField), new PropertyMetadata(""));
    /// <summary>字段帮助文案。</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(FormField), new PropertyMetadata("", OnHintChanged));
    /// <summary>字段错误文案。</summary>
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(
        nameof(Error), typeof(string), typeof(FormField), new PropertyMetadata("", OnErrorChanged));
    /// <summary>必填标记。</summary>
    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.Register(
        nameof(IsRequired), typeof(bool), typeof(FormField), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey HasHintPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasHint), typeof(bool), typeof(FormField), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        nameof(HasError), typeof(bool), typeof(FormField),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>帮助文案是否可见。</summary>
    public static readonly DependencyProperty HasHintProperty = HasHintPropertyKey.DependencyProperty;
    /// <summary>错误文案是否可见。</summary>
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    /// <summary>自动加载主题资源中的表单字段模板。</summary>
    static FormField() => DefaultStyleKeyProperty.OverrideMetadata(typeof(FormField),
        new FrameworkPropertyMetadata(typeof(FormField)));

    /// <summary>当前字段名称。</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    /// <summary>说明当前字段如何填写。</summary>
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    /// <summary>由业务校验提供的错误。</summary>
    public string Error { get => (string)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    /// <summary>是否显示必填星号。</summary>
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
    /// <summary>是否有非空帮助文案。</summary>
    public bool HasHint => (bool)GetValue(HasHintProperty);
    /// <summary>是否有非空错误文案。</summary>
    public bool HasError => (bool)GetValue(HasErrorProperty);

    /// <summary>输入组件继承所属字段的错误状态，无需重复绑定业务校验文案。</summary>
    public static bool GetHasError(DependencyObject element) => (bool)element.GetValue(HasErrorProperty);

    /// <summary>空白帮助文案不占用布局高度。</summary>
    private static void OnHintChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        target.SetValue(HasHintPropertyKey, !string.IsNullOrWhiteSpace(args.NewValue as string));

    /// <summary>统一错误状态，模板同时更新错误文案与输入边框。</summary>
    private static void OnErrorChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        target.SetValue(HasErrorPropertyKey, !string.IsNullOrWhiteSpace(args.NewValue as string));
}
