using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.PostgreSql;
using Xunit;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>JSON 树保留成本与原始位置，缺失指标不补零且不将估算伪装为实际分析。</summary>
public sealed class PostgreSqlPlanReaderTests
{
    [Fact]
    public void Estimate_KeepsCostPrecisionWithoutFabricatingExecutionTime()
    {
        const string raw = "[{\"Plan\":{\"Node Type\":\"Sort\",\"Startup Cost\":1.2500,\"Total Cost\":1.8000,\"Plan Rows\":5,\"Sort Key\":[\"updated_at DESC\"],\"Plans\":[{\"Node Type\":\"Seq Scan\",\"Relation Name\":\"model_config\",\"Plan Rows\":20}]}}]";
        var snapshot = Read(raw); var sort = Assert.Single(snapshot.Roots); var scan = Assert.Single(sort.Children);
        Assert.Equal(DatabasePlanFormat.Tree, snapshot.Format); Assert.False(snapshot.IsActual); Assert.Equal(raw, snapshot.RawText);
        Assert.Equal("1.2500", sort.Metrics.StartupCost); Assert.Equal("1.8000", sort.Metrics.TotalCost);
        Assert.Null(sort.Metrics.FirstRowMilliseconds); Assert.Null(sort.Metrics.CompletionMilliseconds); Assert.Null(sort.Metrics.ActualRows);
        Assert.Equal(DatabasePlanOperation.SequentialScan, scan.Operation); Assert.Equal("model_config", scan.Expression);
        Assert.Equal("20", scan.Metrics.EstimatedRows); Assert.Equal("0/0", scan.Id);
    }

    [Fact]
    public void RawLocations_AreDistinctForIdenticalSubtreesAfterUnicode()
    {
        const string child = "{\"Node Type\":\"Seq Scan\",\"Relation Name\":\"重复表\"}";
        string raw = "[{\"evidence\":\"中文😊\",\"Plan\":{\"Node Type\":\"Append\",\"Plans\":[" + child + "," + child + "]}}]";
        var children = Assert.Single(Read(raw).Roots).Children;
        Assert.Equal(2, children.Count); Assert.NotEqual(children[0].SourceOffset, children[1].SourceOffset);
        Assert.Equal(child, raw.Substring(children[0].SourceOffset!.Value, children[0].SourceLength));
        Assert.Equal(child, raw.Substring(children[1].SourceOffset!.Value, children[1].SourceLength));
        Assert.Equal(raw.IndexOf(child, StringComparison.Ordinal), children[0].SourceOffset);
        Assert.Equal(raw.LastIndexOf(child, StringComparison.Ordinal), children[1].SourceOffset);
    }

    [Fact]
    public void UnknownFieldsAndZero_AreRetainedWithoutInferringActualAnalysis()
    {
        var node = Assert.Single(Read("{\"Plan\":{\"Node Type\":\"Future Scan\",\"Plan Rows\":0,\"Total Cost\":0.0000,\"Future evidence\":{\"mode\":\"x\"},\"Actual Rows\":42}}").Roots);
        Assert.Equal(DatabasePlanOperation.Unknown, node.Operation); Assert.Equal("Future Scan", node.ServerOperator);
        Assert.Equal("0", node.Metrics.EstimatedRows); Assert.Equal("0.0000", node.Metrics.TotalCost); Assert.Null(node.Metrics.ActualRows);
        Assert.Equal("{\"mode\":\"x\"}", node.RawFields["Future evidence"]); Assert.Equal("42", node.RawFields["Actual Rows"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Plan\":{\"Plans\":[]}}")]
    [InlineData("{\"Plan\":{\"Node Type\":\"Append\",\"Plans\":[{}]}}")]
    [InlineData("[]")]
    [InlineData("{\"Plan\":{\"Node Type\":\"Seq Scan\",\"Node Type\":\"Sort\"}}")]
    public void UnsupportedJson_RemainsCompleteRaw(string raw)
    {
        var snapshot = Read(raw); Assert.False(snapshot.HasStructure); Assert.Equal(DatabasePlanFormat.Raw, snapshot.Format); Assert.Equal(raw, snapshot.RawText);
    }

    [Fact]
    public void LargeUnsupportedFormat_IsNotTruncatedByReader()
    {
        string raw = "{\"future\":\"" + new string('界', 200000) + "\"}";
        Assert.Equal(raw, Read(raw).RawText);
    }

    private static DatabaseExecutionPlan Read(string raw)
    {
        var result = new PostgreSqlResult(1, 1, [new(0, "QUERY PLAN", "JSON", typeof(string), false)], [[raw]]);
        return PostgreSqlPlanReader.Read(new([result], [], null, TimeSpan.FromMilliseconds(26), new(true, false)), "SELECT * FROM model_config", "app");
    }
}
