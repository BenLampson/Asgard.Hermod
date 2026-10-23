#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',
    [string] $PackageVersion,
    [string] $MakeAppxPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
[xml] $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw -Encoding UTF8
if (-not $PackageVersion) {
    $PackageVersion = $manifest.Package.Identity.Version
}
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw 'Use a four-part package version with a zero final component, for example 1.0.0.0.'
}
$parsedVersion = [version] $PackageVersion
if ($parsedVersion.Major -eq 0 -and $parsedVersion.Minor -eq 0 -and $parsedVersion.Build -eq 0) {
    throw 'The package version must be greater than 0.0.0.0.'
}
if ($parsedVersion.Major -gt 65535 -or $parsedVersion.Minor -gt 65535 -or $parsedVersion.Build -gt 65535) {
    throw 'Each package version component must be at most 65535.'
}

# 使用最新已安装的 Windows SDK；也允许构建服务器显式指定工具路径。
if (-not $MakeAppxPath) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $sdkVersions = @(Get-ChildItem -LiteralPath $sdkBin -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } |
        Sort-Object { [version] $_.Name } -Descending)
    foreach ($sdkVersion in $sdkVersions) {
        $candidate = Join-Path $sdkVersion.FullName 'x64/MakeAppx.exe'
        if (Test-Path -LiteralPath $candidate) {
            $MakeAppxPath = $candidate
            break
        }
    }
}
if (-not $MakeAppxPath -or -not (Test-Path -LiteralPath $MakeAppxPath -PathType Leaf)) {
    throw 'Install the Windows SDK packaging tools or pass -MakeAppxPath.'
}

# 每次创建独立暂存目录，避免混入旧版本、其他架构或本地用户文件。
$outputDirectory = Join-Path $repositoryRoot 'artifacts/msix'
$stagingDirectory = Join-Path $outputDirectory ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
$projectPath = Join-Path $repositoryRoot 'src/Asgard.Hermod/Asgard.Hermod.csproj'
& dotnet publish $projectPath -c Release -r "win-$Architecture" --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$PackageVersion" -o $stagingDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory 'zh-Hans/Asgard.Hermod.Abstractions.resources.dll'))) {
    throw 'The Simplified Chinese satellite resources are missing from the publish output.'
}

# 安装包保留项目许可、隐私政策和派生终端代码的上游许可，避免仅在源码仓库中提供声明。
foreach ($document in @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md', 'PRIVACY.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination (Join-Path $stagingDirectory $document)
}
$terminalNoticeDirectory = Join-Path $stagingDirectory 'Licenses/WinTerminal'
New-Item -ItemType Directory -Path (Join-Path $terminalNoticeDirectory 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src/Asgard.Hermod.WinTerminal/THIRD-PARTY-NOTICES.md') `
    -Destination (Join-Path $terminalNoticeDirectory 'THIRD-PARTY-NOTICES.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src/Asgard.Hermod.WinTerminal/licenses/VirtualTerminal.LICENSE.txt') `
    -Destination (Join-Path $terminalNoticeDirectory 'licenses/VirtualTerminal.LICENSE.txt')

$manifest.Package.Identity.Version = $PackageVersion
$manifest.Package.Identity.ProcessorArchitecture = $Architecture
$manifest.Save((Join-Path $stagingDirectory 'AppxManifest.xml'))

# 从已有品牌图标导出商店所需尺寸，不重新设计或修改品牌图形。
Add-Type -AssemblyName System.Drawing
$assetsDirectory = Join-Path $stagingDirectory 'Assets'
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
$logo = [Drawing.Image]::FromFile((Join-Path $repositoryRoot 'src/Asgard.Hermod.UI/Assets/Brand/hermod.png'))
try {
    foreach ($asset in @(
        @{ Name = 'StoreLogo.png'; Size = 50 },
        @{ Name = 'Square44x44Logo.png'; Size = 44 },
        @{ Name = 'Square150x150Logo.png'; Size = 150 }
    )) {
        $bitmap = [Drawing.Bitmap]::new($asset.Size, $asset.Size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($logo, 0, 0, $asset.Size, $asset.Size)
            $bitmap.Save((Join-Path $assetsDirectory $asset.Name), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $logo.Dispose()
}

# 商店提交包不包含私钥；本地安装测试需另行签名并信任测试证书。
$packagePath = Join-Path $outputDirectory "Asgard.Hermod_${PackageVersion}_${Architecture}.msix"
& $MakeAppxPath pack /d $stagingDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}
Write-Output "MSIX package: $packagePath"
Write-Output "Unpacked test files: $stagingDirectory"
