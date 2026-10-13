namespace Asgard.Hermod.UI.Tests;

/// <summary>尚未打开的弹层不得把草稿绑定解析到确认页的已应用选择模型。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class InstanceTagBindingTests(WpfTestFixture fixture)
{
    [Fact]
    public Task ClosedPopup_DoesNotInheritTheSelectionModel() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var selector = new InstanceTagSelector { Model = new InstanceTagSelectionViewModel([]) };
        var window = new HermodWindow { Width = 640, Height = 320, Content = selector };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Null(Assert.IsType<Border>(selector.FindName("PopupPanel")).DataContext);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); }
    });
}
