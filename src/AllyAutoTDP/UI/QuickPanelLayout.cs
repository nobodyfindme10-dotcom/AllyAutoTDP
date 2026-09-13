namespace AllyAutoTDP.UI;

public static class QuickPanelLayout
{
    public static Rectangle Calculate(Rectangle workingArea, int deviceDpi)
    {
        if (workingArea.Width <= 0 || workingArea.Height <= 0)
            return Rectangle.Empty;

        int minimumWidth = QuickPanelMetrics.Scale(
            QuickPanelMetrics.LogicalMinimumWidth,
            deviceDpi);
        int preferredWidth = QuickPanelMetrics.Scale(
            QuickPanelMetrics.LogicalPanelWidth,
            deviceDpi);
        int maximumWidth = QuickPanelMetrics.Scale(
            QuickPanelMetrics.LogicalMaximumWidth,
            deviceDpi);
        int width = Math.Min(preferredWidth, workingArea.Width);

        if (workingArea.Width >= minimumWidth)
        {
            width = Math.Clamp(width, minimumWidth, maximumWidth);
            width = Math.Min(width, workingArea.Width);
        }

        int height = workingArea.Height;
        int left = workingArea.Right - width;
        int top = workingArea.Top;

        return new Rectangle(left, top, width, height);
    }
}
