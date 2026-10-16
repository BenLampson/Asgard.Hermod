using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.MySql;
using Xunit;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>服务器数字、缩进、未执行与原始证据的纯解析回归，不创建连接。</summary>
public sealed class MySqlPlanReaderTests
{
    [Fact]
    public void Analyze_KeepsInclusivePerLoopMetricsPrecisionAndNativeOrder()
    {
        const string raw = "-> Sort: t.updated_at DESC\r\n   (cost=0.35 rows=5) (actual time=0.221..0.241 rows=5 loops=1)\r\n   -> Filter: (t.enabled = 1) (actual time=0.061..0.118 rows=5 loops=2)\r\n      -> Table scan on t (actual time=0.052..0.083 rows=20 loops=2)";
        var snapshot = Read(raw);
        Assert.Equal(DatabasePlanFormat.Tree, snapshot.Format); Assert.Equal(raw, snapshot.RawText);
        Assert.True(snapshot.IsActual); Assert.Equal(26, snapshot.RequestElapsed.TotalMilliseconds);
        var sort = Assert.Single(snapshot.Roots); var filter = Assert.Single(sort.Children); var scan = Assert.Single(filter.Children);
        Assert.Equal("0/0/0", scan.Id); Assert.Equal(DatabasePlanOperation.TableScan, scan.Operation); Assert.Equal("t", scan.Expression);
        Assert.Equal("Table scan", scan.ServerOperator); Assert.Equal("20", scan.Metrics.ActualRows); Assert.Equal("2", scan.Metrics.Loops);
        Assert.Equal("0.083", scan.Metrics.CompletionMilliseconds); Assert.Equal("0.241", sort.Metrics.CompletionMilliseconds);
        Assert.Equal("0.35", sort.Metrics.TotalCost); Assert.Null(sort.Metrics.StartupCost);
        Assert.StartsWith("-> Table scan", raw.Substring(scan.SourceOffset!.Value, scan.SourceLength));
    }

    [Fact]
    public void NeverExecuted_AndMissingMetricsDoNotBecomeZero()
    {
        var plan = Read("-> Nested loop\n   -> Index lookup on t (never executed)\n   -> Unknown future operator");
        var loop = Assert.Single(plan.Roots); var missing = loop.Children[1];
        Assert.True(loop.Children[0].Metrics.NeverExecuted); Assert.Null(loop.Children[0].Metrics.ActualRows);
        Assert.Null(missing.Metrics.ActualRows); Assert.Null(missing.Metrics.Loops); Assert.Null(missing.Metrics.CompletionMilliseconds);
        Assert.Equal(DatabasePlanOperation.Unknown, missing.Operation); Assert.Equal("Unknown future operator", missing.FullOperation);
    }

    [Fact]
    public void ActualZero_IsPreservedAndNeverMultipliedByLoops()
    {
        var node = Assert.Single(Read("-> Filter: false (actual time=0.000..0.000 rows=0 loops=50)").Roots);
        Assert.Equal("0", node.Metrics.ActualRows); Assert.Equal("0.000", node.Metrics.CompletionMilliseconds);
        Assert.Equal("50", node.Metrics.Loops); Assert.False(node.Metrics.NeverExecuted);
    }

    [Fact]
    public void TraditionalExplain_RemainsFieldsWithoutInventedTreeOrTimes()
    {
        var result = new MySqlResult(1, 1, [new(0, "id", "INT", typeof(int), false), new(1, "table", "VARCHAR", typeof(string), true), new(2, "rows", "BIGINT", typeof(long), true)],
            [[1, "items", null], [2, "nested", 0L]]);
        var snapshot = MySqlPlanReader.Read(new([result], [], null, TimeSpan.FromMilliseconds(12), new(true, false)), "SELECT * FROM items", "app", false);
        Assert.Equal(DatabasePlanFormat.Fields, snapshot.Format); Assert.Empty(snapshot.Roots); Assert.False(snapshot.IsActual);
        Assert.Equal(new[] { "id", "table", "rows" }, snapshot.Columns); Assert.Null(snapshot.Rows[0][2]); Assert.Equal("0", snapshot.Rows[1][2]);
        Assert.Contains("id\ttable\trows", snapshot.RawText); Assert.Equal("app", snapshot.Database);
    }

    [Fact]
    public void UnknownFormat_FallsBackWithoutTruncatingLargeOutput()
    {
        string raw = "future-format " + new string('数', 200000);
        var snapshot = Read(raw);
        Assert.Equal(DatabasePlanFormat.Raw, snapshot.Format); Assert.Equal(raw, snapshot.RawText); Assert.False(snapshot.HasStructure);
    }

    [Fact]
    public void SiblingNodes_KeepTheirServerParentInsteadOfAttachingToPreviousLeaf()
    {
        var root = Assert.Single(Read("-> Nested loop\n   -> Table scan on a\n   -> Table scan on b\n      -> Filter: x").Roots);
        Assert.Equal(2, root.Children.Count); Assert.Empty(root.Children[0].Children); Assert.Single(root.Children[1].Children);
        Assert.Equal("0/1/0", root.Children[1].Children[0].Id);
    }

    private static DatabaseExecutionPlan Read(string raw)
    {
        var result = new MySqlResult(1, 1, [new(0, "EXPLAIN", "LONGTEXT", typeof(string), false)], [[raw]]);
        return MySqlPlanReader.Read(new([result], [], null, TimeSpan.FromMilliseconds(26), new(true, false)), "SELECT * FROM t", "app", true);
    }
}
