# AgentTasktrayViewer

CodexとClaude Codeの使用量をWindowsのタスクトレイで確認する常駐アプリです。

> v0.0.1は開発中です。開発用MSIXは生成できますが、本番署名は未実施です。

## プロジェクト概要

Codexの5時間枠・週間枠と、Claude Codeの5時間枠・週間枠を、それぞれ独立したタスクトレイアイコンで表示します。通常操作はタスクトレイの右クリックメニューだけで完結します。

## 対応環境

- Windows 10/11 x64
- .NET 10
- Windows Forms
- 配布形式: MSIX

## 開発

開発環境の構築方法は[開発者ガイド](docs/DEVELOPERS_GUIDE.md)を参照してください。

## ドキュメント

- [利用者ガイド](docs/USERS_GUIDE.md)
- [開発者ガイド](docs/DEVELOPERS_GUIDE.md)
- [FAQ](docs/FAQ.md)

## プライバシーとセキュリティ

CodexおよびClaudeの認証情報を直接読み取りません。使用量取得を目的としたAIへのプロンプト送信も行いません。

## ライセンス

[MIT License](LICENSE)
