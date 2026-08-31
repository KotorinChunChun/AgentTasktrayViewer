[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$solution = Join-Path $projectRoot 'AgentTasktrayViewer.sln'

Push-Location $projectRoot
try {
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw '依存関係の復元に失敗しました。' }

    dotnet build $solution --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }

    dotnet test $solution --configuration $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'テストに失敗しました。' }
}
finally {
    Pop-Location
}

