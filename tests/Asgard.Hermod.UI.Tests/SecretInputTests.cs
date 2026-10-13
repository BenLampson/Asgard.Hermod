using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>共享密码组件在显示切换、重新套用模板及卸载时保持绑定和安全默认值。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SecretInputTests(WpfTestFixture fixture)
{
    /// <summary>两种编辑器更新同一绑定，隐藏、换模板及卸载均清除明文副本。</summary>
    [Fact]
    public Task Reveal_PreservesBindingAndResetsWhenLeavingView() => fixture.RunAsync(async () =>
    {
        var source = new TextBox { Text = "initial-test-password" };
        var input = new SecretInput { MaxLength = 128 };
        BindingOperations.SetBinding(input, SecretInput.ValueProperty, new Binding(nameof(TextBox.Text)) { Source = source });
        var window = new HermodWindow { Content = input, Width = 360, Height = 180 };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var masked = Assert.IsType<PasswordBox>(input.Template.FindName("PART_Editor", input));
            var plain = Assert.IsType<TextBox>(input.Template.FindName("PART_PlainEditor", input));
            var toggle = Assert.IsType<Button>(input.Template.FindName("PART_RevealButton", input));
            Assert.False(input.IsPasswordVisible);
            Assert.Empty(plain.Text);
            Assert.False(plain.IsUndoEnabled);
            masked.Password = "masked-test-password";
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(input.IsPasswordVisible);
            Assert.Equal("masked-test-password", plain.Text);
            Assert.Equal(128, plain.MaxLength);
            Assert.Equal(Visibility.Collapsed, masked.Visibility);
            Assert.Equal(Visibility.Visible, plain.Visibility);
            plain.Text = "edited-test-password";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal("edited-test-password", source.Text);
            Assert.Equal(source.Text, masked.Password);
            source.Text = "external-test-password";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(source.Text, plain.Text);
            Assert.True(BindingOperations.IsDataBound(input, SecretInput.ValueProperty));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(input.IsPasswordVisible);
            Assert.Empty(plain.Text);
            Assert.Equal(source.Text, masked.Password);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            input.IsEnabled = false;
            Assert.False(toggle.IsEnabled);
            Assert.False(plain.IsEnabled);
            input.IsEnabled = true;
            var template = input.Template;
            input.Template = null;
            input.Template = template;
            input.ApplyTemplate();
            Assert.False(input.IsPasswordVisible);
            Assert.Empty(plain.Text);
            toggle = Assert.IsType<Button>(input.Template.FindName("PART_RevealButton", input));
            plain = Assert.IsType<TextBox>(input.Template.FindName("PART_PlainEditor", input));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(source.Text, plain.Text);
            source.Text = "";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Empty(input.Value);
            Assert.Empty(plain.Text);
            source.Text = "unload-test-password";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            window.Close();
            Assert.False(input.IsPasswordVisible);
            Assert.Empty(plain.Text);
            Assert.Equal(source.Text, input.Value);
        }
        finally { window.Close(); }
    });

    /// <summary>空格键执行实际按钮切换，用户仍可通过 Tab 进入当前编辑器。</summary>
    internal static void ToggleWithKeyboard(Button toggle)
    {
        Assert.True(toggle.Focus());
        toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(toggle), 0, Key.Space)
        { RoutedEvent = Keyboard.KeyDownEvent });
        toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(toggle), 0, Key.Space)
        { RoutedEvent = Keyboard.KeyUpEvent });
    }
}
