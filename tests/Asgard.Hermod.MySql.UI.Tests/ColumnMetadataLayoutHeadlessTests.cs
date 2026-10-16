using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.MySql;
using Asgard.Hermod.UI.Controls;
using Asgard.Hermod.UI.ViewModels;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>真实模块视图绑定结构化字段，不从旧详情文本猜测类型，切换本地分区不读取或写入。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ColumnMetadataLayoutHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task ObjectViewUsesTypedColumnFactsAndPreservesDataSnapshot() => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
        try
        {
            var fact = new DatabaseColumnDefinition(3, "numeric(12,2)", false, DatabaseColumnKeyKind.Unique, null, "STORED", "original user comment") { GenerationExpression = "price * quantity" };
            var server = new TestMySql { MetadataOverride = parent => parent.Kind == MySqlObjectKind.Columns ? [new(MySqlObjectKind.Column, "server_column", "app", "users", "not a data type") { ColumnDefinition = fact }] : null };
            await using var model = new MySqlTabViewModel("users", "app", MySqlObjectKind.Table, server, TestMySql.Connection, new TestMySqlDialogs());
            await model.ReadAsync(); var snapshot = model.SelectedResult; int reads = server.PageReads.Count;
            model.Section = "structure"; using var bindings = new WpfBindingTrace(); var view = new MySqlTabView { DataContext = model };
            view.Resources.MergedDictionaries.Add(Application.Current.Resources); await Layout();
            var reader = (DatabaseColumnReader)view.FindName("ColumnReader"); Assert.Equal(Visibility.Visible, reader.Visibility);
            var row = Assert.IsType<DatabaseColumnRow>(Assert.Single(((DataGrid)reader.FindName("ColumnGrid")).Items.Cast<object>()));
            Assert.Equal("numeric(12,2)", row.DataType); Assert.DoesNotContain("not a data type", row.Detail); Assert.Contains("price * quantity", row.Detail);
            Assert.Equal("original user comment", row.Comment); Assert.Equal("UNIQUE KEY", row.ConstraintLabel);
            Assert.Equal(Visibility.Collapsed, ((DataGrid)view.FindName("StructureGrid")).Visibility);
            model.StructureSection = "definition"; await Layout(); Assert.Equal(Visibility.Collapsed, reader.Visibility);
            Assert.Equal(Visibility.Visible, ((CodeEditor)view.FindName("DefinitionEditor")).Visibility);
            model.Section = "data"; await Layout(); Assert.Same(snapshot, model.SelectedResult); Assert.Equal(reads, server.PageReads.Count); Assert.Equal(0, server.Executions);
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(view)); Assert.Null(PresentationSource.FromVisual(view));
            async Task Layout()
            {
                view.Measure(new(1160, 720)); view.Arrange(new(0, 0, 1160, 720)); view.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout();
            }
        }
        finally { Text.SetApplicationCulture(null); }
    });
}
