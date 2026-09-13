namespace AllyAutoTDP.UI;

internal sealed class AllyTrayContextMenu : ContextMenuStrip
{
    public AllyTrayContextMenu()
    {
        Renderer = new AllyTrayMenuRenderer();
        BackColor = QuickPanelPalette.Surface;
        ForeColor = QuickPanelPalette.Foreground;
        Font = QuickPanelTypography.Interface(9F);
        ShowImageMargin = false;
        ShowCheckMargin = true;
        Padding = new Padding(6);
        AutoSize = true;
        ItemAdded += (_, e) =>
        {
            if (e.Item is ToolStripMenuItem)
                e.Item.Padding = new Padding(8, 5, 8, 5);
        };
    }
}

internal sealed class AllyTrayMenuRenderer : ToolStripProfessionalRenderer
{
    public AllyTrayMenuRenderer()
        : base(new AllyTrayColorTable())
    {
    }

    protected override void OnRenderMenuItemBackground(
        ToolStripItemRenderEventArgs e)
    {
        Rectangle bounds = new(0, 0, e.Item.Width, e.Item.Height);
        Color background = e.Item.Selected && e.Item.Enabled
            ? QuickPanelPalette.Active
            : QuickPanelPalette.Surface;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, bounds);
    }

    protected override void OnRenderItemCheck(
        ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem { Checked: true })
            return;

        Color color = e.Item.Selected
            ? QuickPanelPalette.Foreground
            : QuickPanelPalette.Accent;
        Rectangle bounds = new(
            0,
            0,
            e.ImageRectangle.Right + e.ImageRectangle.X,
            e.Item.Height);
        TextRenderer.DrawText(
            e.Graphics,
            "✓",
            e.Item.Font,
            bounds,
            color,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);
    }

    protected override void OnRenderItemText(
        ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled)
            e.TextColor = QuickPanelPalette.Muted;
        else if (e.Item.Selected)
            e.TextColor = QuickPanelPalette.Foreground;
        else
            e.TextColor = QuickPanelPalette.Foreground;

        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(
        ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(QuickPanelPalette.Border);
        e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
    }

    protected override void OnRenderToolStripBackground(
        ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(QuickPanelPalette.Border);
        Rectangle bounds = new(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, bounds);
    }
}

internal sealed class AllyTrayColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => QuickPanelPalette.Border;
    public override Color MenuItemBorder => QuickPanelPalette.Selected;
    public override Color MenuItemSelected => QuickPanelPalette.Selected;
    public override Color MenuItemSelectedGradientBegin => QuickPanelPalette.Selected;
    public override Color MenuItemSelectedGradientEnd => QuickPanelPalette.Selected;
    public override Color ToolStripDropDownBackground => QuickPanelPalette.Surface;
    public override Color ImageMarginGradientBegin => QuickPanelPalette.Surface;
    public override Color ImageMarginGradientMiddle => QuickPanelPalette.Surface;
    public override Color ImageMarginGradientEnd => QuickPanelPalette.Surface;
}
