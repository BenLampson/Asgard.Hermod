using System.Net;
using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

public sealed class AliyunOssBucketRegionTests
{
    [Fact]
    public async Task ReadsLocationForTheNamedBucket()
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Equal("test-bucket.oss-cn-hangzhou.aliyuncs.com", request.RequestUri?.Host);
            Assert.Contains("location", request.RequestUri?.Query);
            return Task.FromResult(Response(HttpStatusCode.OK, "<LocationConstraint>oss-cn-chengdu</LocationConstraint>"));
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        Assert.Equal("cn-chengdu", await LookupAsync(client));
    }

    [Theory]
    [InlineData("test-bucket.oss-cn-chengdu.aliyuncs.com")]
    [InlineData("test-bucket.cn-chengdu.oss.aliyuncs.com")]
    public async Task LearnsRegionFromTheOfficialWrongEndpointResponse(string host)
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Contains("location", request.RequestUri?.Query);
            return Task.FromResult(Response(HttpStatusCode.Forbidden, WrongRegion(host)));
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        Assert.Equal("cn-chengdu", await LookupAsync(client));
    }

    [Fact]
    public async Task LocationPermissionDenied_FallsBackToFilteredListingAndMatchesTheExactName()
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            if (++calls == 1) return Task.FromResult(Response(HttpStatusCode.Forbidden, Denied));
            Assert.Equal("oss-cn-hangzhou.aliyuncs.com", request.RequestUri?.Host);
            Assert.Contains("prefix=test-bucket", request.RequestUri?.Query);
            if (calls == 3) Assert.Contains("marker=next", request.RequestUri?.Query);
            string xml = calls == 2
                ? "<ListAllMyBucketsResult><IsTruncated>true</IsTruncated><NextMarker>next</NextMarker>" +
                  "<Buckets><Bucket><Name>test-bucket-old</Name><Region>cn-beijing</Region></Bucket></Buckets></ListAllMyBucketsResult>"
                : "<ListAllMyBucketsResult><IsTruncated>false</IsTruncated>" +
                  "<Buckets><Bucket><Name>test-bucket</Name><Location>oss-cn-chengdu</Location></Bucket></Buckets></ListAllMyBucketsResult>";
            return Task.FromResult(Response(HttpStatusCode.OK, xml));
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        Assert.Equal("cn-chengdu", await LookupAsync(client));
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("test-bucket.oss-cn-chengdu.aliyuncs.com.example.com")]
    [InlineData("a-different-bucket.oss-cn-chengdu.aliyuncs.com")]
    [InlineData("test-bucket.oss-cn-chengdu.attacker.aliyuncs.com")]
    public async Task IgnoresUnrelatedEndpointHintsAndAllowsDataOnlyCredentials(string host)
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.Forbidden, ++calls == 1 ? WrongRegion(host) : Denied)));
        using var client = AliyunOssResourceTestClient.Create(handler);

        Assert.Null(await LookupAsync(client));
        Assert.Equal(2, calls);
    }

    private static Task<string?> LookupAsync(IObjectStorageClient client) =>
        Assert.IsAssignableFrom<IObjectStorageBucketRegionClient>(client)
            .FindBucketRegionAsync("test-bucket", TestContext.Current.CancellationToken);

    private const string Denied = "<Error><Code>AccessDenied</Code><Message>Permission missing</Message><RequestId>test</RequestId></Error>";

    private static string WrongRegion(string host) => "<Error><Code>AccessDenied</Code><Message>Wrong region</Message><RequestId>test</RequestId>" +
        $"<HostId>{host}</HostId><Bucket>test-bucket</Bucket><Endpoint>oss-cn-hangzhou.aliyuncs.com</Endpoint><EC>0003-00001403</EC></Error>";

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/xml") };
}
