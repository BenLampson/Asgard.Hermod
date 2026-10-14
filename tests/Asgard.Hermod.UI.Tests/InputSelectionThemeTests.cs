namespace Asgard.Hermod.UI.Tests;

/// <summary>验证实际输入模板在换肤与密码显示切换后仍保持可读的选区。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class InputSelectionThemeTests(WpfTestFixture fixture)
{
    /// <summary>复用同一组控件切换两种主题，覆盖普通、下拉及密码的两种编辑器。</summary>
    [Fact]
    public Task Selection_RemainsReadableAcrossThemesAndPasswordModes() => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        var text = new TextBox { Text = "selection sample" };
        var secret = new SecretInput { Value = "selection sample" };
        var combo = new ComboBox { IsEditable = true, Text = "selection sample" };
        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(secret);
        panel.Children.Add(combo);
        var window = new HermodWindow { Content = panel, Width = 520, Height = 260 };
        try
        {
            WindowPreviewCapture.Show(window);
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark })
            {
                themes.SetTheme(theme);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                var plain = Assert.IsType<TextBox>(secret.Template.FindName("PART_PlainEditor", secret));
                var masked = Assert.IsType<PasswordBox>(secret.Template.FindName("PART_Editor", secret));
                var editable = Assert.IsType<TextBox>(combo.Template.FindName("PART_EditableTextBox", combo));
                foreach (var input in new[] { text, plain, editable })
                {
                    VerifyContrast(input.SelectionBrush, input.SelectionTextBrush, input.SelectionOpacity);
                }
                VerifyContrast(masked.SelectionBrush, masked.SelectionTextBrush, masked.SelectionOpacity);
                var toggle = Assert.IsType<Button>(secret.Template.FindName("PART_RevealButton", secret));
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(secret.IsPasswordVisible);
                plain.SelectAll();
                Assert.Equal(secret.Value, plain.SelectedText);
                VerifyContrast(plain.SelectionBrush, plain.SelectionTextBrush, plain.SelectionOpacity);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(secret.IsPasswordVisible);
                masked.SelectAll();
                Assert.Empty(plain.Text);
                VerifyContrast(masked.SelectionBrush, masked.SelectionTextBrush, masked.SelectionOpacity);
            }
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>选区使用不透明主题色，前景与背景至少达到普通文本的4.5倍亮度对比。</summary>
    private static void VerifyContrast(Brush background, Brush foreground, double opacity)
    {
        var back = Assert.IsType<SolidColorBrush>(background);
        var front = Assert.IsType<SolidColorBrush>(foreground);
        Assert.Equal(1d, opacity);
        Assert.Equal(1d, back.Opacity);
        Assert.Equal(255, back.Color.A);
        Assert.Equal(Assert.IsType<SolidColorBrush>(Application.Current.FindResource("ActionFillBrush")).Color, back.Color);
        Assert.Equal(Assert.IsType<SolidColorBrush>(Application.Current.FindResource("InverseTextBrush")).Color, front.Color);
        double first = Luminance(back.Color), second = Luminance(front.Color);
        Assert.True((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05) >= 4.5);
    }

    /// <summary>将sRGB颜色转换为相对亮度，用于检查实际主题画刷。</summary>
    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
