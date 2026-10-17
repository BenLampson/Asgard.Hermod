using System.Text;

namespace Asgard.Hermod.ObjectStorage.Clients;

/// <summary>在全局取消期限内寻找实际可完成写入、回读和清理的容器，不依赖具体云 SDK。</summary>
public sealed class ObjectStorageConnectionProbe
{
    /// <summary>临时对象放入独立前缀，名称和内容均不含用户凭据或实例配置。</summary>
    public const string ObjectPrefix = ".hermod-connection-test/";
    /// <summary>通过第三方接口创建连接，其他对象存储可以复用同一验证流程。</summary>
    private readonly IObjectStorageProvider _provider;
    /// <summary>清理采用独立短期限，不被用户取消或全局超时跳过。</summary>
    private readonly TimeSpan _cleanupTimeout;

    /// <summary>只允许有限清理期限，测试可注入更短期限验证清理超时。</summary>
    public ObjectStorageConnectionProbe(IObjectStorageProvider provider, TimeSpan? cleanupTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _cleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(5);
        if (_cleanupTimeout <= TimeSpan.Zero || _cleanupTimeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(cleanupTimeout));
    }

    /// <summary>完整容器列表只提供候选范围，只有具体容器的三个对象操作均成功才通过。</summary>
    public async Task<InstanceConnectionTestResult> TestAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            IReadOnlyList<ObjectStorageBucket> buckets = await ReadBucketsAsync(options, cancellationToken).ConfigureAwait(false);
            if (buckets.Count == 0)
                return Failure("no_bucket", Text.Get("IdentityAndBucketListingPassedButNoBucketIsAvailableForReadWriteVerificationCreateOrA"));
            InstanceConnectionTestResult? lastFailure = null;
            foreach (ObjectStorageBucket bucket in buckets)
            {
                if (cancellationToken.IsCancellationRequested)
                    return lastFailure is null
                        ? Failure("cancelled", Text.Get("ReadWriteConnectionTestCanceled"))
                        : lastFailure with { IsSuccessful = false, ErrorCode = "storage_probe_cancelled", Message = Text.Get("ReadWriteConnectionTestCanceledKnownTemporaryObjectsWereCleanedUp") };
                InstanceConnectionTestResult result = await VerifyBucketAsync(options, bucket, cancellationToken).ConfigureAwait(false);
                if (result.RequiresAttentionAfterCancellation) return result;
                if (cancellationToken.IsCancellationRequested)
                    return result with { IsSuccessful = false, ErrorCode = "storage_probe_cancelled", Message = Text.Get("ReadWriteConnectionTestCanceledKnownTemporaryObjectsWereCleanedUp") };
                if (result.IsSuccessful) return result;
                lastFailure = result;
            }
            return lastFailure ?? Failure("no_bucket", Text.Get("NoBucketsAreAvailableForReadWriteVerification"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, Text.Get("ReadWriteConnectionTestCanceled"), "storage_probe_cancelled", [new(Text.Get("CandidateBucketListing"), false)]);
        }
        catch (Exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return new(false, Text.Get("ReadWriteConnectionTestCanceled"), "storage_probe_cancelled", [new(Text.Get("CandidateBucketListing"), false)]);
            return new(false, Text.Get("CannotReadCandidateBucketsCheckBucketReadPermissionsTheNetworkAndInstanceConfiguratio"),
                "storage_probe_list_failed", [new(Text.Get("CandidateBucketListing"), false)]);
        }
    }

    /// <summary>容器读取使用默认连接；释放失败不会覆盖原来的安全验证结果。</summary>
    private async Task<IReadOnlyList<ObjectStorageBucket>> ReadBucketsAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken)
    {
        IObjectStorageClient? client = null;
        try
        {
            client = _provider.CreateClient(options, null);
            return await client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { DisposeClient(client); }
    }

    /// <summary>每个候选容器采用独立随机对象；未知上传结果也必须清理，明确冲突则不删除。</summary>
    private async Task<InstanceConnectionTestResult> VerifyBucketAsync(InstanceConnectionOptions options,
        ObjectStorageBucket bucket, CancellationToken cancellationToken)
    {
        string key = ObjectPrefix + Guid.NewGuid().ToString("N") + ".probe";
        byte[] expected = Encoding.UTF8.GetBytes("Hermod object storage connection probe\n" + Guid.NewGuid().ToString("N"));
        bool written = false, read = false, needsCleanup = false, cleanupFailed = false;
        string? versionId = null;
        string stage = "write";
        IObjectStorageClient? client = null;
        IObjectStorageConnectionProbeClient? probeClient = null;
        InstanceConnectionTestResult? failure = null;
        try
        {
            client = _provider.CreateClient(options, bucket.Region);
            probeClient = client as IObjectStorageConnectionProbeClient;
            if (probeClient is null)
                return Failure("unsupported", Text.Get("ThisProviderDoesNotYetOfferReadWriteTestsWithExactVersionCleanupFullConnectionCapabil"));
            cancellationToken.ThrowIfCancellationRequested();
            using var body = new MemoryStream(expected, writable: false);
            needsCleanup = true;
            ObjectStorageProbeWriteResult receipt = await probeClient.UploadProbeAsync(bucket.Name, key, body, expected.Length,
                cancellationToken).ConfigureAwait(false);
            versionId = receipt.VersionId;
            written = true;
            stage = "read";
            (bool matches, string? actualVersion) = await ReadEvidenceAsync(probeClient, bucket.Name, key, versionId,
                expected, cancellationToken).ConfigureAwait(false);
            read = matches && (versionId is null || versionId == actualVersion);
            if (!read)
                failure = Failure("content_mismatch", Text.Format("TemporaryObjectReadBackDidNotMatchInBucketReadWriteVerificationFailed", bucket.Name), written, read);
            else if (versionId is null) versionId = actualVersion;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            failure = Failure("cancelled", Text.Get("ReadWriteConnectionTestCanceledKnownTemporaryObjectsWereCleanedUp"), written, read);
        }
        catch (ObjectStorageException error)
        {
            if (!written && stage == "write" && IsDefiniteRejection(error.ErrorCode)) needsCleanup = false;
            failure = StageFailure(bucket.Name, stage, error.ErrorCode, written, read);
        }
        catch (Exception)
        {
            failure = StageFailure(bucket.Name, stage, null, written, read);
        }
        finally
        {
            if (needsCleanup && probeClient is not null)
                cleanupFailed = !await CleanupAsync(probeClient, bucket.Name, key, expected, written, versionId).ConfigureAwait(false);
            DisposeClient(client);
        }
        if (cleanupFailed)
            return new(false, Text.Format("TheTemporaryTestObjectMayRemainTheConnectionTestFailedCheckAndDeleteItInTheProviderCo", bucket.Name, key),
                "storage_probe_cleanup_failed", Checks(written, read, false))
            { RequiresAttentionAfterCancellation = true };
        if (failure is not null)
            return failure with { Checks = Checks(written, read, needsCleanup ? true : null) };
        return new(true, Text.Format("ReadWriteConnectionTestPassedRandomTemporaryObjectWriteReadBackAndCleanupWereVerified", bucket.Name),
            Checks: Checks(true, true, true));
    }

    /// <summary>最多读取预期长度加一个字节，防止错误或忽略 Range 的响应产生无限读取。</summary>
    private static async Task<(bool Matches, string? VersionId)> ReadEvidenceAsync(IObjectStorageConnectionProbeClient client,
        string bucket, string key, string? versionId, byte[] expected, CancellationToken cancellationToken)
    {
        ObjectStorageProbeReadResult evidence = await client.OpenProbeReadAsync(bucket, key, versionId, expected.Length + 1,
            cancellationToken).ConfigureAwait(false);
        await using ObjectStorageReadResult response = evidence.Content;
        byte[] actual = new byte[expected.Length + 1];
        int count = 0;
        while (count < actual.Length)
        {
            int received = await response.Body.ReadAsync(actual.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (received == 0) break;
            count += received;
        }
        return (count == expected.Length && actual.AsSpan(0, count).SequenceEqual(expected), evidence.VersionId);
    }

    /// <summary>未知上传先用随机正文确认归属，无法确认就保留位置提示，绝不盲目按键删除。</summary>
    private async Task<bool> CleanupAsync(IObjectStorageConnectionProbeClient client, string bucket, string key,
        byte[] expected, bool written, string? versionId)
    {
        using var cleanup = new CancellationTokenSource(_cleanupTimeout);
        try
        {
            if (!written)
            {
                (bool matches, string? actualVersion) = await ReadEvidenceAsync(client, bucket, key, null, expected,
                    cleanup.Token).ConfigureAwait(false);
                if (!matches) return false;
                versionId = actualVersion;
            }
            await client.DeleteProbeAsync(bucket, key, versionId, cleanup.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>明确的拒绝响应表示此次未写入，尤其不能删除禁止覆盖所保护的既有对象。</summary>
    private static bool IsDefiniteRejection(string? code) => code is "AccessDenied" or "AuthenticationFailed" or
        "InvalidCredentials" or "NoSuchBucket" or "FileAlreadyExists" or "ObjectAlreadyExists" or "PreconditionFailed" or
        "ProbeWriteRejected" or "ProbePreflightFailed";

    /// <summary>只使用已知错误码选择固定文案，不输出第三方消息或网络异常。</summary>
    private static InstanceConnectionTestResult StageFailure(string bucket, string stage, string? code, bool written, bool read)
    {
        if (code == "ProbePreflightFailed")
            return Failure("preflight_failed", Text.Format("ThePreWriteCheckFailedInBucketNoTestObjectWasCreatedCheckObjectReadPermissionsAndTheN", bucket), written, read);
        if (code is "FileAlreadyExists" or "ObjectAlreadyExists")
            return Failure("conflict", Text.Format("TheRandomTestObjectNameConflictedInBucketItWasSkippedAndNoExistingObjectWasDeleted", bucket), written, read);
        if (code == "AccessDenied")
            return Failure(stage + "_denied", Text.Format("TemporaryObjectWasDeniedInBucketCheckTheCorrespondingObjectPermissions", bucket, (stage == "write" ? Text.Get("Write") : Text.Get("ReadBack"))), written, read);
        if (code == "RequestTimeout")
            return Failure("timeout", Text.Format("TemporaryObjectTimedOutInBucketCheckYourNetworkAndTryAgain", bucket, (stage == "write" ? Text.Get("Write") : Text.Get("ReadBack"))), written, read);
        return Failure(stage + "_failed", Text.Format("TemporaryObjectFailedInBucketCheckTheNetworkCredentialsAndObjectPermissions", bucket, (stage == "write" ? Text.Get("Write") : Text.Get("ReadBack"))), written, read);
    }

    /// <summary>失败检查只呈现已执行步骤，未创建对象时不伪造清理成功。</summary>
    private static InstanceConnectionTestResult Failure(string code, string message, bool written = false, bool read = false) =>
        new(false, message, "storage_probe_" + code, Checks(written, read, null));

    /// <summary>容器列表读取已通过，随后分别记录具体对象步骤和确实执行过的清理结果。</summary>
    private static IReadOnlyList<InstanceConnectionCheck> Checks(bool written, bool read, bool? cleaned)
    {
        var checks = new List<InstanceConnectionCheck>
        {
            new(Text.Get("CandidateBucketListing"), true), new(Text.Get("TemporaryObjectWrite"), written), new(Text.Get("TemporaryObjectReadBack"), read),
        };
        if (cleaned is not null) checks.Add(new(Text.Get("TemporaryObjectCleanup"), cleaned.Value));
        return checks.AsReadOnly();
    }

    /// <summary>客户端释放属于本地清理，不将其异常或诊断信息输出到验证结果。</summary>
    private static void DisposeClient(IObjectStorageClient? client)
    {
        try { client?.Dispose(); }
        catch (Exception) { }
    }
}
