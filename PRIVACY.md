# Asgard Hermod 隐私政策 / Privacy Policy

更新日期 / Last updated: 2026-10-17

开发者 / Developer: BenLampson

联系邮箱 / Contact: cn.benlampson@gmail.com

本政策说明 Asgard Hermod 及其配套账号、配置同步服务如何处理您的信息。

This policy explains how Asgard Hermod and its associated account and configuration synchronization services handle your information.

## 1. 无需登录即可使用 / Use Without Signing In

注册和登录 Hermod 账号均为可选操作。不注册、不登录，您仍可使用本地工作区、远程连接、数据库管理、对象存储及自行配置的 AI 助手等功能。连接远程服务或使用 AI 时，仍需提供相应服务要求的连接信息和凭据。

Registering for and signing in to a Hermod account are optional. You can use the local workspace, remote connections, database management, object storage and your configured AI assistant without a Hermod account. Remote services and AI providers may still require their own connection details and credentials.

在不注册、不登录的本地使用模式下，您的实例配置、连接参数、凭据、标签和模型配置保存在本机，不会上传至 Hermod 的账号或配置同步服务。因此，您无法通过 Hermod 云同步在不同设备之间共享这些配置。

When you use Hermod locally without registering or signing in, your instance configurations, connection parameters, credentials, tags and model configurations are stored on your device and are not uploaded to Hermod's account or configuration synchronization services. Consequently, these configurations cannot be shared across devices through Hermod cloud synchronization.

您主动发起远程连接、文件传输或 AI 请求时，应用仍会向您配置的目标服务发送完成操作所需的数据。这些通信与 Hermod 账号和配置同步服务相互独立，具体处理方式见下文。

When you initiate remote connections, file transfers or AI requests, the application still sends the data needed to perform those operations to the services you configure. These communications are independent of Hermod's account and configuration synchronization services and are described below.

## 2. 本地数据与远程连接 / Local Data and Remote Connections

Hermod 在您的设备上保存实例配置、连接地址、用户名、标签、应用设置，以及您选择保存的密码、私钥和 API 密钥等信息，用于建立连接和恢复工作区。本地保存的凭据、登录令牌及模型密钥使用 Windows 当前用户的数据保护机制进行保护。

Hermod stores instance configurations, connection addresses, usernames, tags, application settings, and any passwords, private keys or API keys you choose to save on your device. This information is used to establish connections and restore your workspace. Saved credentials, authentication tokens and model API keys are protected using Windows data protection for the current user.

使用 SSH、数据库、Redis、RabbitMQ 或对象存储功能时，应用会按照您的操作，与您配置的目标服务通信，处理认证信息、命令、查询、结果、日志或文件。目标服务可能按照其自身规则保存相关数据。

When you use SSH, database, Redis, RabbitMQ or object storage features, the application communicates with the services you configure and processes authentication information, commands, queries, results, logs or files according to your actions. Those services may retain data under their own policies.

文件预览和下载可能产生本地临时副本。您可以通过存储设置清理未使用的临时文件。

File previews and downloads may create temporary local copies. You can clean up unused temporary files through Storage settings.

## 3. 注册与登录 / Registration and Sign-in

如果您选择注册账号，您提供的用户名、邮箱和账号密码会通过 HTTPS 提交至 Hermod 注册服务，并交由 Heimdall 身份服务完成账号创建。Hermod 桌面客户端不保存注册密码。

If you choose to register, your username, email address and account password are submitted to the Hermod registration service over HTTPS and passed to the Heimdall identity service to create your account. The Hermod desktop application does not persist your registration password.

登录通过系统浏览器中的 Heimdall 页面完成。应用处理用户标识、用户名和登录令牌，用于识别账号、验证权限和维持登录状态。

Sign-in takes place on Heimdall pages in your system browser. The application processes your user identifier, username and authentication tokens to identify your account, verify permissions and maintain your signed-in session.

## 4. 云端配置同步 / Cloud Configuration Synchronization

只有您选择登录账号并完成同步密码设置后，应用才会启用云端配置同步。同步内容包括实例、连接配置、标签和模型配置，用于在不同设备之间使用这些配置。

Cloud configuration synchronization is enabled only after you choose to sign in and complete synchronization password setup. It synchronizes instances, connection configurations, tags and model configurations for use across your devices.

实例名称、标签及部分非敏感连接参数会以可读形式保存在同步服务中。敏感连接参数以及包含 API 密钥的模型配置，会在客户端加密后上传。同步密码本身不会发送至同步服务器。

Instance names, tags and some non-sensitive connection parameters are stored in readable form by the synchronization service. Sensitive connection parameters and model configurations, including API keys, are encrypted on your device before upload. Your synchronization password itself is not sent to the synchronization server.

数据库业务内容、对象存储文件、终端输出及 AI 对话不会作为工作区配置自动同步。

