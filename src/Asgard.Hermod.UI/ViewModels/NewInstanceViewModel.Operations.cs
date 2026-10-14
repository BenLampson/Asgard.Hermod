namespace Asgard.Hermod.UI.ViewModels;

/// <summary>管理可取消的真实连接测试和实例保存，异常信息不包含第三方请求或凭据。</summary>
public sealed partial class NewInstanceViewModel
{
    private InstanceConnectionOptions? _verifiedConnection;
    /// <summary>验证当前信息快照，取消后仅保留必须告知用户的资源清理失败。</summary>
    private async Task TestConnectionAsync()
    {
        if (!CanTest)
            return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _testCancellation = cancellation;
        int revision = _configurationRevision;
        var connection = BuildConnection();
        IsTesting = true;
        TestState = InstanceConnectionTestState.Running;
        TestMessage = Text.Get("VerifyingConnectionInformationAndAccessPermissions");
        TestErrorCode = "";
        TestChecks = [];
        _verifiedConfigurationRevision = -1;
        RefreshState();
        try
        {
            var provider = _registry.GetProvider(connection.TypeId, connection.ProviderId);
            connection = await provider.PrepareConnectionAsync(connection, cancellation.Token);
            var result = await provider.TestConnectionAsync(connection, cancellation.Token);
            if (_disposed || revision != _configurationRevision || cancellation.IsCancellationRequested
                && (result.IsSuccessful || !result.RequiresAttentionAfterCancellation))
                return;
            TestState = result.IsSuccessful ? InstanceConnectionTestState.Succeeded : InstanceConnectionTestState.Failed;
            TestMessage = Redact(result.Message);
            TestErrorCode = SafeErrorCode(result.ErrorCode);
            TestChecks = result.Checks?.Select(check => new InstanceConnectionCheck(Redact(check.Name), check.IsSuccessful)).ToArray() ?? [];
            _verifiedConfigurationRevision = result.IsSuccessful ? revision : -1;
            _verifiedConnection = result.IsSuccessful ? connection : null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_disposed && revision == _configurationRevision)
            {
                TestState = InstanceConnectionTestState.NotStarted;
                TestMessage = Text.Get("TestCanceledYouCanStartAgain");
            }
        }
        catch (Exception)
        {
            // 连接器实现可能抛出带请求细节的异常，界面只显示固定的安全说明。
            if (!_disposed && revision == _configurationRevision && !cancellation.IsCancellationRequested)
            {
                TestState = InstanceConnectionTestState.Failed;
                TestMessage = Text.Get("ConnectionTestFailedCheckTheConnectionInformationOrNetworkAndTryAgain");
            }
        }
        finally
        {
            if (ReferenceEquals(_testCancellation, cancellation))
                _testCancellation = null;
            IsTesting = false;
            RefreshState();
        }
    }

    /// <summary>当前测试被取消后不能解锁下一步，允许等待结束后重新开始。</summary>
    private void CancelTest()
    {
        _testCancellation?.Cancel();
        _verifiedConfigurationRevision = -1;
        TestState = InstanceConnectionTestState.NotStarted;
        TestMessage = Text.Get("TestCanceledYouCanStartAgain");
        TestErrorCode = "";
        TestChecks = [];
        RefreshState();
    }

    /// <summary>只保存当前已验证的信息，失败保留填写内容，成功才请求宿主关闭。</summary>
    private async Task SubmitAsync()
    {
        if (!CanCreate)
            return;
        var draft = new InstanceConfigurationDraft
        {
            Name = Name.Trim(),
            Connection = _verifiedConnection ?? BuildConnection(),
            SaveLocation = SaveLocation,
            ExpectedAccount = _initialAccount,
            CheckExpectedAccount = _accounts is not null,
            TagIds = TagSelection.SelectedIds,
            ReplaceTags = TagSelection.HasCatalog,
        };
        IsSaving = true;
        SaveError = "";
        RefreshState();
        try
        {
            // 配置与已确认的稳定标签标识一起提交，不从显示路径反推关联。
            var instance = _editingInstance is null
                ? await _configurations.CreateAsync(draft, _lifetimeCancellation.Token)
                : await _configurations.UpdateAsync(_editingInstance.Id, draft, _lifetimeCancellation.Token);
            if (_disposed)
                return;
            CreatedInstance = instance;
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // 宿主释放模型后不会继续保存，也不会将迟到结果当作用户确认。
        }
        catch (Exception error)
        {
            if (!_disposed)
                SaveError = Text.Error(error, IsEditing ? "SaveFailedTryAgainYourEntriesHaveBeenRetained" : "CreationFailedTryAgainYourEntriesHaveBeenRetained");
        }
        finally
        {
            IsSaving = false;
            RefreshState();
        }
        if (CreatedInstance is not null && !_disposed)
        {
            Dispose();
            CloseRequested?.Invoke(this, true);
        }
    }

    /// <summary>取消向导先终止异步工作，再通知宿主返回取消结果。</summary>
    private void Cancel()
    {
        Dispose();
        CloseRequested?.Invoke(this, false);
    }

    /// <summary>类型、服务和当前声明字段一起形成连接快照，不假设任何凭据名称。</summary>
    private InstanceConnectionOptions BuildConnection() => new()
    {
        TypeId = SelectedType!.Id,
        ProviderId = SelectedProvider!.Id,
        Parameters = Fields.Where(field => field.IsVisible).ToDictionary(field => field.Key,
            field => field.Definition.PreserveWhitespace ? field.Value : field.Value.Trim(), StringComparer.Ordinal),
    };

    /// <summary>连接器说明即使意外包含填写的凭据，也不会在界面上暴露。</summary>
    private string Redact(string text)
    {
        foreach (string credential in Fields.Where(field => field.IsSensitive).Select(field => field.Value.Trim())
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
            text = text.Replace(credential, "••••••••", StringComparison.Ordinal);
        return text;
    }

    /// <summary>仅展示有限长度的标准错误码，避免将服务商原始响应作为错误码输出。</summary>
    private string SafeErrorCode(string? code) => code is { Length: > 0 and <= 80 }
        && code.All(character => char.IsAsciiLetterOrDigit(character) || "._:-".Contains(character)) ? Redact(code) : "";
}
