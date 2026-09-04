[CmdletBinding()]
param(
    [string]$ConfigurationFile = 'packaging/msix/development.json',
    [switch]$SkipSigning,
    [string]$SigningBundlePath = 'C:\Users\kc\Downloads\開発用暫定署名',
    [string]$SigningProfilePath
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

function Get-SigningProfile {
    param(
        [Parameter(Mandatory)][string]$BundlePath,
        [string]$ProfilePath
    )

    $modulePath = Join-Path $BundlePath 'MsixSigning.psm1'
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "MSIX署名モジュールが見つかりません: $modulePath"
    }
    Import-Module $modulePath -Force
    if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
        $ProfilePath = Get-MsixSigningProfilePath
    }
    if (-not (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) {
        throw "PC共通のMSIX署名設定がありません: $ProfilePath"
    }

    try {
        $profile = Get-Content -LiteralPath $ProfilePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "PC共通のMSIX署名設定をJSONとして読み込めません: $ProfilePath"
    }
    $profileFields = @($profile.PSObject.Properties.Name)
    $requiredProfileFields = @('schemaVersion', 'storeLocation', 'storeName', 'thumbprint', 'publisher')
    if ($profileFields.Count -ne $requiredProfileFields.Count -or
        @($requiredProfileFields | Where-Object { $_ -notin $profileFields }).Count -gt 0 -or
        $profile.schemaVersion -ne 1 -or
        $profile.storeLocation -cne 'CurrentUser' -or
        $profile.storeName -cne 'My' -or
        $profile.thumbprint -notmatch '^[A-Fa-f0-9]{40}$' -or
        $profile.publisher -notmatch '^CN=.+$') {
        throw 'PC共通のMSIX署名設定が不正です。CurrentUser/Myだけを使用します。'
    }
    return $profile
}

function Get-SigningCertificate {
    param(
        [Parameter(Mandatory)]$Profile,
        [Parameter(Mandatory)][string]$Publisher
    )

    if ($Profile.publisher -cne $Publisher) {
        throw "MSIXのPublisher ($Publisher) と共通署名設定 ($($Profile.publisher)) が一致しません。"
    }
    $thumbprint = ([string]$Profile.thumbprint).Replace(' ', '').ToUpperInvariant()
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -ErrorAction Stop
    if (-not $certificate.HasPrivateKey) {
        $certificate.Dispose()
        throw '共通証明書に秘密鍵がありません。'
    }
    if ($certificate.Subject -cne $Publisher -or $certificate.Thumbprint -cne $thumbprint) {
        $certificate.Dispose()
        throw '共通証明書のPublisherまたは拇印が一致しません。'
    }
    $now = Get-Date
    if ($certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now.AddDays(7)) {
        $certificate.Dispose()
        throw '共通証明書が有効期間外、または期限まで7日以下です。'
    }
    $usages = @($certificate.Extensions |
        Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object { $_.EnhancedKeyUsages } |
        ForEach-Object { $_.Value })
    if ('1.3.6.1.5.5.7.3.3' -notin $usages) {
        $certificate.Dispose()
        throw '共通証明書にCode Signing用途がありません。'
    }
    return $certificate
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
    $certificate = $null
    try {
        $profile = Get-SigningProfile -BundlePath $SigningBundlePath -ProfilePath $SigningProfilePath
        $certificate = Get-SigningCertificate -Profile $profile -Publisher ([string]$configuration.Publisher)
        $signTool = Find-WindowsSdkTool -Name 'SignTool.exe'
        & $signTool sign /sha1 $certificate.Thumbprint /fd SHA256 $packagePath
        if ($LASTEXITCODE -ne 0) { throw 'MSIX署名に失敗しました。' }

        $certificateName = ([string]$configuration.Publisher -replace '^CN=', '') -replace '[^A-Za-z0-9._-]', '_'
        if ([string]::IsNullOrWhiteSpace($certificateName)) { $certificateName = 'AgentTasktrayViewer-Publisher' }
        $certificatePath = Join-Path $outputDirectory "$certificateName.cer"
        Export-Certificate -Cert $certificate -FilePath $certificatePath -Type CERT -Force | Out-Null
        Write-Host "配布用公開証明書を出力しました: $certificatePath" -ForegroundColor Green
    }
    finally {
        if ($certificate) { $certificate.Dispose() }
    }
}

Write-Host "MSIXを生成しました: $packagePath" -ForegroundColor Green
