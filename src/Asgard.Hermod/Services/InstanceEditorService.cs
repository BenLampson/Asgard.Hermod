using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.Services;

/// <summary>由宿主决定新建实例窗口的所有者。</summary>
public sealed class InstanceEditorService : IInstanceEditor
{
    private readonly Func<Window?> _getOwner;

    /// <summary>延迟获取已启动的主窗口。</summary>
    public InstanceEditorService(Func<Window?> getOwner)
    {
        ArgumentNullException.ThrowIfNull(getOwner);
        _getOwner = getOwner;
    }

    /// <inheritdoc />
    public Task<InstanceSummary?> CreateAsync()
    {
        var model = new NewInstanceViewModel();
        var dialog = new NewInstanceWindow(model) { Owner = _getOwner() };
        InstanceSummary? created = dialog.ShowDialog() == true ? model.CreateInstance() : null;
        return Task.FromResult(created);
    }
}
