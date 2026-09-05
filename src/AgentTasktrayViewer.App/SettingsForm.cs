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
        Action refresh,
        TrayIconVisualStyle initialVisualStyle,
        string initialFontFamily,
        IReadOnlyList<string> availableFontFamilies,
        Action<TrayIconVisualStyle> setVisualStyle,
        Action<string> setFontFamily)
    {
        _isVisible = isVisible;
        _setVisibility = setVisibility;

        Text = "AgentTasktrayViewer 設定";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(420, 430);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 8
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
            Text = "アイコンスタイル",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        }, 0, 2);

        var styles = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 8)
        };
        var backgroundStyle = new RadioButton
        {
            Text = "背景色版",
            AutoSize = true,
            Checked = initialVisualStyle == TrayIconVisualStyle.ColoredBackground
        };
        var textStyle = new RadioButton
        {
            Text = "文字色版（背景なし）",
            AutoSize = true,
            Checked = initialVisualStyle == TrayIconVisualStyle.ColoredText
        };
        backgroundStyle.CheckedChanged += (_, _) =>
        {
            if (backgroundStyle.Checked)
            {
                setVisualStyle(TrayIconVisualStyle.ColoredBackground);
            }
        };
        textStyle.CheckedChanged += (_, _) =>
        {
            if (textStyle.Checked)
            {
                setVisualStyle(TrayIconVisualStyle.ColoredText);
            }
        };
        styles.Controls.Add(backgroundStyle);
        styles.Controls.Add(textStyle);
        layout.Controls.Add(styles, 0, 3);

        layout.Controls.Add(new Label
        {
            Text = "アイコンフォント",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        }, 0, 4);

        var fontSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 240,
            Margin = new Padding(0, 4, 0, 8)
        };
        foreach (string fontFamily in availableFontFamilies)
        {
            fontSelector.Items.Add(fontFamily);
        }
        if (!fontSelector.Items.Contains(initialFontFamily))
        {
            fontSelector.Items.Add(initialFontFamily);
        }
        fontSelector.SelectedItem = initialFontFamily;
        fontSelector.SelectedIndexChanged += (_, _) =>
        {
            if (fontSelector.SelectedItem is string fontFamily)
            {
                setFontFamily(fontFamily);
            }
        };
        layout.Controls.Add(fontSelector, 0, 5);

        layout.Controls.Add(new Label
        {
            Text = "変更はトレイアイコンへすぐに反映されます。4つとも非表示にすると、この設定画面を開くアプリアイコンを表示します。",
            AutoSize = true,
            MaximumSize = new Size(380, 0)
        }, 0, 6);

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
        layout.Controls.Add(buttons, 0, 7);

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
