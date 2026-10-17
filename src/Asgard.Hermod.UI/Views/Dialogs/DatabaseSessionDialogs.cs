using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>PGSQL 与 MySQL 共享同一套范围和移除确认窗口。</summary>
public static class DatabaseSessionDialogs
{
    /// <summary>实例身份由调用者固定；只有一个可添加项时直接使用，多个候选不预选。</summary>
    public static IReadOnlyList<DatabaseIdentity>? Select(Window? owner, string instanceName, string endpoint, string sessionName,
        IReadOnlyList<DatabaseIdentity> candidates, IReadOnlyList<DatabaseIdentity> included)
    {
        var dialog = Create(owner, Text.Format("DatabaseSessionAddTitle", sessionName), 620);
        var panel = new StackPanel { Margin = new Thickness(24) };
        Heading(panel, dialog.Title, Text.Format("DatabaseSessionInstance", instanceName, endpoint));
        panel.Children.Add(new TextBlock { Text = Text.Get("DatabaseSessionAddHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var choices = candidates.Select(item => new DatabaseChoiceViewModel(item, included.Contains(item))).ToArray();
        var available = choices.Where(item => item.CanSelect).ToArray();
        if (available.Length == 1) available[0].IsSelected = true;
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        System.Windows.Automation.AutomationProperties.SetName(search, Text.Get("DatabaseSessionSearch"));
        search.ToolTip = Text.Get("DatabaseSessionSearch"); panel.Children.Add(search);
        var all = new Button { Content = Text.Get("DatabaseSessionSelectAll"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), Visibility = available.Length > 1 ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(all);
        var rows = new StackPanel();
        var accept = new Button { Content = Text.Get("DatabaseSessionAdd"), IsEnabled = available.Length == 1 };
        foreach (var choice in choices)
        {
            if (choice.IsIncluded || available.Length == 1)
            {
                rows.Children.Add(new TextBlock { Text = choice.IsIncluded ? Text.Format("DatabaseSessionAlreadyIncluded", choice.Name) : choice.Name, Tag = choice, Margin = new Thickness(6, 10, 6, 10) });
                continue;
            }
            var row = new CheckBox
            {
                Content = choice.IsIncluded ? Text.Format("DatabaseSessionAlreadyIncluded", choice.Name) : choice.Name,
                IsEnabled = choice.CanSelect,
                IsChecked = choice.IsIncluded || choice.IsSelected,
                Margin = new Thickness(6, 10, 6, 10),
                Tag = choice
            };
            if (available.Length == 1 && choice.CanSelect) { row.IsEnabled = false; }
            row.Checked += (_, _) => { choice.IsSelected = true; Update(); };
            row.Unchecked += (_, _) => { choice.IsSelected = false; Update(); };
            rows.Children.Add(row);
        }
        all.Click += (_, _) => { foreach (CheckBox row in rows.Children.OfType<CheckBox>()) if (((DatabaseChoiceViewModel)row.Tag).CanSelect) row.IsChecked = true; };
        search.TextChanged += (_, _) => { foreach (FrameworkElement row in rows.Children) row.Visibility = ((DatabaseChoiceViewModel)row.Tag).Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; };
        panel.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 180 });
        dialog.Content = panel;
        IReadOnlyList<DatabaseIdentity>? result = null;
        Footer(dialog, accept, () => { result = available.Where(item => item.IsSelected).Select(item => item.Identity).ToArray(); dialog.DialogResult = true; });
        dialog.ShowDialog(); return result;
        void Update() => accept.IsEnabled = available.Any(item => item.IsSelected);
    }

    /// <summary>唯一替代目标固定展示；多个替代目标必须显式选择后才能确认。</summary>
    public static DatabaseRemovalDecision? Remove(Window? owner, string instanceName, string endpoint, DatabaseRemovalRequest request)
    {
        var dialog = Create(owner, Text.Get("DatabaseSessionRemoveTitle"), 560);
        var panel = new StackPanel { Margin = new Thickness(24) };
        Heading(panel, dialog.Title, Text.Format("DatabaseSessionRemovalFacts", instanceName, endpoint, request.SessionName, request.Database));
        panel.Children.Add(new TextBlock { Text = Text.Get("DatabaseSessionRemoveHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 16) });
        bool target = request.Database == request.ExecutionTarget;
        string? replacement = target && request.RemainingDatabases.Count == 1 ? request.RemainingDatabases[0] : null;
        var accept = new Button { Content = Text.Get("DatabaseSessionConfirmRemove"), IsEnabled = !target || replacement is not null };
        if (target)
        {
            panel.Children.Add(new TextBlock { Text = Text.Get("DatabaseSessionReplacementHint"), TextWrapping = TextWrapping.Wrap });
            if (replacement is not null) panel.Children.Add(new TextBlock { Text = replacement, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
            else
            {
                var selector = new ComboBox { ItemsSource = request.RemainingDatabases, SelectedIndex = -1, Margin = new Thickness(0, 12, 0, 0) };
                System.Windows.Automation.AutomationProperties.SetName(selector, Text.Get("DatabaseSessionExecutionTarget"));
                selector.SelectionChanged += (_, _) => { replacement = selector.SelectedItem as string; accept.IsEnabled = replacement is not null; };
                panel.Children.Add(selector);
            }
        }
        dialog.Content = panel; DatabaseRemovalDecision? result = null;
        Footer(dialog, accept, () => { result = new(replacement); dialog.DialogResult = true; });
        dialog.ShowDialog(); return result;
    }

    /// <summary>所有内容移除均明确指出实例、Session 和操作影响。</summary>
    public static bool Confirm(Window? owner, string instanceName, string sessionName, string actionKey, string endpoint = "") =>
        new ConfirmationDialog(Text.Get(actionKey), Text.Format("DatabaseSessionActionFacts", Text.Format("DatabaseSessionInstance", instanceName, endpoint), sessionName, Text.Get(actionKey)), Text.Get("DatabaseSessionConfirm"), true) { Owner = owner }.ShowDialog() == true;

    private static HermodDialog Create(Window? owner, string title, double height) => new() { Owner = owner, Title = title, Width = 680, Height = height };
    private static void Heading(Panel panel, string title, string facts)
    {
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock { Text = facts, TextWrapping = TextWrapping.Wrap });
    }
    private static void Footer(HermodDialog dialog, Button accept, Action commit)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Text.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        accept.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton"); accept.Click += (_, _) => commit();
        buttons.Children.Add(cancel); buttons.Children.Add(accept);
        dialog.FooterContent = new DialogActionBar { Content = buttons, Height = 56, Padding = new Thickness(20, 0, 20, 0) };
        dialog.Loaded += (_, _) => cancel.Focus();
    }
}
