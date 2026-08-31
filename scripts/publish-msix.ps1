[CmdletBinding()]
param(
    [string]$ConfigurationFile = 'packaging/msix/development.json',
    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configurationPath = (Resolve-Path (Join-Path $projectRoot $ConfigurationFile)).Path
$configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)
$versionPrefix = [string]$buildProperties.Project.PropertyGroup.VersionPrefix
if ($versionPrefix -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    throw "VersionPrefixはMAJOR.MINOR.PATCH形式で指定してください: $versionPrefix"
}
$packageVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
$artifactRoot = Join-Path $projectRoot 'artifacts/msix'
$publishDirectory = Join-Path $artifactRoot 'publish'
$layoutDirectory = Join-Path $artifactRoot 'layout'
$outputDirectory = Join-Path $projectRoot 'release/Publish'
$packagePath = Join-Path $outputDirectory "AgentTasktrayViewer_v$versionPrefix.msix"

function Find-WindowsSdkTool {
    param([Parameter(Mandatory)][string]$Name)

    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $tool = Get-ChildItem -LiteralPath $sdkBin -Recurse -Filter $Name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "$Name が見つかりません。Windows SDKを確認してください。" }
    return $tool.FullName
}

function New-PlaceholderPng {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height
    )

    Add-Type -AssemblyName System.Drawing.Common
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(37, 99, 235))
            $fontSize = [Math]::Max(8, [Math]::Min($Width, $Height) / 3)
            $font = [System.Drawing.Font]::new('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold)
            $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $format = [System.Drawing.StringFormat]::new()
            try {
                $format.Alignment = [System.Drawing.StringAlignment]::Center
                $format.LineAlignment = [System.Drawing.StringAlignment]::Center
                $graphics.DrawString('A', $font, $brush, [System.Drawing.RectangleF]::new(0, 0, $Width, $Height), $format)
            }
            finally {
                $format.Dispose()
                $brush.Dispose()
                $font.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory, $layoutDirectory, (Join-Path $layoutDirectory 'Assets'), $outputDirectory -Force | Out-Null

dotnet publish (Join-Path $projectRoot 'src/AgentTasktrayViewer.App/AgentTasktrayViewer.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw '発行に失敗しました。' }

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $layoutDirectory -Recurse -Force
$assetsDirectory = Join-Path $layoutDirectory 'Assets'
New-PlaceholderPng -Path (Join-Path $assetsDirectory 'StoreLogo.png') -Width 50 -Height 50
New-PlaceholderPng -Path (Join-Path $assetsDirectory 'Square44x44Logo.png') -Width 44 -Height 44
New-PlaceholderPng -Path (Join-Path $assetsDirectory 'Square150x150Logo.png') -Width 150 -Height 150
New-PlaceholderPng -Path (Join-Path $assetsDirectory 'Wide310x150Logo.png') -Width 310 -Height 150

$manifestTemplate = Get-Content -LiteralPath (Join-Path $projectRoot 'packaging/msix/Package.appxmanifest.template') -Raw
$identityName = [System.Security.SecurityElement]::Escape([string]$configuration.IdentityName)
$publisher = [System.Security.SecurityElement]::Escape([string]$configuration.Publisher)
$publisherDisplayName = [System.Security.SecurityElement]::Escape([string]$configuration.PublisherDisplayName)
$displayName = [System.Security.SecurityElement]::Escape([string]$configuration.DisplayName)
$manifest = $manifestTemplate.Replace('__IDENTITY_NAME__', $identityName)
$manifest = $manifest.Replace('__PUBLISHER__', $publisher)
$manifest = $manifest.Replace('__PUBLISHER_DISPLAY_NAME__', $publisherDisplayName)
$manifest = $manifest.Replace('__DISPLAY_NAME__', $displayName)
$manifest = $manifest.Replace('__VERSION__', $packageVersion)
Set-Content -LiteralPath (Join-Path $layoutDirectory 'AppxManifest.xml') -Value $manifest -Encoding utf8NoBOM

$makeAppx = Find-WindowsSdkTool -Name 'MakeAppx.exe'
& $makeAppx pack /d $layoutDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX生成に失敗しました。' }

if (-not $SkipSigning) {
    $thumbprint = $env:AGENTTASKTRAYVIEWER_SIGNING_THUMBPRINT
    if ([string]::IsNullOrWhiteSpace($thumbprint)) {
        throw '署名証明書のThumbprintがありません。環境変数AGENTTASKTRAYVIEWER_SIGNING_THUMBPRINTを設定するか、-SkipSigningを指定してください。'
    }
    $signTool = Find-WindowsSdkTool -Name 'SignTool.exe'
    & $signTool sign /sha1 $thumbprint /fd SHA256 $packagePath
    if ($LASTEXITCODE -ne 0) { throw 'MSIX署名に失敗しました。' }
}

Write-Host "MSIXを生成しました: $packagePath" -ForegroundColor Green
