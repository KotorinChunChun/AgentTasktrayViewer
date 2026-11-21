#requires -Version 7.0
[CmdletBinding()]
param([string]$Output)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Output) { $Output = Join-Path $root ('dev/history/v0.0.1/logs/management-' + [guid]::NewGuid().ToString('N')) }
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw '新しい専用出力先を指定してください。' }
if (@(Get-Process -Name AgentTasktrayViewer -ErrorAction SilentlyContinue).Count) {
    throw '既存アプリが起動中です。試験は開始せず、既存プロセスを保持します。'
}
$executable = Join-Path $root 'src/AgentTasktrayViewer.App/bin/Debug/net10.0-windows10.0.19041.0/AgentTasktrayViewer.exe'
if (!(Test-Path -LiteralPath $executable)) { throw '先にDebugビルドを実行してください。' }
New-Item -ItemType Directory -Path $Output | Out-Null
$settings = Join-Path $Output 'settings'
$normalSettings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'AgentTasktrayViewer/settings.json'
function SettingsHash {
    if (Test-Path -LiteralPath $normalSettings) { return (Get-FileHash -LiteralPath $normalSettings -Algorithm SHA256).Hash }
    return 'absent'
}
$before = SettingsHash
$pipeName = 'KotorinToys.' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value + '.agent-tasktray-viewer'
function Request([string]$command, [int]$expectedPid = 0) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
    try {
        $pipe.Connect(2000)
        $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 1024, $true)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        try {
            $writer.AutoFlush = $true
            $writer.WriteLine((@{ command = $command; expectedPid = $expectedPid; requestId = [guid]::NewGuid().ToString('N') } | ConvertTo-Json -Compress))
            $line = $reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult()
            return $line | ConvertFrom-Json
        }
        finally { $writer.Dispose(); $reader.Dispose() }
    }
    finally { $pipe.Dispose() }
}
$script:checks = 0
function Check([bool]$value, [string]$message) {
    if (!$value) { throw $message }
    $script:checks++
    Write-Host ('PASS: ' + $message)
}
function Start-TestProcess {
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $root
    foreach ($argument in @('--startup', '--launch-origin', 'host_manual', '--settings-dir', $settings, '--passive')) {
        $start.ArgumentList.Add($argument)
    }
    return [Diagnostics.Process]::Start($start)
}
$owned = Start-TestProcess
try {
    $reply = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline -and !$owned.HasExited) {
        try { $reply = Request status $owned.Id; if ($reply.ready) { break } } catch { }
        Start-Sleep -Milliseconds 100
    }
    Check ($null -ne $reply -and $reply.ready -and $reply.pid -eq $owned.Id) '起動したPIDから準備完了を取得'
    $owned.Refresh()
    Check ([string]::Equals($owned.MainModule.FileName, $executable, [StringComparison]::OrdinalIgnoreCase)) '実行ファイルの配置が一致'
    Check ($owned.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId) '対話セッションが一致'
    Check ($reply.protocolVersion -eq 2 -and $reply.version -eq '0.0.1' -and $reply.launchOrigin -eq 'host_manual') '管理契約・製品版・起動経路が一致'
    Check (($reply.capabilities -join ',') -eq 'status,show,stop') '状態取得・表示・停止に対応'
    Check ($owned.MainWindowHandle -eq [IntPtr]::Zero) 'startupでは設定画面を表示しない'
    Check ((Request show ($owned.Id + 1)).result -eq 'refused') '誤PIDの表示を拒否'
    Check ((Request stop ($owned.Id + 1)).result -eq 'refused') '誤PIDの停止を拒否'
    $duplicate = Start-TestProcess
    try {
        Check ($duplicate.WaitForExit(10000) -and $duplicate.ExitCode -eq 0) '無表示の二重起動は正常終了'
    }
    finally { $duplicate.Dispose() }
    Check ((Request status $owned.Id).pid -eq $owned.Id) '既存PIDを維持'
    Check ((Request show $owned.Id).result -eq 'completed') '設定画面表示を完了'
    $shownDeadline = [DateTime]::UtcNow.AddSeconds(3)
    do { $owned.Refresh(); if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 50 }
    while ([DateTime]::UtcNow -lt $shownDeadline)
    Check ($owned.MainWindowHandle -ne [IntPtr]::Zero -and $owned.MainWindowTitle -eq '設定 - AgentTasktrayViewer - ver 0.0.1') '設定画面のハンドルとタイトルを確認'
    Check ((Request stop $owned.Id).result -eq 'accepted') '正常終了要求を受理'
    Check ($owned.WaitForExit(10000) -and $owned.ExitCode -eq 0) '正常終了をプロセスで確認'
    Check (Test-Path -LiteralPath (Join-Path $settings 'settings.json')) '隔離設定を保存'
    Check (!(Get-Content (Join-Path $settings 'settings.json') -Raw | ConvertFrom-Json).StartWithWindows) '新規自動起動はOFF'
    Check ((SettingsHash) -eq $before) '通常利用者の設定を保持'
    Check (!(Test-Path -LiteralPath (Join-Path $settings 'app.log'))) 'クラッシュログなし'
    @{ passed = $true; checks = $script:checks; isolated = $true } | ConvertTo-Json |
        Out-File -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
}
finally {
    if (!$owned.HasExited) {
        try { $null = Request stop $owned.Id; $null = $owned.WaitForExit(10000) }
        catch { Write-Warning '試験用プロセスの正常終了を確認できませんでした。' }
    }
    $owned.Dispose()
}
