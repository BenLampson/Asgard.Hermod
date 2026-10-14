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
                text.Focus(); text.SelectAll(); await WindowTestVisuals.UpdateLayoutAsync(window); VerifyRenderedSelection(text);
                var toggle = Assert.IsType<Button>(secret.Template.FindName("PART_RevealButton", secret));
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(secret.IsPasswordVisible);
                plain.SelectAll();
                plain.Focus(); await WindowTestVisuals.UpdateLayoutAsync(window); VerifyRenderedSelection(plain);
                Assert.Equal(secret.Value, plain.SelectedText);
                VerifyContrast(plain.SelectionBrush, plain.SelectionTextBrush, plain.SelectionOpacity);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(secret.IsPasswordVisible);
                masked.SelectAll();
                masked.Focus(); await WindowTestVisuals.UpdateLayoutAsync(window); VerifyRenderedSelection(masked);
                Assert.Empty(plain.Text);
                VerifyContrast(masked.SelectionBrush, masked.SelectionTextBrush, masked.SelectionOpacity);
            }
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>检查实际渲染的蓝色选区内存在白色字形，防止仅画刷正确但文字仍被遮盖。</summary>
    private static void VerifyRenderedSelection(FrameworkElement input)
    {
        int width = (int)Math.Ceiling(input.ActualWidth), height = (int)Math.Ceiling(input.ActualHeight);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(input);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        var color = Assert.IsType<SolidColorBrush>(Application.Current.FindResource("ActionFillBrush")).Color;
        var selected = new List<(int X, int Y)>(); int verticalMargin = Math.Min(8, height / 4);
        for (int y = verticalMargin; y < height - verticalMargin; y++)
            for (int x = 8; x < width - 8; x++)
            {
                int index = (y * width + x) * 4;
                if (Math.Abs(pixels[index] - color.B) < 4 && Math.Abs(pixels[index + 1] - color.G) < 4 && Math.Abs(pixels[index + 2] - color.R) < 4) selected.Add((x, y));
            }
        Assert.NotEmpty(selected); int white = 0, left = selected.Min(point => point.X), right = selected.Max(point => point.X),
            top = selected.Min(point => point.Y), bottom = selected.Max(point => point.Y);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int index = (y * width + x) * 4;
                if (pixels[index] > 225 && pixels[index + 1] > 225 && pixels[index + 2] > 225) white++;
            }
        Assert.True(white >= 5, "Selected glyphs must remain visible above the opaque selection background.");
    }

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
