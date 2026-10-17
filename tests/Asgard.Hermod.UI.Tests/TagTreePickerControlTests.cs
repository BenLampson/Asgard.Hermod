using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实树复选框的原生自动化交互与双向绑定。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TagTreePickerControlTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>控件模板与自动化 Peer 在共享 STA 应用中创建。</summary>
    public TagTreePickerControlTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>半选父节点经原生 Toggle 全选整枝，再次 Toggle 清除且不破坏绑定。</summary>
    [Fact]
    public Task HalfCheckedParent_AutomationToggleSelectsAndClearsEntireBranch() => _fixture.RunAsync(async () =>
    {
        var model = new TagTreePickerViewModel(TagPickerTestData.Definitions(),
            ["Environment/Production/cn-east"], TagFilterMatchMode.Any);
        var environment = TagPickerTestData.Node(model, "Environment");
        var picker = new TagTreePicker { Model = model, Height = 650 };
        var window = new HermodWindow
        {
            Title = "Tag toggle regression",
            Content = picker,
            Width = 420,
            Height = 710,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var checkbox = Assert.Single(WindowTestVisuals.Descendants<TagTreeCheckBox>(picker),
                control => ReferenceEquals(control.DataContext, environment));
            Assert.Null(checkbox.IsChecked);
            Assert.NotNull(BindingOperations.GetBindingExpression(checkbox, CheckBox.IsCheckedProperty));
            var peer = Assert.IsAssignableFrom<AutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(checkbox));
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
            Assert.Equal(ToggleState.Indeterminate, toggle.ToggleState);

            toggle.Toggle();
            await WpfTestFixture.DrainDispatcherAsync();

            Assert.Equal(ToggleState.On, toggle.ToggleState);
            Assert.Equal(new[] { "Environment" }, model.SelectedPaths);
            Assert.All(TagPickerTestData.Descendants([environment]), node => Assert.Equal<bool?>(true, node.IsChecked));
            Assert.NotNull(BindingOperations.GetBindingExpression(checkbox, CheckBox.IsCheckedProperty));

            toggle.Toggle();
            await WpfTestFixture.DrainDispatcherAsync();

            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            Assert.Empty(model.SelectedPaths);
            Assert.All(TagPickerTestData.Descendants([environment]), node => Assert.Equal<bool?>(false, node.IsChecked));
            Assert.NotNull(BindingOperations.GetBindingExpression(checkbox, CheckBox.IsCheckedProperty));
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    });
}
