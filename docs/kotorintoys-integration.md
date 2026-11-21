# KotorinToys連携（開発者向け）

通常起動すると設定画面が開く。設定画面を閉じてもトレイに常駐する。すでに起動しているときに引数なしで起動すると、既存プロセスの設定画面を開く。

| 引数 | 用途 |
| --- | --- |
| `--startup` / `--hidden` | 設定画面を開かずに常駐する。二重起動でも画面を開かない |
| `--launch-origin standalone/host_manual/direct_task/via_kotorintoys` | 管理窓口へ起動経路を返す |
| `--settings-dir <フォルダー>` | 設定とClaude使用量キャッシュの保存先を分離する |
| `--passive` | 検証用。定期取得・初回CLI取得・Claude通知受信を行わない |

`--claude-statusline`は従来のClaude Code連携専用の引数で、管理用の通常起動とは分ける。
保存先を分離してもClaude自身の連携設定は分離されないため、隔離試験では連携を有効にしない。

同一ユーザー専用の名前付きパイプ`KotorinToys.<SID>.agent-tasktray-viewer`で共通契約2のstatus/show/stopに応答する。UIスレッドの応答、要求PID、期限を照合し、モーダル画面を表示している間は停止を拒否する。停止を受理すると、既存のExitThreadでトレイ・タイマー・受信窓口を終了する。
管理窓口にはKotorinToysの`contracts/ManagementEndpoint.cs`を使う。独立してビルドできるよう、同ファイルを製品に含めている。

新規設定では自動起動はOFFとする。既存MSIXの単体StartupTaskは保持する。未パッケージ版の自動起動はKotorinToys側で管理し、単体MSIXの予約と二重に有効化しない。今回はOSの予約・配布形式を変更しない。

```powershell
cd C:\develop\KotorinToys\tools\AgentTasktrayViewer
dotnet build AgentTasktrayViewer.sln -c Debug
dotnet test AgentTasktrayViewer.sln -c Debug --no-build
pwsh -File scripts/Test-Management.ps1
pwsh -File scripts/Test-Ui.ps1
```

管理連携試験は既存アプリが起動中なら開始しない。新しい専用設定で無表示起動・状態取得・設定画面表示・誤PID拒否・二重起動・正常停止を確認し、通常利用者の設定が変わっていないことも照合する。CLIの使用量取得・Claude通知受信・OSの自動起動登録は行わない。

UI試験は実際のメニューと設定画面を隔離データで検証する。結果はそれぞれ版別logsに出力する。インストーラーの生成やリリース公開はこれらの試験に含まれない。
