using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal sealed class SettingsForm : Form
{
    private readonly Func<TrayIconKind, bool> _isVisible;
    private readonly Func<TrayIconKind, bool, bool> _setVisibility;
    private readonly CheckBox[] _visibilityChecks;
    private bool _loading;

    public SettingsForm(
        Func<TrayIconKind, bool> isVisible,
        Func<TrayIconKind, bool, bool> setVisibility,
        Action refresh)
    {
        _isVisible = isVisible;
        _setVisibility = setVisibility;

        Text = "AgentTasktrayViewer 設定";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(390, 270);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "表示するアイコン",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        }, 0, 0);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 8, 0, 8)
        };
        _visibilityChecks = new[]
        {
            CreateVisibilityCheck("Codex 5時間", TrayIconKind.CodexFiveHour),
            CreateVisibilityCheck("Codex 週間", TrayIconKind.CodexWeekly),
            CreateVisibilityCheck("Claude 5時間", TrayIconKind.ClaudeFiveHour),
            CreateVisibilityCheck("Claude 週間", TrayIconKind.ClaudeWeekly)
        };
        checks.Controls.AddRange(_visibilityChecks);
        layout.Controls.Add(checks, 0, 1);

        layout.Controls.Add(new Label
        {
            Text = "4つとも非表示にすると、この設定画面を開くアプリアイコンを表示します。",
            AutoSize = true,
            MaximumSize = new Size(350, 0)
        }, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var update = new Button { Text = "今すぐ更新", AutoSize = true };
        update.Click += (_, _) => refresh();
        buttons.Controls.Add(close);
        buttons.Controls.Add(update);
        layout.Controls.Add(buttons, 0, 3);

        CancelButton = close;
        Controls.Add(layout);
    }

    private CheckBox CreateVisibilityCheck(string text, TrayIconKind kind)
    {
        var check = new CheckBox
        {
            Text = text,
            AutoSize = true,
            Checked = _isVisible(kind),
            Tag = kind,
            Margin = new Padding(0, 2, 0, 2)
        };
        check.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            bool applied = _setVisibility(kind, check.Checked);
            if (!applied)
            {
                _loading = true;
                check.Checked = !check.Checked;
                _loading = false;
            }
            SyncVisibilityChecks();
        };
        return check;
    }

    private void SyncVisibilityChecks()
    {
        _loading = true;
        try
        {
            foreach (CheckBox check in _visibilityChecks)
            {
                check.Checked = _isVisible((TrayIconKind)check.Tag!);
            }
        }
        finally
        {
            _loading = false;
        }
    }
}
