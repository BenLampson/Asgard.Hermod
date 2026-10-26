using Amazon.Runtime;
using Amazon.S3;

namespace Asgard.Hermod.Providers.S3;

/// <summary>连接器共用的 SDK 装配与表单声明；厂商通过独立模块提供协议配置。</summary>
public abstract class S3StorageProvider(S3ProviderProfile profile, IHttpClientFactory clients)
    : IStorageProvider, IObjectStorageProvider
{
    /// <inheritdoc />
    public string TypeId => "storage";
    /// <inheritdoc />
    public string ProviderId => profile.Id;
    /// <inheritdoc />
    public InstanceProviderDescriptor Descriptor { get; } = CreateDescriptor(profile);

    /// <inheritdoc />
    public InstanceConnectionOptions NormalizeConnection(InstanceConnectionOptions options) =>
        profile.CustomEndpoint ? options : ObjectStorageEndpointInput.Normalize(options);

    /// <inheritdoc />
    public IObjectStorageClient CreateClient(InstanceConnectionOptions options, string? region)
    {
        var settings = S3ConnectionSettings.Parse(profile, options, region);
        AWSCredentials credentials = settings.SessionToken is null
            ? new BasicAWSCredentials(settings.AccessKey, settings.SecretKey)
            : new SessionAWSCredentials(settings.AccessKey, settings.SecretKey, settings.SessionToken);
        var config = new AmazonS3Config
        {
            ServiceURL = settings.Endpoint.AbsoluteUri,
            AuthenticationRegion = settings.Region,
            ForcePathStyle = settings.PathStyle,
            AllowAutoRedirect = false,
            UseHttp = settings.Endpoint.Scheme == "http",
            MaxErrorRetry = 0,
            Timeout = TimeSpan.FromHours(1),
            DisableMultiregionAccessPoints = true,
            DisableS3ExpressSessionAuth = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            HttpClientFactory = new S3HttpClientFactory(clients, profile.BucketListDialect),
        };
        return new S3ObjectStorageClient(new S3SdkClient(credentials, config, profile), settings, profile);
    }

    /// <inheritdoc />
    public async Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = S3ConnectionSettings.Parse(profile, options, null);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var snapshot = new InstanceConnectionOptions
            {
                TypeId = options.TypeId,
                ProviderId = options.ProviderId,
                Parameters = new Dictionary<string, string>(options.Parameters),
            };
            var result = await new ObjectStorageConnectionProbe(this).TestAsync(snapshot, deadline.Token).ConfigureAwait(false);
            if (result.RequiresAttentionAfterCancellation) return result;
            cancellationToken.ThrowIfCancellationRequested();
            return deadline.IsCancellationRequested
                ? result with { IsSuccessful = false, ErrorCode = "RequestTimeout", Message = Text.Get("ConnectionTestTimedOutCheckTheNetworkAndTryAgain") }
                : result;
        }
        catch (ObjectStorageException error)
        {
            return new(false, Text.Error(error), error.ErrorCode);
        }
    }

    /// <summary>每个连接器实例在语言初始化后创建描述符，所有显示文本使用共享资源。</summary>
    private static InstanceProviderDescriptor CreateDescriptor(S3ProviderProfile profile)
    {
        var fields = new List<InstanceConfigurationField>();
        if (profile.CustomEndpoint)
        {
            fields.Add(new("endpoint", Text.Get("StorageEndpoint"), MaxLength: 2048, Hint: Text.Get("StorageEndpointHint")));
            fields.Add(new("region", Text.Get("StorageRegion"), IsRequired: false, MaxLength: 64,
                Hint: Text.Format("StorageRegionHint", profile.DefaultRegion)));
        }
        else
            fields.AddRange(ObjectStorageEndpointInput.CreateFields(profile.DefaultRegion, profile.EndpointHintResource is { } hint
                ? Text.Get(hint) : Text.Format("StorageCloudEndpointHint", profile.Endpoint(profile.DefaultRegion))));
        fields.Add(new("accessKeyId", Text.Get("StorageAccessKeyId"), MaxLength: 256, IsSensitive: true));
        fields.Add(new("accessKeySecret", Text.Get("StorageSecretAccessKey"), InstanceFieldKind.Secret, MaxLength: 512));
        fields.Add(new("sessionToken", Text.Get("StorageSessionToken"), InstanceFieldKind.Secret, IsRequired: false,
            MaxLength: 16384, Hint: Text.Get("StorageSessionTokenHint")));
        fields.Add(new("bucket", Text.Get("StorageOptionalBucket"), IsRequired: false, MaxLength: 255, Hint: Text.Get("StorageOptionalBucketHint")));
        if (profile.CustomEndpoint)
            fields.Add(new("addressingStyle", Text.Get("StorageAddressingStyle"), IsRequired: false, MaxLength: 7,
                Hint: Text.Get("StorageAddressingStyleHint")));
        return new(profile.Id, Text.Get(profile.NameResource), Text.Get(profile.DescriptionResource), profile.Icon,
            new("storage", Text.Get("Storage"), Text.Get("ConnectToObjectStorage"), "box"), fields.AsReadOnly())
        {
            ConnectionTestDescription = Text.Get("StorageConnectionTestHint"),
        };
    }
}
