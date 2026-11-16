using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证停止和失败都不会越过阶段边界或泄露服务异常。</summary>
public sealed class ObjectStorageDeleteDialogViewModelTests
{
    /// <summary>停止获取会取消请求，忽略随后仍返回的计划。</summary>
    [Fact]
    public async Task StopCollecting_IgnoresLatePlanAndNeverDeletes()
    {
        var response = new TaskCompletionSource<ObjectStorageDeletePlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken request = default;
        bool deleted = false;
        using var model = new ObjectStorageDeleteDialogViewModel(new("bucket", "region"), "", [new("docs/", true)],
            (_, token) => { request = token; return response.Task; },
            (_, _, _) => { deleted = true; return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        var operation = model.ConfirmCommand.ExecuteAsync();
        model.DismissCommand.Execute(null);
        Assert.True(request.IsCancellationRequested);
        response.SetResult(new(["docs/a.txt"]));
        await operation;

        Assert.False(deleted);
        Assert.Equal(Text.Get("ObjectStorageDeleteCollectStopped"), model.Outcome);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    /// <summary>扫描失败不会发出删除请求；删除失败会提示潜在的部分结果。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_ExplainsAffectedStageWithoutRawException(bool failDuringDeletion)
    {
        var error = new InvalidOperationException("raw storage credentials");
        int deleteCalls = 0;
        using var model = new ObjectStorageDeleteDialogViewModel(new("bucket", "region"), "", [new("docs/", true)],
            (_, _) => failDuringDeletion ? Task.FromResult(new ObjectStorageDeletePlan(["docs/a.txt"]))
                : Task.FromException<ObjectStorageDeletePlan>(error),
            (_, _, _) => { deleteCalls++; return Task.FromException(error); },
            TestContext.Current.CancellationToken);

        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal(failDuringDeletion ? 1 : 0, deleteCalls);
        Assert.Equal(Text.Format(failDuringDeletion ? "ObjectStorageDeleteFailed" : "ObjectStorageDeleteCollectFailed",
            Text.Get("TheOperationFailedCheckTheNetworkAndAccessPermissionsThenTryAgain")), model.Outcome);
        Assert.DoesNotContain(error.Message, model.Outcome, StringComparison.Ordinal);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }
}
