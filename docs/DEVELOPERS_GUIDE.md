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

本番用のPublisher、Identity、証明書はリポジトリへ直接保存しません。開発用MSIXはPC共通の暫定署名設定を利用できます。

## MSIX署名

開発用署名は、PFXをリポジトリへコピーせず、PC共通署名設定と`CurrentUser\My`証明書ストアを使用します。

```powershell
pwsh -NoProfile -File scripts/publish-msix.ps1
```

署名バンドルの場所が既定値と異なる場合は、`-SigningBundlePath`で指定してください。PFXのパスワードや秘密鍵はスクリプトへ渡さず、署名バンドルの移植手順に従って事前登録します。

開発用の自己署名証明書は信頼ストアへ登録していないため、`SignTool verify /pa`では信頼チェーン警告になる場合があります。発行処理で`Successfully signed`が表示され、署名証明書のPublisher・拇印・秘密鍵・Code Signing用途が一致することを確認します。
