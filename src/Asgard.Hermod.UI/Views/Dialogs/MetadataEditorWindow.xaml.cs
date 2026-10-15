using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>轻量元数据窗口复用公共标题、主题、操作栏和标签浮层。</summary>
public partial class MetadataEditorWindow : HermodDialog
{
    private readonly MetadataEditorViewModel _model;
    private readonly IInputElement? _previousFocus = Keyboard.FocusedElement;
    /// <summary>输入焦点进入唯一可编辑字段，关闭后回到入口。</summary>
    public MetadataEditorWindow(MetadataEditorViewModel model)
    {
        _model = model; InitializeComponent(); DataContext = model;
        if (model.EditNameAndTags) Height = model.IsHostingProviderEditing ? 500 : 400;
        model.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => { if (model.IsNameEditing) NameInput.Focus(); else (TagSelector.FindName("TagTrigger") as Button)?.Focus(); };
        Closing += (_, args) => { if (model.IsBusy && !model.WasSaved) args.Cancel = true; };
        Closed += (_, _) =>
        {
            model.CloseRequested -= OnCloseRequested; model.Dispose();
            if (_previousFocus is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus();
        };
    }
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;
    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        if (!HostingProviderSelector.CloseDropDown() && !_model.Tags.CancelPicker()) _model.CancelCommand.Execute(null);
        args.Handled = true;
    }
}
