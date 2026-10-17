namespace Asgard.Hermod.RabbitMq.Tests;

public sealed class ApiContractTests
{
    [Fact]
    public async Task Paging_EncodesVhostAndSearch_UsesFilteredCount()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("""{"items":[{"name":"orders"}],"page":2,"page_size":6,"filtered_count":12,"total_count":99}""")));
        await using var session = handler.Session();
        var page = await session.ReadPageAsync(RabbitMqResource.Queues, "/production", "a&b/#", 2, 6, TestContext.Current.CancellationToken);
        Assert.Equal(12, page.Total); Assert.Equal(2, page.PageCount); Assert.False(page.LocalPaging);
        Assert.Contains("/queues/%2Fproduction", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Contains("name=a%26b%2F%23", handler.Requests[0].Uri.AbsoluteUri); Assert.Contains("page_size=6", handler.Requests[0].Uri.Query);
    }
    [Fact]
    public async Task UnpagedResponses_AreHonestlyLocallyPaged_AndSecretsRemoved()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("""[{"name":"a","password_hash":"secret-hash","password":"secret"},{"name":"b"}]""")));
        await using var session = handler.Session(); var page = await session.ReadPageAsync(RabbitMqResource.Users, page: 2, pageSize: 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(page.LocalPaging); Assert.Equal("b", Assert.Single(page.Items).Name);
        page = await session.ReadPageAsync(RabbitMqResource.Users, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain("secret", page.Items[0].Data.GetRawText());
    }
    [Fact]
    public async Task DefaultExchangePublish_UsesApiAlias_AndReturnsRoutingStatus()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("""{"routed":false}""")));
        await using var session = handler.Session();
        var result = await session.ExecuteAsync(new(RabbitMqOperation.Publish, "/", "", JsonSerializer.SerializeToElement(new { properties = new { }, routing_key = "orders", payload = "", payload_encoding = "string" })), TestContext.Current.CancellationToken);
        Assert.False(result.GetProperty("routed").GetBoolean()); Assert.Equal(HttpMethod.Put, handler.Requests[0].Method);
        Assert.EndsWith("/exchanges/%2F/amq.default/publish", handler.Requests[0].Uri.AbsoluteUri);
    }
    [Fact]
    public async Task QueueDeletion_PreservesAtomicServerPreconditions()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("", HttpStatusCode.NoContent)));
        await using var session = handler.Session(); await session.ExecuteAsync(new(RabbitMqOperation.DeleteQueue, "/", "orders/a"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method); Assert.Contains("orders%2Fa", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("?if-empty=true&if-unused=true", handler.Requests[0].Uri.Query);
    }
    [Fact]
    public async Task BindingDeletion_UsesServerPropertiesKey_AndDestinationKind()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("", HttpStatusCode.NoContent)));
        await using var session = handler.Session();
        await session.ExecuteAsync(new(RabbitMqOperation.DeleteBinding, "/", "events", Destination: "next/e", DestinationType: "exchange", PropertiesKey: "key/#~hash"), TestContext.Current.CancellationToken);
        Assert.EndsWith("/bindings/%2F/e/events/e/next%2Fe/key%2F%23~hash", handler.Requests[0].Uri.AbsoluteUri);
    }
    [Theory]
    [InlineData(false, "ack_requeue_true")]
    [InlineData(true, "ack_requeue_false")]
    public async Task Get_IsExplicitSnapshot_WithBoundedBinaryPreview(bool remove, string mode)
    {
        using var handler = new RecordingHandler((request, _) => Task.FromResult(RecordingHandler.Json(request.Method == HttpMethod.Get
            ? """{"name":"q","type":"classic"}""" : """[{"payload":"AP8=","payload_encoding":"base64","payload_bytes":90000,"properties":{},"exchange":"","routing_key":"q","redelivered":true}]""")));
        await using var session = handler.Session(); var message = Assert.Single(await session.GetMessagesAsync("/", "q", 3, remove, TestContext.Current.CancellationToken));
        Assert.True(message.IsTruncated); Assert.Equal(new byte[] { 0, 255 }, message.Payload); Assert.Equal("AP8=", message.Display());
        using var body = JsonDocument.Parse(handler.Requests[1].Body); Assert.Equal(mode, body.RootElement.GetProperty("ackmode").GetString());
        Assert.Equal(65536, body.RootElement.GetProperty("truncate").GetInt32()); Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        _ = message.Display(true); Assert.Equal(2, handler.Requests.Count);
    }
    [Fact]
    public async Task StreamGet_DoesNotDispatchConsumerRequest()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("""{"type":"stream"}""")));
        await using var session = handler.Session();
        await Assert.ThrowsAnyAsync<Exception>(() => session.GetMessagesAsync("/", "stream", 1, true, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests); Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }
    [Fact]
    public async Task AdministratorWithoutVhosts_AuthenticatesSuccessfully()
    {
        using var handler = new RecordingHandler((request, _) => Task.FromResult(RecordingHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("whoami", StringComparison.Ordinal)
            ? """{"name":"admin","tags":["administrator"]}""" : request.RequestUri.AbsolutePath.EndsWith("overview", StringComparison.Ordinal) ? """{"rabbitmq_version":"4.2"}""" : "[]")));
        await using var session = handler.Session(); await session.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(session.IsAdministrator); Assert.True(session.CanManagePolicies); Assert.True(session.CanMonitor);
        Assert.Empty((await session.ReadPageAsync(RabbitMqResource.Vhosts, cancellationToken: TestContext.Current.CancellationToken)).Items);
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    public async Task Errors_AreSafe_AndNeverRetried(HttpStatusCode status, bool unknown)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("credential=do-not-show", status)));
        await using var session = handler.Session();
        var error = await Assert.ThrowsAsync<RabbitMqFailure>(() => session.ExecuteAsync(new(RabbitMqOperation.DeleteQueue, "/", "q"), TestContext.Current.CancellationToken));
        Assert.Equal(unknown, error.OutcomeUnknown); Assert.Single(handler.Requests); Assert.DoesNotContain("do-not-show", Text.Error(error));
    }
    [Fact]
    public async Task InterruptedWrite_IsUnknownAndDisconnects_NoReplay()
    {
        using var handler = new RecordingHandler((_, _) => throw new HttpRequestException("test-password"));
        await using var session = handler.Session();
        var error = await Assert.ThrowsAsync<RabbitMqFailure>(() => session.ExecuteAsync(new(RabbitMqOperation.DeleteQueue, "/", "q"), TestContext.Current.CancellationToken));
        Assert.True(error.OutcomeUnknown); Assert.False(session.IsConnected); Assert.DoesNotContain("test-password", error.ToString());
        await Assert.ThrowsAsync<RabbitMqFailure>(() => session.ExecuteAsync(new(RabbitMqOperation.DeleteQueue, "/", "q"), TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task RevokedAuthentication_DisconnectsAndStopsSubsequentRequests()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Json("", HttpStatusCode.Unauthorized)));
        await using var session = handler.Session();
        var error = await Assert.ThrowsAsync<RabbitMqFailure>(() => session.ReadPageAsync(RabbitMqResource.Queues, "/", cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(error.ConnectionLost); Assert.False(session.IsConnected);
        await Assert.ThrowsAsync<RabbitMqFailure>(() => session.ReadPageAsync(RabbitMqResource.Queues, "/", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task PreCancelledWrite_SendsNothing()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException()); await using var session = handler.Session();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ExecuteAsync(new(RabbitMqOperation.DeleteQueue, "/", "q"), new CancellationToken(true)));
        Assert.Empty(handler.Requests);
    }
}
