using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.S3.Model;

namespace Asgard.Hermod.Providers.S3;

/// <summary>兼容服务列桶参数在签名前改名，签名仍完全由官方 SDK 生成。</summary>
internal sealed class S3BucketListRequestHandler(S3BucketListDialect dialect) : PipelineHandler
{
    /// <inheritdoc />
    public override Task<T> InvokeAsync<T>(IExecutionContext context)
    {
        if (context.RequestContext.OriginalRequest is ListBucketsRequest)
        {
            var parameters = context.RequestContext.Request.Parameters;
            Rename("continuation-token", dialect.CursorParameter);
            Rename("max-buckets", dialect.PageSizeParameter);
            void Rename(string source, string target)
            {
                if (source != target && parameters.Remove(source, out string? value)) parameters[target] = value;
            }
        }
        return base.InvokeAsync<T>(context);
    }
}
