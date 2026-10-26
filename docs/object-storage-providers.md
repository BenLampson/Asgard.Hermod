# Object storage providers

Hermod includes Alibaba Cloud OSS, Tencent COS, Volcengine TOS, AWS S3, MinIO and
a generic S3-compatible connector. All use the same object browser, search,
preview, transfer and deletion workflows. Each connector is registered by the
application and can be used independently of SQLite and WPF.

Editing keeps the instance type fixed and allows switching its storage provider.
The selected provider determines the connection fields, endpoint and signing protocol.
Switching clears the previous provider's connection fields and requires a successful
new connection test before saving. The instance ID, creation time and tags are retained.

## Connection fields

| Provider | Endpoint | Default region | Notes |
| --- | --- | --- | --- |
| Alibaba Cloud OSS | Region or full OSS service endpoint | `cn-hangzhou` | Uses the official AlibabaCloud.OSS.V2 SDK; supports public, internal and dual-stack regional addresses |
| Tencent COS | Region or full COS service endpoint | `ap-guangzhou` | Regional bucket discovery; bucket names include the COS APPID suffix |
| Volcengine TOS | Region or full TOS service endpoint | `cn-beijing` | Accepts standard TOS and S3 endpoints; uses the S3-compatible protocol |
| AWS S3 | Region or full S3 service endpoint | `us-east-1` | China regions use `amazonaws.com.cn` |
| MinIO | Required custom HTTP(S) endpoint | `us-east-1` | Path addressing by default |
| S3 compatible | Required custom HTTP(S) endpoint | `us-east-1` | Path addressing by default |

For S3-based providers, enter the access key ID and secret access key explicitly.
Optional temporary credentials also require the session token. Hermod does not
read ambient AWS credentials or renew expired tokens. Credentials follow the
existing encrypted instance persistence path and are excluded from display errors.

OSS, COS, TOS and AWS offer an **Endpoint input** selector:

- **Enter region** generates the provider's service address. A blank region uses
  the default shown above.
- **Enter full endpoint** detects the signing region from an official regional
  service address, such as `https://cos.ap-shanghai.myqcloud.com` or
  `https://s3.eu-west-1.amazonaws.com`. Hostnames without a scheme use HTTPS.

Only the selected input is validated, tested and saved. Switching preserves both
drafts in the open editor and invalidates the connection test. Saving persists
`endpointMode` and the active `region` or `endpoint` field; reopening restores the
selection. MinIO and generic S3 have no region-derived service address and continue
to require their server endpoint plus an optional signing region.

For TOS address mode, paste the complete service endpoint from the console, for example
`https://tos-cn-beijing.volces.com` or `https://tos-s3-cn-beijing.volces.com`.
The connector detects the signing region and converts standard TOS addresses to
their S3 equivalents. Hostnames without a scheme use HTTPS. Public `.volces.com`
and private `.ivolces.com` endpoints are supported; scheme and port are preserved.
Do not include a bucket name or object path. Previously saved region-only values
open in region mode. Early TOS configurations with a URL in `region` open in
address mode and are converted to the current format on save. Existing OSS
credentials-only configurations retain the original `cn-hangzhou` default.

The optional bucket field restricts the instance to one bucket and avoids
account-wide bucket listing. Use the bucket's region. Without a fixed bucket,
AWS discovers regions and COS lists buckets in the configured region; create
separate COS instances for other regions as needed.

Custom endpoints must be root HTTP(S) URLs, optionally with a port, such as
`http://localhost:9000` or `https://storage.example.com`. Do not include a bucket,
path prefix, credentials, query or fragment. Leave addressing style blank or use
`path` for `endpoint/bucket`; use `virtual` for `bucket.endpoint` when the server's
DNS and TLS configuration support it. Set the region configured on your server.

## Operations and permissions

The recursive modification-time scan offers **Newest first** and **Oldest first**.
Changing the direction after a complete scan reorders the loaded rows without
another request. Folders stay first and unknown timestamps stay last in each group.

