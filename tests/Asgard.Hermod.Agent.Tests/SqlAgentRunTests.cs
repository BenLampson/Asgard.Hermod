using System.Text;
using System.Text.Json;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>自动查询闭环只接受受限工具，结果有界且不进入后续任务的历史。</summary>
public sealed class SqlAgentRunTests
{
    /// <summary>真实工具关联与结果返回模型，下一轮只保留请求和最终分析。</summary>
    [Fact]
    public async Task QueryResultsFeedAnalysisWithoutLeakingOldSamples()
    {
        var client = new Client([Query("one", "app", "SELECT 1"), AgentReply.FromContent("Analyzed"), AgentReply.FromContent("Next")]);
        var conversation = new AgentConversation(client);
        int executions = 0;
        await conversation.RunAsync("analyze", new("app", "", ""), (query, _) => { executions++; Assert.Equal("SELECT 1", query.Sql); return Task.FromResult(new SqlAgentResult("completed", "private-result-marker")); }, _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(1, executions);
        Assert.Equal("one", client.Requests[1].Single(message => message.Role == AgentRole.Tool).ToolCallId);
        Assert.Contains("private-result-marker", client.Requests[1].Single(message => message.Role == AgentRole.Tool).Content);
        await conversation.SendAsync("next", new("app", "", ""), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("private-result-marker", string.Join("", client.Requests[2].Select(message => message.Content)));
        Assert.Contains("Analyzed", string.Join("", client.Requests[2].Select(message => message.Content)));
    }

    /// <summary>越界数据库、写入、事务控制和文件输出在执行委托之前即被拒绝。</summary>
    [Theory]
    [InlineData("other", "SELECT 1")]
    [InlineData("app", "DELETE FROM users")]
    [InlineData("app", "COMMIT")]
    [InlineData("app", "SELECT 1 INTO OUTFILE '/tmp/file'")]
    [InlineData("app", "SELECT 1 /*!; COMMIT; DELETE FROM users */")]
    [InlineData("app", "SELECT pg_read_file('/etc/passwd')")]
    [InlineData("app", "SELECT * FROM dblink('remote', 'UPDATE users SET id=1 RETURNING id') AS t(id int)")]
    public async Task UnsafeToolsNeverReachExecutor(string database, string sql)
    {
        var conversation = new AgentConversation(new Client([Query("one", database, sql)]));
        int executions = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversation.RunAsync("analyze", new("app", "", ""), (_, _) => { executions++; return Task.FromResult(new SqlAgentResult("completed", "")); }, _ => { }, TestContext.Current.CancellationToken));
        Assert.Equal(0, executions);
        Assert.Empty(conversation.Messages);
    }

    /// <summary>未知结果后不开放新工具，也不自动重试同一个查询。</summary>
    [Fact]
    public async Task UnknownOutcomeDisablesFurtherQueries()
    {
        var client = new Client([Query("one", "app", "SELECT 1"), AgentReply.FromContent("Outcome uncertain")]);
        await new AgentConversation(client).RunAsync("analyze", new("app", "", ""), (_, _) => Task.FromResult(new SqlAgentResult("unknown", "safe failure", true)), _ => { }, TestContext.Current.CancellationToken);
        Assert.Empty(client.Tools[1]);
        Assert.Equal(2, client.Requests.Count);
    }

    /// <summary>每次任务最多四个查询，模型在预算耗尽后只能总结已有证据。</summary>
    [Fact]
    public async Task QueryBudgetIsBounded()
    {
        var client = new Client(Enumerable.Range(0, 4).Select(index => Query(index.ToString(), "app", "SELECT 1")).Append(AgentReply.FromContent("Summary")).ToArray());
        int queries = 0;
        await new AgentConversation(client).RunAsync("analyze", new("app", "", ""), (_, _) => { queries++; return Task.FromResult(new SqlAgentResult("completed", "[]")); }, _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(4, queries);
        Assert.Empty(client.Tools[4]);
    }

    /// <summary>宽表、多字节值、二进制及截断结果不突破发送上限。</summary>
    [Fact]
    public void ResultSamplesAreBoundedAndMarkIncompleteData()
    {
        var rows = Enumerable.Range(0, 100).Select(_ => new object?[] { new string('中', 2000), new byte[] { 1, 2 } }).ToArray();
        string sample = SqlAgentSample.Create(["name", "payload"], rows, false);
        Assert.True(Encoding.UTF8.GetByteCount(sample) <= 8000);
        using var json = JsonDocument.Parse(sample);
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(json.RootElement.GetProperty("rows").GetArrayLength() <= 20);
        ReadOnlySqlQuery.Validate("WITH x AS (SELECT 1) SELECT * FROM x");
    }

    private static AgentReply Query(string id, string database, string sql) => new("", []) { ToolCalls = [new(id, "sql_query", JsonSerializer.Serialize(new { database, sql }))] };

    /// <summary>记录协议请求，不连接模型或数据库。</summary>
    private sealed class Client(IReadOnlyList<AgentReply> replies) : IAgentClient
    {
        internal List<AgentMessage[]> Requests { get; } = [];
        internal List<AgentToolDefinition[]> Tools { get; } = [];
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => CompleteWithToolsAsync(messages, [], cancellationToken);
        public Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default)
        { Requests.Add(messages.ToArray()); Tools.Add(tools.ToArray()); return Task.FromResult(replies[Requests.Count - 1]); }
    }
}
