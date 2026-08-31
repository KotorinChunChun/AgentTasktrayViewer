# 開発者ガイド

## 必要な環境

- Windows 10/11 x64
- .NET SDK 10.0.400以降
- Visual Studio Build Tools 2022
- Windows SDK 10.0.26100.0以降（`MakeAppx.exe`と`SignTool.exe`）
- PowerShell 7（`pwsh`）

## 基本コマンド

```powershell
dotnet restore AgentTasktrayViewer.sln
dotnet build AgentTasktrayViewer.sln --configuration Debug --no-restore
dotnet test AgentTasktrayViewer.sln --configuration Debug --no-build
```

MSIXのPublisher情報と署名設定は準備中です。本番用のPublisher、Identity、証明書はリポジトリへ直接保存しません。

