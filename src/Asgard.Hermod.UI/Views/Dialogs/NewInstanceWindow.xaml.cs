using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>新建实例表单，仅处理窗口与焦点生命周期。</summary>
public partial class NewInstanceWindow : HermodDialog
{
    /// <summary>绑定由宿主提供的表单状态。</summary>
    public NewInstanceWindow(NewInstanceViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => NameInput.Focus();
        Closed += (_, _) => model.CloseRequested -= OnCloseRequested;
    }

    /// <summary>将表单提交或取消转换为模态窗口结果。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;
}
