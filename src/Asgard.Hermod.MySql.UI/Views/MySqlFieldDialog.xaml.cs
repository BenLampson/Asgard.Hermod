using Asgard.Hermod.MySql.UI.ViewModels;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>复用公共可调整窗口，应用仅更新调用单元格的本地草稿。</summary>
public partial class MySqlFieldDialog : HermodDialog
{
    private readonly MySqlFieldEditorViewModel _model;
    private bool _applied;
    public MySqlFieldDialog(MySqlFieldEditorViewModel model)
    {
        _model = model; InitializeComponent(); DataContext = model;
        if (!model.IsEditable) Title = Text.Get("MySqlViewField");
        Loaded += (_, _) => FieldInput.FocusInput();
    }
    private void OnApply(object sender, RoutedEventArgs args) { if (!_model.Apply()) return; _applied = true; DialogResult = true; }
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_applied || !_model.IsEditable || !_model.IsDirty) return;
        if (new ConfirmationDialog(Text.Get("MySqlDiscardField"), Text.Get("MySqlDiscardFieldHint"), Text.Get("MySqlDiscard")) { Owner = this }.ShowDialog() != true) args.Cancel = true;
    }
}
