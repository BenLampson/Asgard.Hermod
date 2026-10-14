using Asgard.Hermod.MySql;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在创建 HWND 的同步路径注入异常，真实验证 ShowDialog 的失败清理。</summary>
internal sealed class WizardFailureTestContentProvider : IInstanceConfigurationContentProvider
{
    /// <summary>跟踪真实向导，结束后必须已经触发 Closed。</summary>
    internal NewInstanceWindow? Window { get; private set; }
    /// <summary>跟踪关闭通知，避免只隐藏故障窗口。</summary>
    internal bool WasClosed { get; private set; }
    /// <summary>跟踪模型，验证故障路径已经解绑并释放。</summary>
    internal NewInstanceViewModel? Model { get; private set; }
    /// <summary>与真实 MySQL 编辑模式匹配。</summary>
    public string TypeId => MySqlIds.Type;
    /// <summary>故障与窗口大小无关。</summary>
    public Size PreferredWizardSize => new(912, 740);
    /// <summary>只在本用例的向导 HWND 创建时抛错，不污染应用 Dispatcher 的其他操作。</summary>
    public FrameworkElement CreateContent(NewInstanceViewModel model)
    {
        Model = model;
        Window = Assert.Single(Application.Current.Windows.OfType<NewInstanceWindow>(), window => ReferenceEquals(window.DataContext, model));
        Window.Closed += (_, _) => WasClosed = true;
        Window.SourceInitialized += (_, _) => throw new InvalidOperationException("Expected wizard initialization failure.");
        return new Grid();
    }
}
