namespace Asgard.Hermod.UI.Tests;

/// <summary>验证共享表单组件的真实模板、遮蔽输入和错误提示。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class FormControlTests(WpfTestFixture fixture)
{
    /// <summary>密码编辑更新原有绑定，主题改变不泄漏或丢失内存值。</summary>
    [Fact]
    public Task SecretInput_UsesMaskedEditorAndPreservesTwoWayBinding() => fixture.RunAsync(async () =>
    {
        var source = new TextBox { Text = "initial-secret" };
        var secret = new SecretInput();
        BindingOperations.SetBinding(secret, SecretInput.ValueProperty, new Binding(nameof(TextBox.Text)) { Source = source });
        var window = CreateWindow(secret);
        var themes = new ThemeService(Application.Current.Resources);
        try
        {
            window.Show();
            await WpfTestFixture.DrainDispatcherAsync();
            var editor = Assert.Single(WindowTestVisuals.Descendants<PasswordBox>(secret));
            Assert.Equal("initial-secret", editor.Password);
            Assert.Equal('●', editor.PasswordChar);
            editor.Password = "changed-secret";
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal("changed-secret", source.Text);
            Assert.True(BindingOperations.IsDataBound(secret, SecretInput.ValueProperty));
            themes.SetTheme(UiTheme.Dark);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal("changed-secret", editor.Password);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(secret), text => text.Text.Contains("secret"));
        }
        finally { themes.SetTheme(UiTheme.Light); window.Close(); }
    });

    /// <summary>就地错误替代帮助文字，所有表单控件统一使用主题错误颜色。</summary>
    [Fact]
    public Task FormField_ErrorReplacesHintAndUpdatesInputBorder() => fixture.RunAsync(async () =>
    {
        var input = new TextBox { Height = 36 };
        var field = new FormField { Label = "名称", Hint = "名称在同一父级下唯一。", Content = input, IsRequired = true };
        var window = CreateWindow(field);
        try
        {
            window.Show();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(field.HasHint);
            Assert.False(field.HasError);
            field.Error = "请输入名称。";
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(field.HasError);
            var error = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(field), text => text.Text == field.Error);
            Assert.Equal(Visibility.Visible, error.Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.Single(WindowTestVisuals.Descendants<TextBlock>(field), text => text.Text == field.Hint).Visibility);
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color,
                Assert.IsType<SolidColorBrush>(input.BorderBrush).Color);
            Assert.True(FormField.GetHasError(input));
            var errorIcon = Assert.IsType<IconView>(input.Template.FindName("ErrorIcon", input));
            Assert.Equal("circle-alert", errorIcon.Kind);
            Assert.Equal(Visibility.Visible, errorIcon.Visibility);
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color,
                Assert.IsType<SolidColorBrush>(errorIcon.Foreground).Color);
            input.Focus();
            await WpfTestFixture.DrainDispatcherAsync();
            var chrome = Assert.IsType<Border>(input.Template.FindName("Chrome", input));
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color,
                Assert.IsType<SolidColorBrush>(chrome.BorderBrush).Color);
            field.Error = " ";
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(field.HasError);
            Assert.False(FormField.GetHasError(input));
            Assert.Equal(Visibility.Collapsed, errorIcon.Visibility);
        }
        finally { window.Close(); }
    });

    /// <summary>内容边距只应用一次，普通、只读和多行输入使用相同的正文起点。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task TextBox_PaddingIsAppliedOnce(bool multiline, bool readOnly) => fixture.RunAsync(async () =>
    {
        var input = new TextBox { Text = "instance-001", AcceptsReturn = multiline, IsReadOnly = readOnly };
        var window = CreateWindow(input);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            double expected = input.BorderThickness.Left + input.Padding.Left;
            double before = input.GetRectFromCharacterIndex(0).Left;
            // 原生文字视图保留约 2px 的插入符留白；外层模板不能再次叠加整份 Padding。
            Assert.InRange(before, expected, expected + 3);
            input.Padding = new Thickness(14, 6, 14, 6);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.InRange(input.GetRectFromCharacterIndex(0).Left - before, 3.5, 4.5);
        }
        finally { window.Close(); }
    });

    /// <summary>字符串按钮文字继承按钮前景，主操作在浅色和深色背景上均使用反色文字。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task ActionButton_StringContentInheritsForeground(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var button = new Button { Content = "保存更改", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var window = CreateWindow(button);
        try
        {
            window.Show();
            await WpfTestFixture.DrainDispatcherAsync();
            var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(button));
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("InverseTextBrush")).Color,
                Assert.IsType<SolidColorBrush>(label.Foreground).Color);
            Assert.Equal(button.FontFamily, label.FontFamily);
            Assert.Equal(12, label.FontSize);
            Assert.Equal(FontWeights.SemiBold, label.FontWeight);
        }
        finally { themes.SetTheme(UiTheme.Light); window.Close(); }
    });

    /// <summary>模板检查在真实隐藏窗口中进行，不占用桌面焦点。</summary>
    private static Window CreateWindow(FrameworkElement content) => new()
    {
        Content = content,
        Width = 360,
        Height = 180,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
    };
}
