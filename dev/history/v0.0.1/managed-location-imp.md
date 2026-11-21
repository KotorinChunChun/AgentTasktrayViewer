# 管理フォルダーへの一本化

2026-10-25。KotorinToysへの収録後、利用者の指示により正本を`tools/AgentTasktrayViewer`へ移す。

- 元と管理先のHEADは`1d4d85c`で一致し、いずれも未保存変更なし。管理先のGit完全性を確認済み。
- 既存の成果物`artifacts`と`release`は管理先の同名フォルダーへ移動する。
- 元の作業フォルダー（元Git、中間生成物、履歴を含む）は`dev/history/v0.0.1/logs/relocation-original-workspace/`へ回復用に移動する。Git対象外とし、削除しない。
- 管理先の独立Gitは本体`.git/modules/tools/AgentTasktrayViewer`を使用。元配置へのoriginと親のローカルURLは解除する。利用者設定・OS予約は変更しない。
- [x] 移動後のDebugビルド成功（警告0・エラー0）、単体テスト14件成功。

旧配置の隠し`.git`の空フォルダーのみ一括移動で残ったため、退避Gitの`fsck --full`成功と元の空状態を確認して空フォルダーを除去した。元Git・ソース・中間生成物は退避先に残っており回復可能。
