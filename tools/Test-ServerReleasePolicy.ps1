#Requires -Version 7.0
<#
.SYNOPSIS
检查受版本控制的发布入口，阻止远程执行、源码传输和生产 Compose 构建入口。
.DESCRIPTION
此检查是仓库发布规则，不能代替生产主机权限隔离；忽略的历史 artifacts 仅保留为证据。
#>
[CmdletBinding(DefaultParameterSetName = 'Repository')]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory, ParameterSetName = 'Image')]
    [string]$ImageReference,
    [Parameter(Mandatory, ParameterSetName = 'Image')]
    [string]$SourceCommit
)

$ErrorActionPreference = 'Stop'
if ($PSCmdlet.ParameterSetName -eq 'Image') {
    # 必须包含 Registry、唯一版本、完整来源提交和清单摘要，拒绝旧的本地镜像标签。
    $referencePattern = '^[a-z0-9][a-z0-9.-]*\.[a-z0-9.-]+(?::[0-9]+)?/[a-z0-9._/-]+:v[0-9]+\.[0-9]+\.[0-9]+-[A-Za-z0-9_.-]+@sha256:[0-9a-f]{64}$'
    if ($SourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $ImageReference -cnotmatch $referencePattern -or
        $ImageReference.Split('@')[0] -cnotmatch ('[:.-]' + [regex]::Escape($SourceCommit) + '(?:[.-]|$)')) {
        throw 'Require a versioned Registry image containing the full source commit and an @sha256 manifest digest.'
    }
}
$composePath = Join-Path $RepositoryRoot 'Docker/server/compose.yaml'
$compose = Get-Content -LiteralPath $composePath -Raw
if ($compose -match '(?m)^\s*(build|dockerfile|context)\s*:') {
    throw 'Production Compose must use Registry images only; build configuration is forbidden.'
}
if ($compose -notmatch 'image:\s*\$\{HERMOD_SERVER_IMAGE:\?') {
    throw 'Production Compose must require an explicit HERMOD_SERVER_IMAGE.'
}

# 发布入口不提供远程 shell 或文件传输能力；生产操作由独立的运行维护流程完成。
$entryPoints = @('.github/workflows', 'tools', 'packaging', 'Docker')
$extensions = @('.ps1', '.sh', '.bash', '.cmd', '.bat', '.py', '.js', '.mjs', '.yml', '.yaml')
$remoteCommand = '(?i)(?<![\w/-])(ssh|scp|sftp|rsync|plink|pscp|sshpass|paramiko|fabric)(?:\.exe)?(?=[\s"''(])'
$remoteDocker = '(?i)(ssh|tcp)://|DOCKER_HOST|DOCKER_CONTEXT|BUILDX_BUILDER|BUILDKIT_HOST|docker\s+(?:--context|--host|-H)\b|docker\s+context\s+(?:create|use)|docker\s+buildx\s+create'
foreach ($entryPoint in $entryPoints) {
    $directory = Join-Path $RepositoryRoot $entryPoint
    if (-not (Test-Path -LiteralPath $directory)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
        if ($file.FullName -eq $PSCommandPath -or $file.Extension -notin $extensions) { continue }
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if ($content -match $remoteCommand -or $content -match $remoteDocker) {
            throw "Remote execution, transfer or Docker endpoint overrides are forbidden in release entry points: $($file.FullName)"
        }
        if ($file.Extension -in @('.yml', '.yaml') -and $content -match 'self-hosted') {
            throw "Release automation must use hosted CI runners: $($file.FullName)"
        }
    }
}

Write-Output 'Server release policy passed: runtime-only Compose; no remote execution or transfer entry points.'
