using Asgard.Hermod.PostgreSql.UI.ViewModels;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.PostgreSql.UI.Views;

/// <summary>复用公共可调整窗口，应用仅更新调用单元格的本地草稿。</summary>
public partial class PostgreSqlFieldDialog : HermodDialog
{
    private readonly PostgreSqlFieldEditorViewModel _model;
    private bool _applied;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public PostgreSqlFieldDialog(PostgreSqlFieldEditorViewModel model)
    {
        _model = model; InitializeComponent(); DataContext = model;
        if (!model.IsEditable) Title = Text.Get("PostgreSqlViewField");
        Loaded += (_, _) => FieldInput.FocusInput();
    }
    private void OnApply(object sender, RoutedEventArgs args) { if (!_model.Apply()) return; _applied = true; DialogResult = true; }
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_applied || !_model.IsEditable || !_model.IsDirty) return;
        if (new ConfirmationDialog(Text.Get("PostgreSqlDiscardField"), Text.Get("PostgreSqlDiscardFieldHint"), Text.Get("PostgreSqlDiscard")) { Owner = this }.ShowDialog() != true) args.Cancel = true;
    }
}
