using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.Tests;

/// <summary>编辑器真实保存、连接测试失效和密钥清除均不访问外部模型。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentModelEditorHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task StaleListRowsCannotEditDeleteOrSelectDefaultsAfterOwnerChanges() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); using var list = new AgentModelsViewModel(runtime.Catalog, runtime.Client);
        var row = list.Rows[1]; AgentModelEditRequest? request = null; list.EditRequested += (_, value) => request = value;
        await runtime.Accounts.SignInAsync(TestContext.Current.CancellationToken);
        await list.DefaultCommand.ExecuteAsync(row); Assert.NotEmpty(list.Error); Assert.Equal(AgentModelTestRuntime.First.Id, runtime.Catalog.Current.DefaultProfileId);
        list.DeleteCommand.Execute(row); await list.ConfirmDeleteCommand.ExecuteAsync(); Assert.Equal(3, runtime.Catalog.Current.Profiles.Count);
        list.EditCommand.Execute(row); Assert.NotNull(request);
        using var editor = list.CreateEditor(request.Profile, request.Epoch); editor.Name = "Wrong account edit";
        await editor.SaveCommand.ExecuteAsync(); Assert.NotEmpty(editor.Error);
        Assert.Equal(AgentModelTestRuntime.Second.Name, runtime.Catalog.Current.Profiles[1].Name);
    });

    [Fact]
    public Task DeletedProfileEditorCannotTestSavedCredentialsAgain() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); using var editor = new AgentModelEditorViewModel(runtime.Catalog, runtime.Client, AgentModelTestRuntime.First);
        await runtime.Catalog.DeleteAsync(AgentModelTestRuntime.First, runtime.Catalog.Epoch);
        await editor.TestCommand.ExecuteAsync(); Assert.Empty(runtime.Client.Captured); Assert.False(editor.TestSucceeded); Assert.NotEmpty(editor.TestResult);
    });

    [Fact]
    public Task BlankKeyRetainsOnlySameEndpointAndExplicitClearSurvivesCancel() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime();
        using (var editor = new AgentModelEditorViewModel(runtime.Catalog, runtime.Client, AgentModelTestRuntime.First))
        {
            Assert.Empty(editor.ApiKey); editor.Name = "Renamed"; await editor.SaveCommand.ExecuteAsync(); Assert.Empty(editor.Error);
            Assert.Equal(AgentModelTestRuntime.First.ApiKey, runtime.Store.Load()!.Profiles.Single(profile => profile.Id == AgentModelTestRuntime.First.Id).ApiKey);
        }
        var original = runtime.Catalog.Current.Profiles.Single(profile => profile.Id == AgentModelTestRuntime.First.Id);
        using (var editor = new AgentModelEditorViewModel(runtime.Catalog, runtime.Client, original))
        {
            editor.Endpoint = "https://new.example/v1"; await editor.SaveCommand.ExecuteAsync(); Assert.Empty(editor.Error);
            var changed = runtime.Catalog.Current.Profiles.Single(profile => profile.Id == original.Id); Assert.Empty(changed.ApiKey);
        }
        using (var editor = new AgentModelEditorViewModel(runtime.Catalog, runtime.Client, AgentModelTestRuntime.Second))
        {
            await editor.ClearKeyCommand.ExecuteAsync(); editor.CancelCommand.Execute(null);
            Assert.Empty(runtime.Catalog.Current.Profiles.Single(profile => profile.Id == AgentModelTestRuntime.Second.Id).ApiKey);
            Assert.Empty(editor.ApiKey);
        }
    });

    [Fact]
    public Task AnyEditInvalidatesRealConnectionTestAndLateResultsCannotReturn() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); using var editor = new AgentModelEditorViewModel(runtime.Catalog, runtime.Client, AgentModelTestRuntime.First);
        runtime.Client.Handler = async token => { await Task.Delay(30, token); return AgentReply.FromContent("OK"); };
        await editor.TestCommand.ExecuteAsync(); Assert.NotEmpty(editor.TestResult); Assert.Single(runtime.Client.Captured);
        Assert.Equal(AgentModelTestRuntime.First.ApiKey, runtime.Client.Captured[0].Options.ApiKey);
        editor.IsDefault = !editor.IsDefault; Assert.Empty(editor.TestResult);
        var late = new TaskCompletionSource<AgentReply>(TaskCreationOptions.RunContinuationsAsynchronously); runtime.Client.Handler = _ => late.Task;
        var testing = editor.TestCommand.ExecuteAsync(); editor.Name = "Changed during test";
        await testing.WaitAsync(TimeSpan.FromSeconds(2)); late.SetResult(AgentReply.FromContent("late")); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Empty(editor.TestResult); Assert.False(editor.IsTesting); Assert.Empty(editor.ApiKey);
    });

    /// <summary>测试使用编辑草稿，现有对话在保存后才读取更新后的地址、模型和密钥。</summary>
    [Fact]
    public Task TestedDraftReachesExistingConversationOnlyAfterSave() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); using var session = runtime.Client.CreateSession();
        using var editor = new AgentModelEditorViewModel(runtime.Catalog, runtime.Client, AgentModelTestRuntime.First)
        {
            Endpoint = "https://replacement.example/v1",
            Model = "replacement-model",
            ApiKey = "replacement-key"
        };
        await editor.TestCommand.ExecuteAsync(); Assert.True(editor.TestSucceeded);
        var tested = Assert.Single(runtime.Client.Captured).Options;
        using (session.BeginTask(TestContext.Current.CancellationToken))
        {
            var existing = runtime.Client.Captured[1].Options;
            Assert.Equal(AgentModelTestRuntime.First.Endpoint, existing.Endpoint);
            Assert.Equal(AgentModelTestRuntime.First.Model, existing.Model);
            Assert.Equal(AgentModelTestRuntime.First.ApiKey, existing.ApiKey);
        }
        await editor.SaveCommand.ExecuteAsync(); Assert.Empty(editor.Error);
        using (session.BeginTask(TestContext.Current.CancellationToken))
        {
            var updated = runtime.Client.Captured[2].Options;
            Assert.Equal(tested.Endpoint, updated.Endpoint); Assert.Equal(tested.Model, updated.Model); Assert.Equal(tested.ApiKey, updated.ApiKey);
        }
    });
}
