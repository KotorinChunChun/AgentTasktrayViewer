using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal static class IconFactory
{
    public static readonly IReadOnlyList<string> RecommendedFontFamilies = new[]
    {
        "Segoe UI",
        "Arial",
        "Arial Narrow",
        "Bahnschrift"
    };

    public static IReadOnlyList<string> GetAvailableRecommendedFontFamilies()
    {
        using var installedFonts = new InstalledFontCollection();
        var installedNames = installedFonts.Families
            .Select(family => family.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return RecommendedFontFamilies
            .Where(installedNames.Contains)
            .ToArray();
    }

    public static Icon Create(
        UsageWindow window,
        Color serviceColor,
        TrayIconVisualStyle visualStyle,
        string fontFamilyName)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            bool coloredBackground = visualStyle == TrayIconVisualStyle.ColoredBackground;
            bool warning = window.IsAvailable && window.UsedPercent >= 70;
            float borderWidth = window.IsAvailable && window.UsedPercent >= 90 ? 3f : 2f;
            RectangleF textBounds;

            using (GraphicsPath iconPath = RoundedRectangle(1, 1, 30, 30, 5))
            {
                if (coloredBackground)
                {
                    using var background = new SolidBrush(window.IsAvailable ? serviceColor : Color.FromArgb(105, 105, 105));
                    graphics.FillPath(background, iconPath);
                }

                if (coloredBackground || warning)
                {
                    Color borderColor = !window.IsAvailable
                        ? serviceColor
                        : window.UsedPercent >= 90
                            ? Color.FromArgb(220, 38, 38)
                            : window.UsedPercent >= 70
                                ? Color.FromArgb(245, 158, 11)
                                : Color.White;
                    using var pen = new Pen(borderColor, borderWidth);
                    graphics.DrawPath(pen, iconPath);
                }

                float inset = coloredBackground || warning ? borderWidth + 0.5f : 0.25f;
                textBounds = RectangleF.Inflate(new RectangleF(0, 0, 32, 32), -inset, -inset);
            }

            string label = window.IsAvailable ? $"{Math.Round(window.RemainingPercent):0}" : "?";
            Color textColor = coloredBackground
                ? Color.White
                : window.IsAvailable ? serviceColor : Color.FromArgb(105, 105, 105);
            using var textBrush = new SolidBrush(textColor);
            using var fontFamily = ResolveFontFamily(fontFamilyName);
            using GraphicsPath textPath = CreateMaximizedTextPath(label, fontFamily, textBounds);
            graphics.FillPath(textBrush, textPath);
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

    private static FontFamily ResolveFontFamily(string fontFamilyName)
    {
        try
        {
            return new FontFamily(fontFamilyName);
        }
        catch (ArgumentException)
        {
            return new FontFamily("Segoe UI");
        }
    }

    private static GraphicsPath CreateMaximizedTextPath(string text, FontFamily fontFamily, RectangleF target)
    {
        FontStyle style = fontFamily.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap;

        const float initialSize = 32f;
        using var measurementPath = new GraphicsPath();
        measurementPath.AddString(text, fontFamily, (int)style, initialSize, PointF.Empty, format);
        RectangleF measured = measurementPath.GetBounds();
        if (measured.Width <= 0 || measured.Height <= 0)
        {
            return new GraphicsPath();
        }

        float scale = Math.Min(target.Width / measured.Width, target.Height / measured.Height);
        float fittedSize = initialSize * scale;
        var result = new GraphicsPath();
        result.AddString(text, fontFamily, (int)style, fittedSize, PointF.Empty, format);
        RectangleF fitted = result.GetBounds();
        using var matrix = new Matrix();
        matrix.Translate(
            target.Left + ((target.Width - fitted.Width) / 2f) - fitted.Left,
            target.Top + ((target.Height - fitted.Height) / 2f) - fitted.Top);
        result.Transform(matrix);
        return result;
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
