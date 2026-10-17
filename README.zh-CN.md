# Asgard Hermod

[English](README.md) | **简体中文**

面向开发人员和运维人员的 Windows 桌面管理工具，在一个应用中管理 SSH、数据库、
消息队列和对象存储，并提供可选的 AI 助手与跨设备配置同步。

[源代码](https://github.com/BenLampson/Asgard.Hermod/tree/dev) ·
[使用与开发文档](https://github.com/BenLampson/Asgard.Hermod/tree/dev/docs) ·
[隐私政策](PRIVACY.md) · [支持](SUPPORT.md) · [Apache 2.0 许可证](LICENSE)

## 功能

| 工作区 | 功能 |
| --- | --- |
| 本地终端 | Windows ConPTY 会话、终端标签、文本选择与键盘操作。 |
| SSH | 终端、SFTP 文件传输、端口转发、Linux 监控和容器管理。 |
| MySQL 与 PostgreSQL | 数据库对象浏览、独立 SQL 会话、查询结果、表格编辑与执行计划。 |
| Redis | Redis 7+ 连接、拓扑发现、键浏览与值编辑。 |
| RabbitMQ | Management API 连接、资源管理与按需查看消息。 |
| 对象存储 | 阿里云 OSS、腾讯云 COS、火山引擎 TOS、AWS S3、MinIO 和自定义 S3 兼容服务；支持上传、下载、预览和临时链接。 |
| AI 助手 | 使用自行配置的兼容模型服务辅助 SQL、日志分析和 SSH 排障；需要批准的操作会在执行前要求确认。 |
| 个人工作区 | 实例管理、标签、浅色与深色主题、中英文界面，以及可选的凭据加密同步。 |

## 不登录也能使用

**Hermod 账号为可选项。** 不注册、不登录，仍可使用本地及远程资源管理功能，
包括自行配置的 AI 助手。连接远程服务时，仍需提供相应服务要求的凭据。

未登录时，工作区配置保存在本机，不会上传至 Hermod 的账号或配置同步服务。
通过 Hermod 云同步在不同设备之间共享配置，需要先登录并设置同步密码。

远程连接、文件传输和 AI 请求仍会向您配置的目标服务发送完成操作所需的数据。
本地存储、账号、同步及 AI 的数据处理方式见[隐私政策](PRIVACY.md)。

## 获取应用与运行要求

目前正在准备 Microsoft Store 上架。发布后的下载可查看
[GitHub Releases](https://github.com/BenLampson/Asgard.Hermod/releases)，
也可以按照下文从源代码构建开发版本。

- 当前桌面安装包要求 Windows 10 22H2（19045）及以上版本，包括 Windows 11。
  WPF 应用需要在 Windows 上运行。
- 当前商店安装包面向 x64，已包含 .NET 运行时。
- 远程工作区需要您自己的服务器或云服务。
- AI 功能需要兼容的模型服务地址、模型名称，以及服务提供方要求的凭据。

应用支持英文和简体中文，默认跟随 Windows 显示语言，并记住手动语言选择。
不支持的语言和缺失的翻译均回退至英文；界面语言与区域格式相互独立。

## 从源代码构建

当前应用源代码和详细开发文档位于
[`dev`](https://github.com/BenLampson/Asgard.Hermod/tree/dev) 分支。
请在 Windows 上安装 .NET 10 SDK；SDK 选择规则见该分支的 `global.json`。
也可以使用安装了 .NET 桌面开发工作负载的 Visual Studio。

```powershell
git clone --branch dev https://github.com/BenLampson/Asgard.Hermod.git
cd Asgard.Hermod
dotnet restore Asgard.Hermod.slnx
dotnet build Asgard.Hermod.slnx -c Release --no-restore
dotnet run --project src/Asgard.Hermod --configuration Release --no-build
```

开发版本将本地配置保存在 `%LOCALAPPDATA%\Asgard\Hermod\hermod.db`。
打包安装的版本可能使用 Windows 为包分配的存储路径，详见
[本地存储说明](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/local-storage.md)。

依赖框架的发布版本要求安装 .NET 10 Desktop Runtime。
[Microsoft Store 打包说明](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/microsoft-store.md)
介绍了包含运行时的 MSIX 构建、包标识及提交前检查。

## 文档

| 主题 | 文档 |
| --- | --- |
| 架构与项目职责 | [模块架构](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/architecture.md) |
| SSH、SFTP、监控与容器 | [SSH 工作区](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/ssh.md) |
| SQL 工作区 | [MySQL](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/mysql.md)、[PostgreSQL](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/postgresql.md) |
| 云存储连接 | [对象存储提供方](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/object-storage-providers.md) |
| 预览、外部打开与临时文件 | [文件打开](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/file-opening.md)、[存储设置](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/storage-settings.md) |
| 账号与跨设备配置 | [身份与同步](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/identity-and-sync.md) |
| 语言选择与翻译 | [国际化](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/localization.md) |
| 终端嵌入与来源声明 | [终端组件](https://github.com/BenLampson/Asgard.Hermod/blob/dev/src/Asgard.Hermod.WinTerminal/README.md) |

## 参与开发

修改应用前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 和
[代理开发规则](https://github.com/BenLampson/Asgard.Hermod/blob/dev/AGENTS.md)。
仓库文档、Issue 和 Pull Request 默认使用英文。应用自身的界面文字需要提供英文和
简体中文资源，并以英文作为回退。

在 `dev` 分支提交代码前运行：

```powershell
dotnet build Asgard.Hermod.slnx -c Release
dotnet format Asgard.Hermod.slnx --verify-no-changes --no-restore
dotnet test --solution Asgard.Hermod.slnx --configuration Release --no-build
```

## 支持、隐私与安全

- 缺陷与功能建议：[GitHub Issues](https://github.com/BenLampson/Asgard.Hermod/issues)。
- 支持与私密咨询：[cn.benlampson@gmail.com](mailto:cn.benlampson@gmail.com)。
- 数据处理与删除请求：[隐私政策](PRIVACY.md)。
- 漏洞与敏感报告：[安全政策](SECURITY.md)。
- 社区参与：[行为准则](CODE_OF_CONDUCT.md)。

请勿在公开 Issue 中提供密码、私钥、API 密钥或未经脱敏的连接字符串。
报告问题需要的信息见 [SUPPORT.md](SUPPORT.md)。

## 许可证

Copyright 2026 BenLampson and contributors.

项目原创内容采用 [Apache License, Version 2.0](LICENSE) 许可。
项目归属声明见 [NOTICE](NOTICE)，随项目使用的组件与素材见
[第三方声明](THIRD-PARTY-NOTICES.md)。第三方组件保留各自的许可证与归属声明。
第三方名称与标识归各自权利人所有，项目许可证不授予第三方商标权利。
