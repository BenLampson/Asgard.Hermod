using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>共享确认窗口，操作含义由调用者使用完整资源句式提供。</summary>
public sealed class ConfirmationDialog : HermodDialog
{
    /// <summary>默认焦点交给取消，原始预览内容不翻译、不改写。</summary>
    public ConfirmationDialog(string title, string message, string acceptLabel, bool danger = false, string? preview = null)
    {
        Title = title; Width = 640; Height = preview is null ? 360 : 520;
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (preview is not null)
        {
            var text = new TextBox
            {
                Text = preview,
                IsReadOnly = true,
                AcceptsReturn = true,
                Height = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 14, 0, 0)
            };
            text.SetResourceReference(FontFamilyProperty, "MonoFont"); content.Children.Add(text);
        }
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Text.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var accept = new Button { Content = acceptLabel };
        accept.SetResourceReference(StyleProperty, danger ? "DangerButton" : "PrimaryButton");
        accept.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(accept);
        FooterContent = new DialogActionBar { Height = 56, Padding = new Thickness(20, 0, 20, 0), Content = buttons };
        Loaded += (_, _) => cancel.Focus();
    }
}
