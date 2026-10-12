using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>按 OSS 官方 V4 规范计算签名；当前仅用于固定的 ListBuckets 只读请求。</summary>
public static class AliyunOssV4Signer
{
    /// <summary>固定产品和派生步骤来自官方规范，时间戳和规范请求可由官方向量验证。</summary>
    /// <remarks>规范：https://www.alibabacloud.com/help/en/oss/developer-reference/recommend-to-use-signature-version-4 。</remarks>
    public static string CalculateSignature(string canonicalRequest, string timestamp, string region, string accessKeySecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(timestamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeySecret);
        string date = timestamp[..8];
        string scope = $"{date}/{region}/oss/aliyun_v4_request";
        string requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
        string stringToSign = $"OSS4-HMAC-SHA256\n{timestamp}\n{scope}\n{requestHash}";
        byte[] key = Encoding.UTF8.GetBytes("aliyun_v4" + accessKeySecret);
        try
        {
            foreach (string component in new[] { date, region, "oss", "aliyun_v4_request" })
            {
                byte[] nextKey = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(component));
                CryptographicOperations.ZeroMemory(key);
                key = nextKey;
            }
            return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
