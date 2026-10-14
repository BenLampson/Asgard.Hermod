using System.Windows.Input;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>Tab 遍历真实模板，仅经过可操作项，布局容器不占用焦点站点。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class KeyboardNavigationTests(WpfTestFixture fixture)
{
    [Fact]
    public Task TypePicker_TabReachesOnlyTheOperableCard() => fixture.RunAsync(async () =>
    {
        var picker = new InstanceTypePicker
        {
            ItemsSource = new[] { new InstanceTypeDescriptor("first", "First", "Description", "database"),
            new InstanceTypeDescriptor("second", "Second", "Description", "database") }
        };
        var before = new Button { Content = "Before" }; var after = new Button { Content = "After" };
        var panel = new StackPanel(); panel.Children.Add(before); panel.Children.Add(picker); panel.Children.Add(after);
        var window = new HermodWindow { Content = panel, Width = 650, Height = 400 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window); Assert.True(before.Focus());
            Assert.True(before.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            var card = Assert.IsType<SelectionCard>(Keyboard.FocusedElement);
            card.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(card)!, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            card.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(card)!, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Assert.Same(card.DataContext, picker.SelectedItem);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Modal_TabStaysInsideAndClosingRestoresEntryFocus() => fixture.RunAsync(async () =>
    {
        var entry = new Button { Content = "Open" };
        var owner = new HermodWindow { Content = entry, Width = 650, Height = 420 };
        try
        {
            WindowPreviewCapture.Show(owner); await WindowTestVisuals.UpdateLayoutAsync(owner);
            Assert.True(entry.Focus());
            using var model = new MetadataEditorViewModel("Rename", "Name", "Original", 128, (_, _, _) => Task.CompletedTask);
            var window = new MetadataEditorWindow(model) { Owner = owner };
            await ModalWindowTestInteraction.ShowAsync(window, () =>
            {
                var input = Assert.IsType<TextBox>(window.FindName("NameInput")); Assert.True(input.IsKeyboardFocused);
                for (int i = 0; i < 12; i++)
                {
                    var focused = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
                    Assert.True(focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                    var next = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
                    Assert.True(window.IsAncestorOf(next));
                    Assert.False(next is FormField or DialogActionBar || next.GetType() == typeof(ContentControl));
                }
                model.CancelCommand.Execute(null);
                return Task.CompletedTask;
            });
            Assert.True(entry.IsKeyboardFocused);
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData("segmented")]
    [InlineData("section")]
    [InlineData("tabs")]
    public Task Tab_EntersAndLeavesSelectorAsOneGroup(string kind) => fixture.RunAsync(async () =>
    {
        ListBox selector = kind switch { "segmented" => new SegmentedSelector(), "section" => new SectionSelector(), _ => new WorkspaceTabStrip() };
        selector.ItemsSource = new[] { "First", "Second", "Third" }; selector.SelectedIndex = 1;
        var before = new Button { Content = "Before" }; var after = new Button { Content = "After" };
        var panel = new StackPanel(); panel.Children.Add(before); panel.Children.Add(selector); panel.Children.Add(after);
        var window = new HermodWindow { Content = panel, Width = 600, Height = 400 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(before.Focus());
            Assert.True(before.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            var entered = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
            Assert.True(entered.IsDescendantOf(selector));
            Assert.IsAssignableFrom<ListBoxItem>(entered);
            entered.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(entered)!, 0, Key.Right)
            { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(2, selector.SelectedIndex);
            entered = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
            Assert.True(entered.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(after, Keyboard.FocusedElement);
            Assert.True(after.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
            var returned = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
            Assert.True(returned.IsDescendantOf(selector));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task Tab_SkipsFieldHostAndActionBarButKeepsTheirChildren() => fixture.RunAsync(async () =>
    {
        var before = new Button { Content = "Before" };
        var input = new TextBox();
        var inside = new Button { Content = "Inside" };
        var after = new Button { Content = "After" };
        var panel = new StackPanel();
        panel.Children.Add(before);
        panel.Children.Add(new FormField { Label = "Name", Content = input });
        panel.Children.Add(new ContentControl { Content = new DialogActionBar { Content = inside } });
        panel.Children.Add(after);
        var window = new HermodWindow { Content = panel, Width = 600, Height = 400 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(before.Focus());
            Assert.True(before.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(input, Keyboard.FocusedElement);
            Assert.True(input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(inside, Keyboard.FocusedElement);
            Assert.True(inside.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(after, Keyboard.FocusedElement);
            Assert.True(after.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
            Assert.Same(inside, Keyboard.FocusedElement);
            Assert.True(inside.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
            Assert.Same(input, Keyboard.FocusedElement);
        }
        finally { window.Close(); }
    });
}
