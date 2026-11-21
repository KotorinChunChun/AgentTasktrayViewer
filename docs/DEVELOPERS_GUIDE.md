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

開発用の署名にはPC共通の署名設定と`CurrentUser\My`証明書ストアを使用します。PFXはリポジトリへコピーしません。

```powershell
pwsh -NoProfile -File scripts/publish-msix.ps1
```

署名バンドルの場所が既定値と異なる場合は、`-SigningBundlePath`で指定してください。PFXのパスワードや秘密鍵はスクリプトへ渡さず、署名バンドルの移植手順に従って事前登録します。

開発用の自己署名証明書は信頼ストアへ登録していないため、`SignTool verify /pa`では信頼チェーン警告になる場合があります。発行処理で`Successfully signed`が表示され、署名証明書のPublisher・拇印・秘密鍵・Code Signing用途が一致することを確認します。

署名付きMSIXを発行すると、MSIXと同じ`release/Publish`へ署名者の公開証明書（`.cer`）も自動出力します。現在の開発用構成では`KotorinChunChun.cer`です。配布先ではこの`.cer`だけを信頼済みユーザー（`Trusted People`）へ登録します。秘密鍵を含む`.pfx`は配布しません。


## 通知領域を手動操作しないCLI検証

```powershell
cd C:\develop\KotorinToys\tools\AgentTasktrayViewer
pwsh -File ./scripts/Test-Ui.ps1
pwsh -File ./scripts/Test-Ui.ps1 -Show menu
```

通常実行では実際のWinFormsのメニューと設定画面で11項目を自動検証します。成功すると専用の出力先にresult.jsonを出力し、終了値0を返します。失敗時は1、引数が不正か出力先がすでにある場合は2です。Showにはmenu、codex、diagnostics、settings、iconsを指定できます。CLI検証の後に指定した画面が開き、閉じると終了します。アイコン例は製品と同じIconFactoryで描画します。

試験用EXEだけが内部の隔離入口を利用します。通知領域のアイコン・管理IPC・定期取得は作成せず、Codex/Claudeの実接続、連携設定、Windows起動登録、外部ページは使用しません。製品のメニューと保存処理をそのまま使い、設定は毎回新しい専用ディレクトリへ保存します。実トレイの位置・サービス認証・実取得の確認は別の受入です。

通常の製品起動には変更ありません。CLIの隔離オプションを通常利用者向け引数に追加していません。
