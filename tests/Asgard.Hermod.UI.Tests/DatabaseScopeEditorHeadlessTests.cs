using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>直接渲染浮层内容，不打开 Popup 或 Window。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseScopeEditorHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, 540)]
    [InlineData("en", UiTheme.Dark, 540)]
    [InlineData("zh-Hans", UiTheme.Dark, 400)]
    public Task ScopeContent_MatchesDesignGeometryAndKeepsChangesLocal(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var scope = new DatabaseSessionScope("instance", "Session 1", [new("instance", "ben-aggregation-platform")]);
            int calls = 0; var model = new DatabaseScopeEditorViewModel(scope, new[] { "heimdall", "hermod_dev", "analytics" }.Select(name => new DatabaseIdentity("instance", name)), _ => { calls++; return Task.FromResult(false); });
            model.Choices[1].IsSelected = true;
            using var bindings = new WpfBindingTrace(); var view = new DatabaseScopeEditor { DataContext = model, Width = width };
            var root = new Border { Child = view }; root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            root.Measure(new(width, double.PositiveInfinity)); root.Arrange(new(0, 0, width, root.DesiredSize.Height)); root.UpdateLayout();
            await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            Assert.Equal(width, view.ActualWidth); Assert.Equal(new Thickness(19), ((Border)view.FindName("Panel")).Padding);
            var choices = WindowTestVisuals.Descendants<CheckBox>(view).ToArray(); Assert.Equal(4, choices.Length);
            Assert.All(choices, item => Assert.Equal(38, item.ActualHeight)); Assert.Equal(20, choices[0].TranslatePoint(new(), root).X);
            if (language == "zh-Hans" && width == 540) Assert.InRange(choices[0].TranslatePoint(new(), root).Y, 95, 97);
            for (int index = 1; index < choices.Length; index++) Assert.Equal(54, choices[index].TranslatePoint(new(), root).Y - choices[index - 1].TranslatePoint(new(), root).Y, 6);
            Assert.True(model.Choices[1].IsPendingAdd); Assert.Equal(Text.Get("DatabaseScopeIncluded"), model.Choices[0].State);
            Assert.Equal(0, calls); Assert.Single(scope.Databases); Assert.Empty(bindings.Messages);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var selectedIcon = (IconView)choices[0].Template.FindName("Selection", choices[0]);
            Assert.True(VisualTreeHelper.GetDrawing(selectedIcon)?.Bounds.Width > 0);
            string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, $"scope-{language}-{theme}-{width}.png")); encoder.Save(stream);
            }
            model.CancelCommand.Execute(null); Assert.False(model.IsOpen); Assert.Single(scope.Databases); Assert.Equal(0, calls);
            Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public void CrossInstanceCandidate_WithSameNameCannotHideBehindAnIncludedDatabase()
    {
        var scope = new DatabaseSessionScope("instance", "Session 1", [new("instance", "app")]);
        Assert.ThrowsAny<Exception>(() => new DatabaseScopeEditorViewModel(scope, [new("other", "app")], _ => Task.FromResult(true)));
        Assert.Single(scope.Databases); Assert.Equal(0, scope.Revision);
    }

    [Fact]
    public void BatchChange_RejectsForeignEmptyStaleAndImplicitTargetChanges()
    {
        var scope = new DatabaseSessionScope("instance", "Session 1", [new("instance", "app"), new("instance", "reports")]);
        scope.SelectExecutionTarget("app");
        Assert.ThrowsAny<Exception>(() => scope.PrepareChange([]));
        Assert.ThrowsAny<Exception>(() => scope.PrepareChange([new("other", "audit")]));
        var change = scope.PrepareChange([new("instance", "reports"), new("instance", "audit")]);
        Assert.False(scope.ApplyConfirmedChange(change, "")); Assert.False(scope.ApplyConfirmedChange(change, "outside"));
        Assert.Equal(2, scope.Databases.Count); Assert.Equal("app", scope.ExecutionTarget);
        Assert.True(scope.ApplyConfirmedChange(change, "reports")); Assert.Equal(new[] { "reports", "audit" }, scope.Databases.Select(item => item.Name));
        Assert.False(scope.ApplyConfirmedChange(change, "reports")); Assert.Equal("reports", scope.ExecutionTarget);
        var keepTarget = scope.PrepareChange([new("instance", "reports"), new("instance", "audit"), new("instance", "app")]);
        Assert.False(scope.ApplyConfirmedChange(keepTarget, "audit")); Assert.Equal(2, scope.Databases.Count);
    }
}