Database contents, object storage files, terminal output and AI conversations are not automatically synchronized as workspace configuration.

您可以通过退出账号或忘记已保存的同步密码暂停后续同步，继续使用本地工作区。这些操作不会自动删除已保存的云端数据。

You can pause further synchronization by signing out or forgetting the saved synchronization password and continue using a local workspace. These actions do not automatically delete existing cloud data.

## 5. AI 助手 / AI Assistant

AI 助手会将您的问题、相关对话和完成任务所需的上下文，直接发送至您配置的模型服务。上下文可能包含数据库结构、所选 SQL、您允许共享的结果样本、主动附加的终端文本或日志，以及任务执行产生的诊断和工具结果。

The AI assistant sends your questions, relevant conversation history and task context directly to the model service you configure. This context may include database schemas, selected SQL, result samples you permit the application to share, terminal text or logs you explicitly attach, and diagnostic or tool results produced during a task.

发起 SSH 自动排障任务后，应用可能采集固定范围的只读诊断信息，并将结果发送给模型。需要批准的命令会在执行前要求确认。

When you start an automated SSH troubleshooting task, the application may collect a defined set of read-only diagnostic information and send the results to the model. Commands requiring approval are presented for confirmation before execution.

模型 API 密钥用于向所配置的模型服务进行身份认证。该服务对请求内容的保存、使用和训练规则，取决于其提供方的政策。请在使用前了解其政策，并避免提交无权披露的信息。

Your model API key is used to authenticate requests to the configured model service. That provider's policies determine how request content is retained, used or used for model training. Review those policies before use and avoid submitting information you are not authorized to disclose.

## 6. 信息接收方与保护 / Data Recipients and Protection

应用启动后及运行期间会定期检查更新，也可从设置手动检查。商店安装版通过微软商店查询可用更新，应用通过 GitHub 的公开发布接口读取版本与更新说明。此类检查不发送连接配置、凭据、终端内容或 AI 对话；微软和 GitHub 会接收通信所需的 IP 地址等技术信息。

The application periodically checks for updates after startup and while running; you can also check manually in settings. Store installations query Microsoft Store for update availability, and the application reads public version information and release notes from GitHub. These checks do not send connection configurations, credentials, terminal content or AI conversations. Microsoft and GitHub receive technical information, such as your IP address, needed for communication.

信息仅在提供所选功能所需的范围内，由 Hermod 账号和同步服务、Heimdall 身份服务，以及您配置的远程服务或模型服务处理。网络服务会接收建立通信所需的 IP 地址等技术信息。

Information is processed as needed to provide your selected features by Hermod account and synchronization services, the Heimdall identity service, and the remote or model services you configure. Network services receive technical information, such as your IP address, needed to establish communications.

相关服务的数据处理地点取决于实际部署位置及您选择的服务提供方。我们不会出售您的个人信息。

Data processing locations depend on where the relevant services are deployed and which providers you select. We do not sell your personal information.

我们采用访问控制、传输保护和敏感数据加密等措施保护信息，但任何存储或通信方式均无法保证绝对安全。

We use measures such as access controls, transport protection and encryption of sensitive data to protect information. However, no storage or communication method can guarantee absolute security.

## 7. 保存、管理与删除 / Retention, Controls and Deletion

本地配置会保留至您主动删除相关数据。云端账号和同步数据在提供服务所需期间保存；不再需要或收到有效删除请求后，我们将依法删除或匿名化处理，但法律要求保留的情况除外。

Local configurations remain until you remove the relevant data. Cloud account and synchronization data are retained for as long as needed to provide the services. When no longer needed, or following a valid deletion request, we will delete or anonymize the data in accordance with applicable law, except where retention is legally required.

您可以在应用中查看、修改或删除配置，管理模型服务，退出账号，以及清理未使用的临时文件。退出账号或卸载应用不代表云端数据已删除，也不保证所有本地文件已清除。

You can view, edit or delete configurations, manage model services, sign out, and clean up unused temporary files in the application. Signing out or uninstalling the application does not delete cloud data and does not guarantee removal of all local files.

如需访问、更正、导出个人信息，或申请删除账号及相关云端数据，请联系 cn.benlampson@gmail.com。我们将在核实身份后依法处理。由您自行配置的第三方服务保存的数据，需要向相应服务提供方提出请求。

To request access to, correction or export of your personal information, or deletion of your account and associated cloud data, contact cn.benlampson@gmail.com. We will verify your identity and handle your request in accordance with applicable law. Requests concerning data held by third-party services you configure must be directed to those providers.

## 8. 政策更新与联系 / Policy Updates and Contact

应用功能或数据处理方式发生变化时，我们会更新本政策及更新日期。

We will update this policy and its revision date when application features or data handling practices change.

有关本政策或个人信息处理的问题，请联系：

For questions about this policy or the handling of personal information, contact:

cn.benlampson@gmail.com
