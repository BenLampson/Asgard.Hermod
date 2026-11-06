using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class ObjectTreePagingTests(WpfTestFixture fixture)
{
    [Fact]
    public Task Branch_ReadsEveryPageAndRetriesWithoutShowingPartialResults() => fixture.RunAsync(async () =>
    {
        bool fail = true;
        var offsets = new List<int>();
        var server = new TestMySql
        {
            MetadataPageOverride = (_, offset) =>
            {
                offsets.Add(offset);
                if (offset == 1 && fail) throw new IOException("private connection details");
                return offset == 0
                    ? new([new(MySqlObjectKind.Database, "first", "first")], true)
                    : new([new(MySqlObjectKind.Database, "second", "second")], false);
            }
        };
        await using var session = await server.OpenAsync(TestMySql.Connection);
        using var tree = new MySqlObjectTree(session);
        await tree.LoadAsync(tree.Root);
        Assert.True(tree.Root.IsLoadFailed);
        Assert.DoesNotContain(tree.Root.Children, item => !item.IsPlaceholder);
        Assert.DoesNotContain("private connection details", tree.Root.Status);

        fail = false;
        await tree.Root.LoadMoreCommand.ExecuteAsync();
        Assert.False(tree.Root.IsLoadFailed);
        Assert.False(tree.Root.HasMore);
        Assert.Equal(new[] { "first", "second" }, tree.Root.Children.Select(item => item.Object.Name));
        Assert.Equal(new[] { 0, 1, 0, 1 }, offsets);
    });
}
