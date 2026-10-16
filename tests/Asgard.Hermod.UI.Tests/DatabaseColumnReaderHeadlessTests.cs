using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>字段阅读器只消费服务器元数据，逐语言、主题和 DPI 验证真实行列与选择状态。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseColumnReaderHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, 1160, 96)]
    [InlineData("en", UiTheme.Dark, 1160, 192)]
    [InlineData("en", UiTheme.Light, 600, 96)]
    public Task NativeColumnGridPreservesServerDefaultsAndUsesReferenceGeometry(string language, UiTheme theme, double width, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var rows = Rows(); var view = new DatabaseColumnReader { Rows = rows }; var root = new Border { Child = view };
            root.Resources.MergedDictionaries.Add(Application.Current.Resources); root.Background = (Brush)view.FindResource("SurfaceBrush");
            using var bindings = new WpfBindingTrace(); await Layout();
            var grid = (DataGrid)view.FindName("ColumnGrid"); Assert.Equal(5, grid.Columns.Count); Assert.Equal(36, grid.ColumnHeaderHeight); Assert.Equal(44, grid.RowHeight);
            if (width == 1160) Assert.Equal(300, grid.ActualHeight);
            Assert.Equal(new Point(0, grid.ActualHeight + 16), ((Border)view.FindName("SelectionCard")).TranslatePoint(new(), view));
            Assert.True(grid.IsReadOnly); Assert.Same(rows[0], grid.SelectedItem);
            grid.SelectedItem = rows[^1]; await Layout();
            var definition = (TextBox)view.FindName("SelectedDefinition"); Assert.Contains("datetime(6)", definition.Text); Assert.Contains("CURRENT_TIMESTAMP(6)", definition.Text);
            Assert.Equal("created_at", ((TextBlock)view.FindName("SelectedName")).Text); Assert.Equal("订单创建时间", ((TextBlock)view.FindName("SelectedComment")).Text);
            Assert.Contains("DEFAULT CURRENT_TIMESTAMP(6)", definition.Text); Assert.True(definition.IsReadOnly);
            var bitmap = new RenderTargetBitmap((int)(width * dpi / 96), (int)(520 * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(root);
            var nameGlyphs = OffscreenGlyphMeasurement.Read((TextBlock)view.FindName("SelectedName"), root); Assert.NotEmpty(nameGlyphs);
            Assert.All(nameGlyphs, glyph => { Assert.Equal(13, glyph.Size); Assert.Contains("jetbrainsmono-medium.ttf", glyph.Font, StringComparison.OrdinalIgnoreCase); });
            if (width == 1160) { Assert.Equal(210, grid.Columns[0].ActualWidth); Assert.Equal(210, grid.Columns[1].ActualWidth); Assert.Equal(110, grid.Columns[2].ActualWidth); Assert.Equal(250, grid.Columns[3].ActualWidth); }
            else { var viewer = (ScrollViewer)grid.Template.FindName("DG_ScrollViewer", grid); Assert.True(viewer.ScrollableWidth > 0); }
            Capture(bitmap, $"db-columns-{language}-{theme}-{width}-{dpi}");
            view.Rows = Rows(); await Layout(); Assert.Equal("created_at", Assert.IsType<DatabaseColumnRow>(grid.SelectedItem).Name);
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            async Task Layout()
            {
                root.Measure(new(width, 520)); root.Arrange(new(0, 0, width, 520)); root.UpdateLayout();
                VisualTreeHelper.SetRootDpi(root, new(1, 1)); VisualTreeHelper.SetRootDpi(root, new(dpi / 96, dpi / 96));
                root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public void MissingMetadataNeverBecomesNullableOrAnInventedDefault()
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
        try
        {
            var unknown = new DatabaseColumnRow("raw_name", "raw_type", null); Assert.Equal("raw_type", unknown.DataType);
            Assert.Equal(Text.Get("DatabaseMetadataUnavailable"), unknown.NullableLabel); Assert.Equal(Text.Get("DatabaseMetadataUnavailable"), unknown.DefaultLabel);
            var empty = new DatabaseColumnRow("empty_default", "text", new(1, "text", true, DatabaseColumnKeyKind.None, "", "", ""));
            Assert.Equal("''", empty.DefaultLabel); Assert.NotEqual(unknown.DefaultLabel, empty.DefaultLabel);
        }
        finally { Text.SetApplicationCulture(null); }
    }
    private static DatabaseColumnRow[] Rows() => [
        new("order_id", "bigint", new(1, "bigint", false, DatabaseColumnKeyKind.Primary, null, "auto_increment", "")),
        new("user_id", "bigint", new(2, "bigint", false, DatabaseColumnKeyKind.Indexed, null, "", "")),
        new("amount", "decimal(10,2)", new(3, "decimal(10,2)", false, DatabaseColumnKeyKind.None, "0.00", "", "")),
        new("status", "varchar(20)", new(4, "varchar(20)", false, DatabaseColumnKeyKind.None, "pending", "", "")),
        new("remark", "text", new(5, "text", true, DatabaseColumnKeyKind.None, null, "", "")),
        new("created_at", "datetime(6)", new(6, "datetime(6)", false, DatabaseColumnKeyKind.None, "CURRENT_TIMESTAMP(6)", "", "订单创建时间"))];
    private static void Capture(BitmapSource bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is not { } directory) return;
        Directory.CreateDirectory(directory); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
