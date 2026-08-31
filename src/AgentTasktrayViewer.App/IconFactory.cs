using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal static class IconFactory
{
    public static Icon Create(UsageWindow window, Color serviceColor)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var path = RoundedRectangle(1, 1, 30, 30, 6))
        using (var background = new SolidBrush(window.IsAvailable ? serviceColor : Color.FromArgb(105, 105, 105)))
        using (var textBrush = new SolidBrush(Color.White))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.FillPath(background, path);

            int borderWidth = window.IsAvailable && window.UsedPercent >= 90 ? 3 : 2;
            Color borderColor = !window.IsAvailable
                ? serviceColor
                : window.UsedPercent >= 90
                    ? Color.FromArgb(220, 38, 38)
                    : window.UsedPercent >= 70
                        ? Color.FromArgb(245, 158, 11)
                        : Color.White;
            using var pen = new Pen(borderColor, borderWidth);
            graphics.DrawPath(pen, path);

            string label = window.IsAvailable ? $"{Math.Round(window.RemainingPercent):0}" : "?";
            float fontSize = label.Length >= 3 ? 10f : label.Length == 2 ? 13f : 16f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString(label, font, textBrush, new RectangleF(0, 0, 32, 32), format);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
