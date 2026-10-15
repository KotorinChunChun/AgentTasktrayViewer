# セキュリティ診断・改善実装レポート

診断日: 2026-10-15。対象: Core/App/Tests、使用量取得と名前付きパイプ、設定保存。開始時Gitはmainでclean。

| 重大度 | 診断 | 改善方針・実装状態 |
|---|---|---|
| 中 | `src/AgentTasktrayViewer.Core/ClaudeUsageIpc.cs:66` のサーバーpipeはCurrentUserOnly指定なし、ReadToEndAsyncは終了通知以外のタイムアウト/入力上限なし。接続者がEOFを送らないと更新通知を占有でき、大量送信でメモリを消費し得る | 同一ユーザーACL、固定通知形式、入力長・読取時間制限を追加する改善候補。高重大度の修正優先のため今回は未実装 |
| 低 | 設定・usage cacheをローカルJSONで保持。ログ/使用量の保存先ACLは今回未検証 | Windowsユーザー分離とログ保有期間を明文化する |

`CodexUsageProvider.cs` はUseShellExecute=falseの固定引数でCLIを起動し、READMEの認証情報非読取方針と実装の境界を確認。APIキーの新規保存/送信処理は追加していない。

検証: `dotnet list AgentTasktrayViewer.sln package --vulnerable --include-transitive --format json` 成功。NuGet既知脆弱性の指摘0（3プロジェクト）。ソース静的確認であり実IPC攻撃・GUI・実アカウント動作は未検証。製品変更なし、ビルドは形式的に実行しない。

秘密検査: Git追跡テキスト 52 件をPEM秘密鍵・代表的なGitHub/AWS/OpenAIキー形式で検査し候補0。2MB超・バイナリ・vendor・private/temp/logs/releaseを除外。履歴全体、独自形式、未追跡の実設定は未検査であり、秘密情報が存在しない保証ではない。
