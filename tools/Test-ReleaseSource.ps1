#Requires -Version 7.0
<#
.SYNOPSIS
确认发布任务从 main 运行，检出提交属于远端 main，并返回商店包版本。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReleaseTag,
    [string]$SourceRef = $env:GITHUB_REF,
    [string]$SourceCommit = $env:GITHUB_SHA,
    [switch]$RequireNewTag
)

$ErrorActionPreference = 'Stop'

if ($SourceRef -cne 'refs/heads/main') {
    throw 'Releases can only run from the main branch.'
}
if ($ReleaseTag -cnotmatch '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'Release tags must use vMajor.Minor.Patch, for example v1.0.1.'
}
if ($SourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'The release source must be an exact commit SHA.'
}

$checkedOutCommit = git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $checkedOutCommit -ne $SourceCommit) {
    throw 'The checked-out commit does not match the release source.'
}

# 标签事件的 ref 不是分支，不能仅根据 Release 的 target_commitish 判断代码来源。
# 获取实际 main 历史；允许发布仍属于 main 的已验证提交，不要求打包期间分支静止。
git fetch --no-tags origin '+refs/heads/main:refs/remotes/origin/main' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not fetch the main branch for release verification.'
}
git merge-base --is-ancestor $SourceCommit refs/remotes/origin/main
if ($LASTEXITCODE -ne 0) {
    throw 'The release source has not been merged into main.'
}

if ($RequireNewTag) {
    git ls-remote --exit-code --tags origin "refs/tags/$ReleaseTag" | Out-Null
    $tagExitCode = $LASTEXITCODE
    if ($tagExitCode -eq 0) {
        throw 'The release tag already exists. Use a new version instead of replacing it.'
    }
    if ($tagExitCode -ne 2) {
        throw 'Could not check whether the release tag already exists.'
    }
}

$ReleaseTag.Substring(1) + '.0'
