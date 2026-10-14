using Asgard.Hermod.Redis.UI.ViewModels;
using Asgard.Hermod.Redis.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.Redis.UI.Services;

/// <summary>公共窗口承担主题、标题和操作栏，Redis 只提供正文及明确的确认语义。</summary>
public sealed class WindowsRedisWorkspaceDialogs(Func<Window?> owner) : IRedisWorkspaceDialogs
{
    /// <inheritdoc />
    public Task<RedisCloseChoice> CloseDirtyAsync(string key, int database)
    {
        var dialog = new HermodDialog { Owner = owner(), Title = Text.Get("RedisCloseDirty"), Width = 620, Height = 280 };
        dialog.Content = new TextBlock { Text = Text.Format("RedisCloseDirtyMessage", key, database), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        RedisCloseChoice choice = RedisCloseChoice.Continue;
        void Add(string resource, RedisCloseChoice result, string style)
        {
            var button = new Button { Content = Text.Get(resource), Margin = new Thickness(8, 0, 0, 0) };
            button.SetResourceReference(FrameworkElement.StyleProperty, style);
            button.Click += (_, _) => { choice = result; dialog.DialogResult = result != RedisCloseChoice.Continue; };
            buttons.Children.Add(button);
        }
        Add("RedisContinueEditing", RedisCloseChoice.Continue, "GhostButton");
        Add("RedisDiscardClose", RedisCloseChoice.Discard, "DangerButton");
        Add("RedisSaveClose", RedisCloseChoice.Save, "PrimaryButton");
        dialog.FooterContent = new DialogActionBar { Content = buttons };
        dialog.Loaded += (_, _) => ((Button)buttons.Children[0]).Focus(); dialog.ShowDialog();
        return Task.FromResult(choice);
    }
    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, int database) => Task.FromResult(new ConfirmationDialog(Text.Get("RedisDeleteKey"),
        Text.Format("RedisDeleteConfirm", key, database), Text.Get("RedisDeleteKey"), true)
    { Owner = owner() }.ShowDialog() == true);
    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, int database, string target) => Task.FromResult(new ConfirmationDialog(Text.Get("RedisDeleteKey"),
        Text.Format("RedisDeleteTargetConfirm", target, key), Text.Get("RedisDeleteKey"), true)
    { Owner = owner() }.ShowDialog() == true);
    /// <inheritdoc />
    public Task<RedisKeyId?> CreateAsync(IRedisSession session, int database, string prefix, CancellationToken cancellationToken = default)
        => CreateAsync(session, database, prefix, Text.Format("RedisWriteTarget", Text.Get("InstanceUnknown"), Text.Get("EnvironmentNotSet"), database), cancellationToken);
    /// <inheritdoc />
    public Task<RedisKeyId?> CreateAsync(IRedisSession session, int database, string prefix, string target, CancellationToken cancellationToken = default)
    {
        using var model = new RedisCreateViewModel(session, database, prefix, cancellationToken, target);
        var dialog = new RedisCreateDialog(model) { Owner = owner() };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.ResultKey : null);
    }
    /// <inheritdoc />
    public async Task EditAsync(RedisKeyTabViewModel tab, bool append = false)
    {
        if (!tab.IsEditing)
        {
            if (append) tab.AppendCommand.Execute(null);
            else await tab.EditCommand.ExecuteAsync();
        }
        if (!tab.IsEditing) return;
        new RedisEditDialog(tab) { Owner = owner() }.ShowDialog();
    }
    /// <inheritdoc />
    public Task<string?> ExpiryAsync(string key)
    {
        var dialog = new HermodDialog { Owner = owner(), Title = Text.Get("RedisChangeExpiry"), Width = 540, Height = 280 };
        var input = new TextBox { MaxLength = 18 };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = key, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = key, Margin = new Thickness(0, 0, 0, 18) });
        var field = new FormField { Label = Text.Get("RedisExpirySeconds"), Content = input }; content.Children.Add(field);
        dialog.Content = content;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = Text.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Content = Text.Get("RedisChangeExpiry") }; save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        save.Click += (_, _) =>
        {
            if (input.Text.Length != 0 && (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || !double.IsFinite(value) || value <= 0 || value > TimeSpan.MaxValue.TotalSeconds)) { field.Error = Text.Get("RedisExpiryInvalid"); return; }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); dialog.FooterContent = new DialogActionBar { Content = buttons };
        dialog.Loaded += (_, _) => input.Focus();
        return Task.FromResult(dialog.ShowDialog() == true ? input.Text : null);
    }
    /// <inheritdoc />
    public Task ShowLatestAsync(string key, string preview)
    {
        var dialog = new HermodDialog { Owner = owner(), Title = Text.Get("RedisPreviewLatest"), Width = 780, Height = 620 };
        var panel = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock { Text = key, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = key, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var text = new TextBox
        {
            Text = preview,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        text.SetResourceReference(Control.FontFamilyProperty, "MonoFont"); panel.Children.Add(text); dialog.Content = panel;
        var close = new Button { Content = Text.Get("Close"), IsCancel = true }; dialog.FooterContent = new DialogActionBar { Content = close };
        dialog.ShowDialog(); return Task.CompletedTask;
    }
}
