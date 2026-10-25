# KotorinToys連携（開発者向け）

通常起動は設定画面を開き、閉じてもトレイ常駐を続ける。既存プロセスへの引数なし再起動は設定画面を開く。

| 引数 | 用途 |
| --- | --- |
| `--startup` / `--hidden` | 設定画面を開かず常駐。二重起動でも画面を開かない |
| `--launch-origin standalone/host_manual/direct_task/via_kotorintoys` | 管理窓口へ起動経路を返す |
| `--settings-dir <フォルダー>` | 設定とClaude使用量キャッシュの保存先を分離 |
| `--passive` | 検証用。定期取得・初回CLI取得・Claude通知受信を行わない |

`--claude-statusline`は従来のClaude Code連携専用入口で、管理用の通常起動とは分ける。
設定先の分離はClaude自身の連携設定の分離ではないため、隔離試験では連携の有効化操作を行わない。

同一ユーザー専用の`KotorinToys.<SID>.agent-tasktray-viewer` pipeで共通契約2のstatus/show/stopに応答する。UIスレッドの応答、要求PID、期限を照合し、モーダル画面中は停止を拒否する。停止受理後は既存のExitThreadによってトレイ・タイマー・受信窓口を終了する。
管理窓口はKotorinToysの`contracts/ManagementEndpoint.cs`を独立ビルドできるよう製品内へ収録したもの。

自動起動は新規OFF。既存MSIXの単体StartupTaskは保持する。未パッケージ版の自動起動はKotorinToys側で管理し、単体MSIXの予約と二重に有効化しない。今回はOSの予約・配布形式を変更しない。

```powershell
cd C:\develop\AgentTasktrayViewer
dotnet build AgentTasktrayViewer.sln -c Debug
dotnet test AgentTasktrayViewer.sln -c Debug --no-build
```
