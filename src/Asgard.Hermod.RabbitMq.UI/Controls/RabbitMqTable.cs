using System.Windows.Data;
using Asgard.Hermod.RabbitMq.UI.ViewModels;

namespace Asgard.Hermod.RabbitMq.UI.Controls;

/// <summary>有界快照表格，列声明与数据均来自模型，不自动生成敏感字段。</summary>
public sealed class RabbitMqTable : DataGrid
{
    public static readonly DependencyProperty SchemaProperty = DependencyProperty.Register(nameof(Schema), typeof(IReadOnlyList<RabbitMqColumn>), typeof(RabbitMqTable), new PropertyMetadata(null, OnSchemaChanged));
    public IReadOnlyList<RabbitMqColumn>? Schema { get => (IReadOnlyList<RabbitMqColumn>?)GetValue(SchemaProperty); set => SetValue(SchemaProperty, value); }
    public RabbitMqTable()
    {
        AutoGenerateColumns = false; IsReadOnly = true; CanUserAddRows = false; CanUserDeleteRows = false;
        CanUserSortColumns = false; SelectionMode = DataGridSelectionMode.Single; SelectionUnit = DataGridSelectionUnit.FullRow;
        HeadersVisibility = DataGridHeadersVisibility.Column; RowHeight = 36; ColumnHeaderHeight = 36;
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal; BorderThickness = new(1); FontSize = 12;
        EnableRowVirtualization = true; EnableColumnVirtualization = false;
        SetResourceReference(StyleProperty, "RabbitMqTableStyle");
    }
    private static void OnSchemaChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var grid = (RabbitMqTable)sender; grid.Columns.Clear();
        var header = new FrameworkElementFactory(typeof(TextBlock));
        header.SetBinding(TextBlock.TextProperty, new Binding());
        header.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        header.SetResourceReference(TextBlock.ForegroundProperty, "RabbitMqMutedBrush");
        header.SetValue(TextBlock.FontSizeProperty, 12d);
        header.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        var headerTemplate = new DataTemplate { VisualTree = header };
        foreach (var column in grid.Schema ?? [])
        {
            var text = new Style(typeof(TextBlock));
            text.Setters.Add(new Setter(TextBlock.FontFamilyProperty, grid.FindResource("UiFont")));
            text.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12d));
            text.Setters.Add(new Setter(TextBlock.ForegroundProperty, grid.FindResource(column.Key == "name" ? "RabbitMqInkBrush" : "RabbitMqMutedBrush")));
            text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Title,
                HeaderTemplate = headerTemplate,
                Binding = new Binding($"[{column.Key}]"),
                ElementStyle = text,
                Width = column.Width == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(column.Width),
                MinWidth = column.Width == 0 ? 200 : 70
            });
        }
    }
}
