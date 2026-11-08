using System.Text.Json;
using AgentTasktrayViewer;
using AgentTasktrayViewer.Core;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length) return 2;
        var root = Path.GetFullPath(args[outputIndex + 1]);
        if (Directory.Exists(root)) { Console.Error.WriteLine("新しい専用出力先を指定してください。"); return 2; }
        var showIndex = Array.IndexOf(args, "--show");
        var scene = showIndex >= 0 && showIndex + 1 < args.Length ? args[showIndex + 1] : null;
        if (scene is not (null or "menu" or "codex" or "diagnostics" or "settings" or "icons")) return 2;
        Directory.CreateDirectory(root);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
        try
        {
            using var context = new TrayApplicationContext(new(true, root, true), isolatedTest: true);
            var menu = context.TestMenu;
            ToolStripMenuItem Item(string name) => menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == name);
            var manager = new SettingsManager(root);
            Check(!Item("Windows起動時に開始").Enabled && !Item("Claude Code連携").Enabled, "外部設定操作を無効化");
            Check(!Item("Codex 使用量ページを開く").Enabled, "外部ページを開かない");
            Item("今すぐ更新").PerformClick();
            // 使用量と診断は架空の固定応答。CLIやネットワークを呼ばない。
            var visibility = Item("表示するアイコン").DropDownItems.OfType<ToolStripMenuItem>().ToArray();
            Check(visibility.Length == 4, "4枠の表示切替");
            visibility[0].PerformClick();
            Check(!manager.Load().ShowCodexFiveHour, "表示OFFの専用設定保存");
            visibility[0].PerformClick();
            Check(manager.Load().ShowCodexFiveHour, "表示ONの専用設定保存");
            Item("更新間隔").DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "3分").PerformClick();
            Check(manager.Load().UpdateIntervalMinutes == 3, "更新間隔の保存");
            Item("アイコンスタイル").DropDownItems.OfType<ToolStripMenuItem>().First().PerformClick();
            Check(manager.Load().IconVisualStyle == TrayIconVisualStyle.ColoredBackground, "表示形式の保存");
            Item("設定...").PerformClick();
            Check(context.TestSettings?.Visible == true, "実設定画面を開く");
            context.TestSettings!.Close();
            using var owner = new Form { Text = WindowTitle.Create("操作確認"), Size = new Size(740, 600), StartPosition = FormStartPosition.CenterScreen };
            context.ThreadExit += (_, _) => owner.Close();
            owner.Show();
            menu.Show(owner, new Point(20, 45));
            Application.DoEvents();
            Check(Item("Codex").DropDownItems[0].Text!.Contains("残り75%"), "使用率を残量へ変換");
            Check(Item("Claude").DropDownItems[0].Text!.Contains("残り?%"), "未受信を未知として表示");
            Check(Item("更新間隔").DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "3分").Checked, "メニュー再表示時の選択保持");
            menu.Close(); owner.Hide();
            File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed = true, checks, isolated = true, liveProviderCalled = false, osStartupChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {checks}");
            if (scene == "settings") { Item("設定...").PerformClick(); Application.Run(context.TestSettings!); }
            else if (scene == "icons")
            {
                using var preview = new Form { Text = WindowTitle.Create("アイコン表示例"), ClientSize = new Size(580, 260), StartPosition = FormStartPosition.CenterScreen };
                var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3, Padding = new Padding(16) };
                table.Controls.Add(new Label { Text = "残量", AutoSize = true }, 0, 0);
                var values = new double?[] { 25, 75, 95, null };
                for (int i = 0; i < 4; i++) table.Controls.Add(new Label { Text = values[i] is double v ? $"{100-v}%" : "取得前", AutoSize = true }, i+1, 0);
                var bitmaps = new List<Bitmap>();
                foreach (var style in Enum.GetValues<TrayIconVisualStyle>())
                {
                    int row = style == TrayIconVisualStyle.ColoredBackground ? 1 : 2;
                    table.Controls.Add(new Label { Text = row == 1 ? "背景色版" : "文字色版", AutoSize = true }, 0, row);
                    for (int i = 0; i < 4; i++)
                    {
                        var window = values[i] is double value ? UsageWindow.Available(UsageProvider.Codex, UsageWindowType.FiveHour, value, null) : UsageWindow.Unavailable(UsageProvider.Codex, UsageWindowType.FiveHour);
                        using var icon = IconFactory.Create(window, Color.FromArgb(37,99,235), style, "Segoe UI");
                        var bitmap = icon.ToBitmap(); bitmaps.Add(bitmap);
                        table.Controls.Add(new PictureBox { Image = bitmap, Size = new Size(64,64), SizeMode = PictureBoxSizeMode.CenterImage }, i+1, row);
                    }
                }
                preview.Controls.Add(table); Application.Run(preview); foreach (var bitmap in bitmaps) bitmap.Dispose();
            }
            else if (scene != null)
            {
                // 初回Show後なので、再表示はVisibleChangedで一度だけ行う。
                owner.VisibleChanged += (_, _) => { if (!owner.Visible) return; menu.AutoClose=false; menu.Show(owner,new Point(20,45)); if(scene!="menu") Item(scene=="codex"?"Codex":"接続状態").ShowDropDown(); };
                Application.Run(owner);
            }
            context.ExitThread();
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
