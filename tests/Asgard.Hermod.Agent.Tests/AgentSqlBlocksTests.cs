using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

public sealed class AgentSqlBlocksTests
{
    [Fact]
    public void SqlBlocks_ExtractsOnlyCompleteExplicitSqlFences()
    {
        var blocks = AgentSqlBlocks.Extract("Explanation\n```sql\nSELECT 1;\n```\n~~~MySQL\nSELECT 2;\n~~~\n```json\n{}\n```\n```sql\nSELECT unfinished");
        Assert.Equal(["SELECT 1;", "SELECT 2;"], blocks);
    }

    [Fact]
    public void SqlBlocks_DoesNotTreatNestedFenceInsideOtherLanguageAsSql()
    {
        var blocks = AgentSqlBlocks.Extract("````text\n```sql\nSELECT 1;\n```\n````\n```sql\nSELECT 2;\n```");
        Assert.Equal("SELECT 2;", Assert.Single(blocks));
    }

    [Fact]
    public void SqlBlocks_DoesNotExecuteOrClassifyWriteStatements()
    {
        var blocks = AgentSqlBlocks.Extract("```sql\nDROP TABLE t;\n```");
        Assert.Equal("DROP TABLE t;", Assert.Single(blocks));
        Assert.Empty(AgentSqlBlocks.Extract("SELECT 1;"));
    }
}
