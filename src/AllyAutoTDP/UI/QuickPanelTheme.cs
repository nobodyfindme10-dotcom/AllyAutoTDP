using System.Drawing.Text;

namespace AllyAutoTDP.UI;

internal static class QuickPanelPalette
{
    public static readonly Color Backdrop = Color.FromArgb(0x07, 0x0A, 0x0B);
    public static readonly Color Panel = Color.FromArgb(0x0D, 0x12, 0x13);
    public static readonly Color Surface = Color.FromArgb(0x15, 0x1C, 0x1E);
    public static readonly Color Active = Color.FromArgb(0x20, 0x29, 0x2B);
    public static readonly Color Border = Color.FromArgb(0x30, 0x3B, 0x3D);
    public static readonly Color Foreground = Color.FromArgb(0xF0, 0xF4, 0xF1);
    public static readonly Color Muted = Color.FromArgb(0xA9, 0xB4, 0xB2);
    public static readonly Color Cyan = Color.FromArgb(0x68, 0xCD, 0xD0);
    public static readonly Color Amber = Color.FromArgb(0xF1, 0xB3, 0x4B);
    public static readonly Color Danger = Color.FromArgb(0xFF, 0x70, 0x65);

    // Compatibility aliases for the existing tray renderer and helpers.
    public static Color Background => Backdrop;
    public static Color Control => Surface;
    public static Color Selected => Active;
    public static Color Accent => Cyan;
}

internal static class QuickPanelMetrics
{
    public const int LogicalPanelWidth = 364;
    public const int LogicalMinimumWidth = 300;
    public const int LogicalMaximumWidth = 420;
    public const int LogicalHeaderHeight = 60;
    public const int LogicalNavigationHeight = 58;
    public const int LogicalTouchTarget = 48;
    public const int LogicalContentPadding = 16;
    public const int LogicalSectionGap = 24;

    public static int Scale(int logicalPixels, int deviceDpi) =>
        Math.Max(1, (int)Math.Round(
            logicalPixels * Math.Max(deviceDpi, 96) / 96f));
}

internal static class QuickPanelTypography
{
    private static readonly HashSet<string> InstalledFontNames =
        new(
            new InstalledFontCollection()
                .Families
                .Select(family => family.Name),
            StringComparer.OrdinalIgnoreCase);

    public static Font Interface(float size, FontStyle style = FontStyle.Regular) =>
        new(Resolve("Commissioner", "Segoe UI"), size, style);

    public static Font Metrics(float size, FontStyle style = FontStyle.Regular) =>
        new(Resolve("Fragment Mono", "Consolas"), size, style);

    private static string Resolve(string preferred, string fallback) =>
        InstalledFontNames.Contains(preferred) ? preferred : fallback;
}
