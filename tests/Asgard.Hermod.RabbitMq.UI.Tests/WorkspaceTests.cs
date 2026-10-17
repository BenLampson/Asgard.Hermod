namespace Asgard.Hermod.RabbitMq.UI.Tests;

[Collection(HeadlessCollection.Name)]
public sealed class WorkspaceTests(HeadlessFixture fixture)
{
    internal static RabbitMqWorkspaceViewModel Model(FakeRabbitMq server, TestDialogs dialogs)
    { var config = new TestConfigurations(); return new(new(TestConfigurations.Instance), config, server, config, dialogs) { IsActive = false }; }
    [Fact]
    public Task AdministratorWithoutBrowsePermissions_RetainsGlobalAdministration() => fixture.RunAsync(async () =>
    {
        var server = new FakeRabbitMq { NoPermissions = true }; await using var model = Model(server, new()); await model.InitializeAsync();
        Assert.True(model.HasConnectedOnce); Assert.Empty(model.Vhosts); Assert.Single(model.AdministrationVhosts);
        Assert.Equal(RabbitMqResource.Vhosts, model.Resource); Assert.True(model.CanCreate);
        await model.NavigateCommand.ExecuteAsync(model.AdministrationPages.Single(item => item.Resource == RabbitMqResource.Permissions));
        Assert.True(model.CanCreate); Assert.Single(model.Rows); Assert.True(model.HasAdministrationScope);
    });
    [Fact]
    public Task PolicyMaker_CanManagePoliciesWithoutUserAdministration() => fixture.RunAsync(async () =>
    {
        await using var model = Model(new() { IsAdministrator = false }, new()); await model.InitializeAsync();
        Assert.Single(model.AdministrationPages); Assert.Equal(RabbitMqResource.Policies, model.AdministrationPages[0].Resource);
        await model.NavigateCommand.ExecuteAsync(model.AdministrationPages[0]); Assert.True(model.CanCreate);
    });
    [Fact]
    public Task MessageView_DoesNotConsumeUntilExplicitGet_AndCopyExportOnlyPreview() => fixture.RunAsync(async () =>
    {
        var server = new FakeRabbitMq(); var dialogs = new TestDialogs(); await using var model = Model(server, dialogs); await model.InitializeAsync();
        await model.NavigateCommand.ExecuteAsync(model.Navigation.Single(item => item.Resource == RabbitMqResource.Queues)); model.SelectedRow = model.Rows[0];
        await model.MessagesCommand.ExecuteAsync(); Assert.Equal(0, server.Gets);
        await model.RefreshCommand.ExecuteAsync(); Assert.Equal(0, server.Gets);
        await model.GetCommand.ExecuteAsync(); Assert.Equal(1, server.Gets); Assert.False(server.Removed); Assert.True(model.IsTruncated);
        model.CopyCommand.Execute(null); await model.ExportCommand.ExecuteAsync();
        Assert.Equal(model.Payload, dialogs.Copied); Assert.True(dialogs.Preview); Assert.Equal(model.SelectedMessage!.Message.Payload, dialogs.Exported);
        model.MessageEncoding = "base64"; Assert.Equal(1, server.Gets);
        model.GetMode = "remove"; await model.GetCommand.ExecuteAsync(); Assert.Equal(1, server.Gets); Assert.NotNull(dialogs.Last);
        Assert.False(dialogs.Last.SubmitCommand.CanExecute(null)); dialogs.Last.Confirmation = "wrong"; Assert.False(dialogs.Last.SubmitCommand.CanExecute(null));
        dialogs.OnShow = async form => { form.Confirmation = "orders.created"; await form.SubmitCommand.ExecuteAsync(); return true; };
        await model.GetCommand.ExecuteAsync(); Assert.Equal(2, server.Gets); Assert.True(server.Removed);
    });
    [Fact]
    public Task LateRead_CannotReplaceTheNewResourcePage() => fixture.RunAsync(async () =>
    {
        var server = new FakeRabbitMq(); await using var model = Model(server, new()); await model.InitializeAsync();
        server.PendingQueues = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task read = model.NavigateCommand.ExecuteAsync(model.Navigation.Single(item => item.Resource == RabbitMqResource.Queues));
        // 导航命令串行保护自身；虚拟主机变化仍必须取消旧请求并隔离结果。
        model.SelectedVhost = null;
        server.PendingQueues.SetResult(new([FakeRabbitMq.Queue("stale")], 1, 50, 1)); await read;
        Assert.Empty(model.Rows); Assert.DoesNotContain(model.Rows, row => row.Record.Name == "stale");
    });
    [Fact]
    public Task UnknownGet_PreservesSnapshot_AndRequiresReconnectBeforeNewAttempt() => fixture.RunAsync(async () =>
    {
        var server = new FakeRabbitMq(); await using var model = Model(server, new()); await model.InitializeAsync();
        await model.NavigateCommand.ExecuteAsync(model.Navigation.Single(item => item.Resource == RabbitMqResource.Queues)); model.SelectedRow = model.Rows[0];
        await model.MessagesCommand.ExecuteAsync(); await model.GetCommand.ExecuteAsync(); string snapshot = model.Payload;
        server.Failure = new("RabbitMqOutcomeUnknown", true, true); await model.GetCommand.ExecuteAsync();
        Assert.Equal(snapshot, model.Payload); Assert.True(model.Connection.IsDisconnected); Assert.False(model.GetCommand.CanExecute(null));
        Assert.True(model.CopyCommand.CanExecute(null)); Assert.Equal(2, server.Gets);
    });
    [Fact]
    public Task FormErrors_RetainDraft_AndUnknownOutcomeBlocksDuplicateSubmit() => fixture.RunAsync(async () =>
    {
        int submissions = 0;
        var form = new RabbitMqForm(RabbitMqOperation.DeclareQueue, "/", "orders", "instance", _ =>
        { submissions++; throw new RabbitMqFailure("RabbitMqOutcomeUnknown", true); });
        form.Arguments.Add(new() { Key = "x-message-ttl", Type = "number", Value = "1000" });
        await form.SubmitCommand.ExecuteAsync(); Assert.Equal(1, submissions); Assert.True(form.OutcomeUnknown); Assert.False(form.SubmitCommand.CanExecute(null));
        Assert.Equal("orders", form.Fields.Single(item => item.Key == "name").Value); Assert.Single(form.Arguments);
        await form.SubmitCommand.ExecuteAsync(); Assert.Equal(1, submissions);
        Assert.True(form.CanClose);
    });
    [Fact]
    public Task EditingUserAndPolicy_PreservesRolesPriorityAndPassword() => fixture.RunAsync(() =>
    {
        var user = new RabbitMqForm(RabbitMqOperation.PutUser, "", "operator", "target", _ => Task.CompletedTask,
            FakeRabbitMq.Row("""{"name":"operator","tags":["administrator","monitoring"]}"""));
        var mutation = user.BuildMutation(); Assert.Equal("monitoring,administrator", mutation.Body.GetProperty("tags").GetString());
        Assert.False(mutation.Body.TryGetProperty("password", out _));
        var policy = new RabbitMqForm(RabbitMqOperation.PutPolicy, "/", "ttl", "target", _ => Task.CompletedTask,
            FakeRabbitMq.Row("""{"priority":37,"pattern":".*","apply-to":"queues","definition":{"message-ttl":1000}}"""));
        Assert.Equal(37, policy.BuildMutation().Body.GetProperty("priority").GetInt32());
        return Task.CompletedTask;
    });
    [Fact]
    public Task GrantingOwnPermission_RefreshesBrowseScopeWithoutReconnect() => fixture.RunAsync(async () =>
    {
        var server = new FakeRabbitMq { NoPermissions = true }; var dialogs = new TestDialogs();
        await using var model = Model(server, dialogs); await model.InitializeAsync();
        await model.NavigateCommand.ExecuteAsync(model.AdministrationPages.Single(item => item.Resource == RabbitMqResource.Permissions));
        dialogs.OnShow = async form =>
        {
            form.Fields.Single(item => item.Key == "name").Value = "operator";
            await form.SubmitCommand.ExecuteAsync(); server.NoPermissions = false; return true;
        };
        await model.CreateCommand.ExecuteAsync(); Assert.Equal("/production", Assert.Single(model.Vhosts));
        Assert.Equal(RabbitMqResource.Permissions, model.Resource);
    });
    [Fact]
    public Task TypedArguments_AndQueueTypeConstraints_ArePreserved() => fixture.RunAsync(() =>
    {
        var form = new RabbitMqForm(RabbitMqOperation.DeclareQueue, "/", "events", "target", _ => Task.CompletedTask);
        form.Fields.Single(item => item.Key == "type").Value = "quorum";
        var mutation = form.BuildMutation(); Assert.True(mutation.Body.GetProperty("durable").GetBoolean()); Assert.False(mutation.Body.GetProperty("auto_delete").GetBoolean());
        form.Arguments.Add(new() { Key = "x-message-ttl", Type = "number", Value = "123.5" });
        Assert.Equal(123.5m, form.BuildMutation().Body.GetProperty("arguments").GetProperty("x-message-ttl").GetDecimal());
        form.Arguments.Add(new() { Key = "x-message-ttl", Type = "string", Value = "duplicate" }); Assert.ThrowsAny<Exception>(() => form.BuildMutation());
        return Task.CompletedTask;
    });
}
