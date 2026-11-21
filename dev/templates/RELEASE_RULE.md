# AgentTasktrayViewer リリース手順

## 配布方式

AgentTasktrayViewerは.NET 10の`win-x64`自己完結型発行物をMSIXへ格納して配布します。ClickOnce、Inno Setup、NSISは使用しません。

## バージョン

セマンティックバージョニングを使用します。アプリの表示バージョンはプロジェクト設定、MSIXの4桁バージョンは`Package.appxmanifest`生成時に一元的に反映します。

## PublisherとIdentity

- 開発時は開発専用Identityとテスト署名を使用します。
- 本番時はPartner Centerで確定したIdentity、Publisher、証明書へ差し替えます。
- 本番証明書とパスワードはリポジトリへ保存しません。
- Publisher情報が未確定の間は本番パッケージを公開しません。

## ビルドと検証

1. `pwsh ./scripts/build.ps1`でReleaseビルドとテストを実行します。
2. `pwsh ./scripts/publish-msix.ps1`で自己完結型単一ファイルを発行し、MSIXを生成します。
3. 署名設定が指定された場合だけ`SignTool.exe`で署名します。
4. 生成物はGit対象外の`release/Publish/`へ保存します。
5. MSIXのインストール、起動、トレイ常駐、自動起動、アンインストールを実機で確認します。

## Store公開

Partner Centerの予約済み名称とIdentityを本番設定へ反映し、Store申請用パッケージを生成します。開発用Identityの成果物をStoreへ提出してはいけません。

## PowerShell

すべての`.ps1`はPowerShell 7の`pwsh`で実行します。