Use **Current folder properties** or a folder's **Folder properties** menu to
calculate its total size, file count, subfolder count and latest file modification
time. The dialog warns before starting: recursive listing can take a long time
and incur listing request charges. It shows an indeterminate progress bar with
running counts and size, and supports cancellation or closing the window.
Only a complete scan exposes final properties. It reads listing metadata without
downloading file contents or requesting each object's metadata. Counts include
implicit and empty subfolders once, exclude the selected folder itself, and
describe currently visible objects rather than historical versions. Concurrent
remote changes can affect this scan; it is not an atomic snapshot.

Supported operations include paginated bucket/object listing, object metadata,
range reads and downloads, single and multipart uploads, deletion, and temporary
GET links. Links must expire within seven days and can stop working earlier when
the signing credentials expire. Access-policy text describes the effective policy
boundary; Hermod does not infer that an object is public from a successful HEAD.

Grant only the permissions needed for the intended operations: bucket listing
(unless a fixed bucket is supplied), object listing, reading, writing, deletion,
and multipart initiation/upload/completion/abort for large files. Region discovery
may also require reading bucket location. COS/TOS uploads with overwrite disabled
require reading the bucket versioning setting.

Connection testing lists buckets or the selected bucket and, when a bucket is
available, creates, reads and deletes a random temporary probe object. On versioned
buckets, cleanup targets the returned version, including the literal `null`
version. Incomplete cleanup is reported. A denied preflight check does not trigger
a write or deletion.

Uploads up to 8 MiB use a single PUT; larger uploads use streamed sequential
multipart requests. Failed or canceled multipart transfers attempt an abort with
an independent timeout. Callers retain ownership of the upload stream. Downloads
release the SDK response when their stream is disposed.

Overwrite-disabled writes use server-side conditions, including at multipart
completion. COS/TOS also use their native forbid-overwrite header. Their native
header does not protect enabled or suspended versioning, so Hermod rejects that
combination instead of claiming an atomic no-overwrite guarantee. Generic S3
servers must implement the S3 conditional-write contract for this behavior.

## SDK boundary and verification

`Providers.S3` uses `AWSSDK.S3`; each vendor project supplies its own endpoint and
protocol profile. Small request/response adapters handle COS bucket-list names
and COS/TOS overwrite headers. Protocol code remains outside shared contracts,
object browser UI and persistence. SDK retries are disabled; the application HTTP
policy does not retry writes automatically.

Tests exercise the actual SDK marshalling, signing and response handling with
controlled HTTP responses. They cover pagination, safe failures, partial batch
deletion, cancellation, multipart abort and probe version cleanup. UI tests cover
English/Chinese, light/dark themes, scrolling, keyboard access and embedded image
decoding. These tests do not establish connectivity to a particular cloud account
or MinIO installation; account permissions, endpoint reachability and compatible
server versions must be checked against that deployment.

## Official references

- [Tencent COS GET Service and regional bucket discovery](https://intl.cloud.tencent.com/zh/document/product/436/8291)
- [Tencent COS PUT Object and forbid-overwrite behavior](https://cloud.tencent.cn/document/product/436/7749)
- [TOS access with AWS S3 SDKs](https://docs.volcengine.com/docs/TorchObjectStorage/AccessingTOSUsingAWSS3SDK?lang=zh)
- [TOS regions and endpoints](https://docs.volcengine.com/docs/TorchObjectStorage/Regionsandendpoints?lang=en)
- [TOS PUT Object](https://docs.volcengine.com/docs/TorchObjectStorage/PutObject?lang=en)
- [AWS SDK for .NET](https://github.com/aws/aws-sdk-net)

All branded images are packaged in the UI assembly and load offline. See their
[official source URLs and attribution](../src/Asgard.Hermod.UI/Assets/Providers/README.md).
