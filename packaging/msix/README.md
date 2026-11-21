# MSIXパッケージング

`Package.appxmanifest.template`から環境別設定を反映したマニフェストを生成します。

- `development.json`: ローカル検証用Identity
- `production.example.json`: Partner Center情報の記入例

本番用設定、証明書、秘密情報はコミットしません。Publisher情報が確定したら、`production.example.json`を基にGit対象外のローカル設定を用意します。
