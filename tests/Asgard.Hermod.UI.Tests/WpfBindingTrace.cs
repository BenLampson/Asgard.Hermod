using System.Diagnostics;

namespace Asgard.Hermod.UI.Tests;

/// <summary>显式启用 WPF 诊断并捕获真实绑定错误，不能只凭页面已显示判断绑定正常。</summary>
internal sealed class WpfBindingTrace : TraceListener
{
    private readonly SourceLevels _previous;
    internal List<string> Messages { get; } = [];
    internal WpfBindingTrace()
    {
        PresentationTraceSources.Refresh();
        _previous = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        PresentationTraceSources.DataBindingSource.Listeners.Add(this);
    }
    public override void Write(string? message) { if (message is not null) Messages.Add(message); }
    public override void WriteLine(string? message) { if (message is not null) Messages.Add(message); }
    protected override void Dispose(bool disposing)
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
        PresentationTraceSources.DataBindingSource.Switch.Level = _previous; base.Dispose(disposing);
    }
}
