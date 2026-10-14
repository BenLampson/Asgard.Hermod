using System.Globalization;
using System.Windows.Automation;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>共享资源先加载后再应用手动语言，也在打开前得到正确文案；不打开菜单或访问剪贴板。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TerminalClipboardLocalizationHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task Menu_ResolvesLanguageAtPreparationAndTargetsSameTerminal(string language) => fixture.RunOffscreenAsync(() =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            using var session = new TerminalSession(); using var terminal = new TerminalControl { Session = session, IsReadOnly = true };
            var root = new Border { Child = terminal }; root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            root.Measure(new(200, 100)); root.Arrange(new(0, 0, 200, 100));
            var menu = Assert.IsType<TerminalClipboardMenu>(terminal.ContextMenu); menu.PrepareForTarget(terminal);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            string[] keys = ["TerminalCopy", "TerminalPaste", "TerminalSelectAll"];
            for (int index = 0; index < items.Length; index++)
            {
                Assert.Equal(Text.Get(keys[index]), items[index].Header);
                Assert.Equal(items[index].Header, AutomationProperties.GetName(items[index])); Assert.Same(terminal, items[index].CommandTarget);
            }
            Assert.False(menu.IsOpen); Assert.Null(PresentationSource.FromVisual(terminal));
        }
        finally { Text.SetApplicationCulture(null); }
        return Task.CompletedTask;
    });
}
