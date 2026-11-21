#requires -Version 7.0
param([ValidateSet('none','menu','codex','diagnostics','settings','icons')][string]$Show='none', [string]$Output)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Output){$Output=Join-Path $root ('dev/history/v0.0.1/logs/ui-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))}
$build=Join-Path $root 'artifacts/readme-ui'
dotnet build (Join-Path $root 'tests/AgentTasktrayViewer.UiTests') --artifacts-path $build -p:NuGetAudit=false --nologo
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$arguments=@('--output',[IO.Path]::GetFullPath($Output))
if($Show -ne 'none'){$arguments+=@('--show',$Show)}
& (Join-Path $build 'bin/AgentTasktrayViewer.UiTests/debug/AgentTasktrayViewer.UiTests.exe') @arguments
exit $LASTEXITCODE
