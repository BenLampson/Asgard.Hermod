using System.Globalization;
using System.Text;
using System.Text.Json;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>验证模型边界、现有会话复用、用户授权和本地对话生命周期。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MySqlAssistantTests(WpfTestFixture fixture)
{
    [Fact]
    public Task Send_ReusesOpenedSessionAndExcludesResultsWithoutExecutingSuggestion() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = Query(server, new());
        tab.Sql = "SELECT * FROM users"; await tab.ExecuteAsync(tab.Sql);
        var client = new Model(); using var assistant = new MySqlAssistantViewModel(tab, client) { Prompt = "Find names" };
        await assistant.SendCommand.ExecuteAsync();

        Assert.Null(assistant.SendCommand.LastException); Assert.Equal("", assistant.Error);
        Assert.Equal(1, server.Opens); Assert.Single(server.ExecutedSql); Assert.Equal(2, assistant.Messages.Count);
        Assert.Equal("SELECT name FROM users LIMIT 20;", Assert.Single(assistant.Messages[1].SqlBlocks).Sql);
        using var request = Request(client);
        var context = request.RootElement.GetProperty("context");
        Assert.Equal("app", context.GetProperty("database").GetString());
        Assert.Contains("users", context.GetProperty("schema").GetString());
        Assert.Equal(tab.Sql, context.GetProperty("sql").GetString()); Assert.Equal("", context.GetProperty("results").GetString());
        Assert.DoesNotContain("Alice", client.Requests[0][^1].Content); Assert.Equal("", assistant.Prompt);
    });

    [Fact]
    public Task ExplicitResultSharing_IsBoundedAndNeverRequeries() => fixture.RunOffscreenAsync(async () =>
    {
        var columns = Enumerable.Range(0, 25).Select(index => new MySqlColumn(index, "field_" + index, "TEXT", typeof(string), true)).ToArray();
        var rows = Enumerable.Range(0, 30).Select(_ => Enumerable.Range(0, 25).Select(_ => (object?)new string('x', 1024)).ToArray()).ToArray();
        var server = new TestMySql { ResultOverride = new(1, 1, columns, rows) }; await using var tab = Query(server, new());
        await tab.ExecuteAsync("SELECT * FROM users");
        var client = new Model(); using var assistant = new MySqlAssistantViewModel(tab, client) { Prompt = "Inspect this sample", IncludeResults = true, IncludeSql = false };
        await assistant.SendCommand.ExecuteAsync();

        Assert.Equal("", assistant.Error); Assert.Single(server.ExecutedSql); Assert.Equal(1, server.Opens);
        using var request = Request(client); var context = request.RootElement.GetProperty("context");
        Assert.Equal("", context.GetProperty("sql").GetString());
        string sample = context.GetProperty("results").GetString()!; Assert.InRange(Encoding.UTF8.GetByteCount(sample), 1, 8192);
        using var result = JsonDocument.Parse(sample);
        Assert.True(result.RootElement.GetProperty("sample").GetBoolean());
        Assert.Equal(16, result.RootElement.GetProperty("columns").GetArrayLength());
        var sampleRows = result.RootElement.GetProperty("rows"); Assert.InRange(sampleRows.GetArrayLength(), 1, 20);
        foreach (var row in sampleRows.EnumerateArray())
        { Assert.Equal(16, row.GetArrayLength()); foreach (var cell in row.EnumerateArray()) Assert.InRange(cell.GetString()!.Length, 0, 256); }
    });

    [Fact]
    public Task ExplicitResultSharing_UsesBaselineAndDoesNotExposeUnsavedGridDraft() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = Query(server, new()); await tab.ExecuteAsync("SELECT * FROM users");
        var grid = tab.SelectedResult!; var row = grid.Rows[0];
        Assert.True(await tab.PrepareCellAsync(grid, row, row.Cells[1])); row.Cells[1].Apply("unsaved-private-draft");
        var client = new Model(); using var assistant = new MySqlAssistantViewModel(tab, client) { Prompt = "Inspect sample", IncludeResults = true };
        await assistant.SendCommand.ExecuteAsync();
        using var request = Request(client); string sample = request.RootElement.GetProperty("context").GetProperty("results").GetString()!;
        Assert.Contains("Alice", sample); Assert.DoesNotContain("unsaved-private-draft", sample); Assert.True(tab.HasDataDraft); Assert.Equal(0, server.Writes);
    });

    [Fact]
    public Task Insert_UsesExistingEditorSelectionAndKeepsNativeUndo() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = Query(server, new()); tab.Sql = "old draft";
        using var assistant = new MySqlAssistantViewModel(tab, new Model()) { Prompt = "Write SQL" };
        await assistant.SendCommand.ExecuteAsync();
        var view = new MySqlTabView { DataContext = tab }; var root = Root(view); Layout(root, 780, 680);
        var editor = ((DatabaseSqlWorkspace)view.FindName("SqlWorkspace")).Editor; var input = (TextBox)editor.FindName("Input");
        input.Select(0, input.Text.Length);
        var suggestion = Assert.Single(assistant.Messages[1].SqlBlocks); assistant.InsertCommand.Execute(suggestion);
        Assert.Equal(suggestion.Sql, tab.Sql); Assert.Empty(server.ExecutedSql); Assert.True(input.CanUndo);
        input.Undo(); Assert.Equal("old draft", tab.Sql); Assert.Empty(server.ExecutedSql);
    });

    [Fact]
    public Task Execute_RequiresConfirmationOfExactSqlAndDatabase() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs(); await using var tab = Query(server, dialogs);
        using var assistant = new MySqlAssistantViewModel(tab, new Model()) { Prompt = "Suggest a query" }; await assistant.SendCommand.ExecuteAsync();
        var suggestion = Assert.Single(assistant.Messages[1].SqlBlocks);
        await assistant.ExecuteCommand.ExecuteAsync(suggestion);
        Assert.Equal((suggestion.Sql, "app"), Assert.Single(dialogs.AssistantSqlConfirmations)); Assert.Empty(server.ExecutedSql);
        dialogs.AssistantSqlAllowed = true; await assistant.ExecuteCommand.ExecuteAsync(suggestion);
        Assert.Equal(2, dialogs.AssistantSqlConfirmations.Count); Assert.Equal((suggestion.Sql, "app"), Assert.Single(server.ExecutedSql)); Assert.Equal(1, server.Opens);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Execute_RejectsSuggestionsAfterChangingDatabaseOrReconnecting(bool reconnect) => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs { AssistantSqlAllowed = true }; await using var tab = Query(server, dialogs);
        using var assistant = new MySqlAssistantViewModel(tab, new Model()) { Prompt = "Suggest a query" }; await assistant.SendCommand.ExecuteAsync();
        var oldSuggestion = Assert.Single(assistant.Messages[1].SqlBlocks);
        if (reconnect)
        {
            await tab.DisconnectSessionAsync(); tab.RebindConnection(TestMySql.Connection);
            assistant.Prompt = "Refresh context"; await assistant.SendCommand.ExecuteAsync(); Assert.Equal(2, server.Opens);
        }
        else await tab.ChangeDatabaseAsync("reporting");
        await assistant.ExecuteCommand.ExecuteAsync(oldSuggestion);
        Assert.Empty(dialogs.AssistantSqlConfirmations); Assert.Empty(server.ExecutedSql); Assert.True(tab.HasError);
    });

    [Fact]
    public Task Cancel_PreservesSuccessfulMessagesAndPromptThenDisposeClearsAndCancels() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = Query(server, new()); var client = new Model();
        using var assistant = new MySqlAssistantViewModel(tab, client) { Prompt = "First question" }; await assistant.SendCommand.ExecuteAsync();
        Assert.Equal(2, assistant.Messages.Count);
        client.Block(); assistant.Prompt = "Unsent follow-up";
        var cancelled = assistant.SendCommand.ExecuteAsync(); await client.Entered.Task;
        Assert.True(assistant.IsBusy); assistant.CancelCommand.Execute(null); await cancelled;
        Assert.True(client.LastToken.IsCancellationRequested); Assert.False(assistant.IsBusy); Assert.Equal(2, assistant.Messages.Count); Assert.Equal("Unsent follow-up", assistant.Prompt);

        client.Block(); var disposed = assistant.SendCommand.ExecuteAsync(); await client.Entered.Task;
        assistant.Dispose(); await disposed;
        Assert.True(client.LastToken.IsCancellationRequested); Assert.Empty(assistant.Messages); Assert.Equal("", assistant.Prompt); Assert.False(assistant.IsBusy);
        Assert.False(assistant.SendCommand.CanExecute(null)); Assert.False(assistant.ExecuteCommand.CanExecute(null)); Assert.Empty(server.ExecutedSql);
    });

    [Theory]
    [InlineData("en", UiTheme.Light, 360)]
    [InlineData("zh-Hans", UiTheme.Dark, 440)]
    public Task AssistantView_UsesLocalizedControlsAndHasNoBindingErrors(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var server = new TestMySql(); await using var tab = Query(server, new());
            using var assistant = new MySqlAssistantViewModel(tab, new Model()) { Prompt = "Show users" }; await assistant.SendCommand.ExecuteAsync();
            using var bindings = new WpfBindingTrace();
            var view = new MySqlAssistantView { DataContext = assistant }; var root = Root(view); Layout(root, width, 760);
            await WpfTestFixture.DrainDispatcherAsync(); Layout(root, width, 760);
            var buttons = Descendants<Button>(root).ToArray();
            Assert.Contains(buttons, button => System.Windows.Automation.AutomationProperties.GetName(button) == Text.Get("AgentSend") && ReferenceEquals(button.Command, assistant.SendCommand));
            Assert.Contains(buttons, button => Equals(button.Content, Text.Get("MySqlAssistantInsert")) && ReferenceEquals(button.Command, assistant.InsertCommand));
            Assert.Contains(buttons, button => Equals(button.Content, Text.Get("MySqlAssistantExecute")) && ReferenceEquals(button.Command, assistant.ExecuteCommand));
            var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            Assert.True(prompt.ActualWidth > 200); Assert.True(prompt.ActualHeight >= 48);
            Assert.True(prompt.TranslatePoint(new(), root).Y + prompt.ActualHeight <= root.ActualHeight + 1);
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private static MySqlTabViewModel Query(TestMySql server, TestMySqlDialogs dialogs) => new("Query", "app", MySqlObjectKind.Root, server, TestMySql.Connection, dialogs);
    private static JsonDocument Request(Model client) => JsonDocument.Parse(client.Requests[^1][^1].Content);
    private static Border Root(UIElement content)
    { var root = new Border { Child = content }; root.Resources.MergedDictionaries.Add(Application.Current.Resources); return root; }
    private static void Layout(FrameworkElement root, double width, double height)
    { root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    /// <summary>模型边界仅记录请求和取消，不访问网络或数据库。</summary>
    private sealed class Model : IAgentClient
    {
        internal List<IReadOnlyList<AgentMessage>> Requests { get; } = [];
        internal CancellationToken LastToken;
        internal TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _blocked;
        internal void Block() { _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously); Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public async Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToArray()); LastToken = cancellationToken; Entered.TrySetResult();
            if (_blocked is not null) await _blocked.Task.WaitAsync(cancellationToken);
            return AgentReply.FromContent("Here is a bounded query.\n```sql\nSELECT name FROM users LIMIT 20;\n```");
        }
    }
}
