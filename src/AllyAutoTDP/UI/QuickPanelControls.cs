using System.Drawing.Drawing2D;

namespace AllyAutoTDP.UI;

internal sealed class QuickPanelSurface : Panel
{
    public QuickPanelSurface()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = QuickPanelPalette.Surface;
    }

    public bool ShowAccent { get; set; }

    public Color AccentColor { get; set; } = QuickPanelPalette.Cyan;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 2 || Height < 2)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = QuickPanelButton.RoundedRect(bounds, 5);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
        using var border = new Pen(QuickPanelPalette.Border);
        e.Graphics.DrawPath(border, path);

        if (ShowAccent)
        {
            using var accent = new SolidBrush(AccentColor);
            e.Graphics.FillRectangle(accent, 0, 6, 3, Math.Max(1, Height - 12));
        }
    }
}

internal class QuickPanelButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public QuickPanelButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = QuickPanelPalette.Surface;
        ForeColor = QuickPanelPalette.Foreground;
        BorderColor = QuickPanelPalette.Border;
        AccentColor = QuickPanelPalette.Amber;
        Font = QuickPanelTypography.Interface(9F);
        Height = QuickPanelMetrics.LogicalTouchTarget;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        MouseEnter += (_, _) =>
        {
            _hovered = true;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
        };
    }

    public QuickPanelIconKind IconKind { get; set; }

    public Color IconColor { get; set; } = QuickPanelPalette.Foreground;

    public Color BorderColor { get; set; }

    public Color AccentColor { get; set; }

    public bool Emphasized { get; set; }

    public bool Borderless { get; set; }

    public bool Active { get; set; }

    public bool Danger { get; set; }

    public bool VerticalContent { get; set; }

    public bool CenterIconOnly { get; set; }

    public bool CenterContent { get; set; }

    public bool AllowTextEllipsis { get; set; }

    protected override bool ShowFocusCues => false;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 2 || Height < 2)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        Color background = Borderless
            ? (Active ? QuickPanelPalette.Active : Parent?.BackColor ?? QuickPanelPalette.Panel)
            : BackColor;
        if ((_hovered || _pressed) && Enabled)
            background = QuickPanelPalette.Active;
        if (!Enabled)
            background = Color.FromArgb(120, background);

        using GraphicsPath path = RoundedRect(bounds, 4);
        using (var brush = new SolidBrush(background))
            e.Graphics.FillPath(brush, path);

        if (!Borderless)
        {
            Color border = Danger
                ? QuickPanelPalette.Danger
                : (Emphasized ? AccentColor : BorderColor);
            if (!Enabled)
                border = Color.FromArgb(100, border);
            using var pen = new Pen(border, Focused ? 2F : 1F);
            e.Graphics.DrawPath(pen, path);
        }

        Color contentColor = Enabled ? (Danger ? QuickPanelPalette.Danger : ForeColor) : QuickPanelPalette.Muted;
        DrawContent(e.Graphics, contentColor);
    }

    private void DrawContent(Graphics graphics, Color contentColor)
    {
        int iconSize = Math.Min(24, Math.Max(18, Height - 12));
        bool hasIcon = IconKind != QuickPanelIconKind.None;
        Rectangle textBounds;
        if (CenterIconOnly && hasIcon && string.IsNullOrEmpty(Text))
        {
            Rectangle iconBounds = new((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize);
            QuickPanelIconPainter.Draw(graphics, IconKind, iconBounds, IconColor, DeviceDpi / 96F);
            return;
        }

        if (CenterContent && hasIcon && !VerticalContent)
        {
            Size textSize = TextRenderer.MeasureText(
                graphics,
                Text,
                Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int gap = string.IsNullOrEmpty(Text) ? 0 : 8;
            int contentWidth = iconSize + gap + textSize.Width;
            int start = Math.Max(0, (Width - contentWidth) / 2);
            Rectangle iconBounds = new(start, (Height - iconSize) / 2, iconSize, iconSize);
            QuickPanelIconPainter.Draw(graphics, IconKind, iconBounds, IconColor, DeviceDpi / 96F);
            textBounds = new(
                iconBounds.Right + gap,
                0,
                Math.Max(1, textSize.Width),
                Height);
        }
        else if (VerticalContent)
        {
            Rectangle iconBounds = new((Width - iconSize) / 2, 7, iconSize, iconSize);
            QuickPanelIconPainter.Draw(graphics, IconKind, iconBounds, IconColor, DeviceDpi / 96F);
            textBounds = new(2, iconBounds.Bottom + 1, Math.Max(1, Width - 4), Math.Max(1, Height - iconBounds.Bottom - 2));
        }
        else if (hasIcon)
        {
            Rectangle iconBounds = new(12, (Height - iconSize) / 2, iconSize, iconSize);
            QuickPanelIconPainter.Draw(graphics, IconKind, iconBounds, IconColor, DeviceDpi / 96F);
            textBounds = new(iconBounds.Right + 8, 0, Math.Max(1, Width - iconBounds.Right - 16), Height);
        }
        else
        {
            textBounds = ClientRectangle;
        }

        TextFormatFlags flags = TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoClipping;
        if (AllowTextEllipsis)
            flags |= TextFormatFlags.EndEllipsis;
        flags |= VerticalContent || !hasIcon
            ? TextFormatFlags.HorizontalCenter
            : TextFormatFlags.Left;
        TextRenderer.DrawText(graphics, Text, Font, textBounds, contentColor, flags);
    }

    internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        GraphicsPath path = new();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class QuickPanelProfileRow : Button
{
    private bool _hovered;

    public QuickPanelProfileRow(string displayName, string executablePath, bool isCurrent)
    {
        DisplayName = displayName;
        ExecutablePath = executablePath;
        IsCurrent = isCurrent;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = QuickPanelPalette.Surface;
        ForeColor = QuickPanelPalette.Foreground;
        Font = QuickPanelTypography.Interface(9F);
        Height = QuickPanelMetrics.Scale(72, 96);
        Dock = DockStyle.Top;
        Margin = new Padding(0);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ListItem;
        AccessibleName = displayName;
        MouseEnter += (_, _) =>
        {
            _hovered = true;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            Invalidate();
        };
    }

    public string DisplayName { get; }

    public string ExecutablePath { get; }

    public bool IsCurrent { get; }

    protected override bool ShowFocusCues => false;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        Color background = _hovered ? QuickPanelPalette.Active : QuickPanelPalette.Surface;
        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, ClientRectangle);

        if (IsCurrent)
        {
            using var currentBrush = new SolidBrush(QuickPanelPalette.Cyan);
            e.Graphics.FillRectangle(currentBrush, 0, 8, 3, Math.Max(1, Height - 16));
        }

        Rectangle textBounds = new(16, 0, Math.Max(1, Width - 58), Height - 1);
        TextRenderer.DrawText(
            e.Graphics,
            DisplayName,
            Font,
            textBounds,
            ForeColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        Rectangle chevronBounds = new(Math.Max(0, Width - 40), 0, 28, Height - 1);
        QuickPanelIconPainter.Draw(
            e.Graphics,
            QuickPanelIconKind.ChevronRight,
            chevronBounds,
            QuickPanelPalette.Muted,
            DeviceDpi / 96F);

        using var pen = new Pen(QuickPanelPalette.Border);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

internal sealed class QuickPanelToggle : Button
{
    private bool _checked;
    private bool _hovered;

    public QuickPanelToggle(string accessibleName = "AutoTDP")
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = QuickPanelPalette.Surface;
        Width = QuickPanelMetrics.Scale(58, 96);
        Height = QuickPanelMetrics.Scale(30, 96);
        MinimumSize = new Size(
            QuickPanelMetrics.Scale(58, 96),
            QuickPanelMetrics.Scale(30, 96));
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = accessibleName;
        TabStop = true;
        MouseEnter += (_, _) =>
        {
            _hovered = true;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            Invalidate();
        };
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 2 || Height < 2)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rail = new(2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
        using (var trackBrush = new SolidBrush(
                   _checked ? QuickPanelPalette.Cyan : QuickPanelPalette.Active))
        using (var borderPen = new Pen(_hovered ? QuickPanelPalette.Foreground : QuickPanelPalette.Border))
        using (GraphicsPath path = QuickPanelButton.RoundedRect(rail, rail.Height / 2))
        {
            e.Graphics.FillPath(trackBrush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        int diameter = Math.Max(12, rail.Height - 6);
        int x = _checked ? rail.Right - diameter - 3 : rail.Left + 3;
        using var thumb = new SolidBrush(
            _checked ? QuickPanelPalette.Panel : QuickPanelPalette.Muted);
        e.Graphics.FillEllipse(
            thumb,
            x,
            rail.Top + (rail.Height - diameter) / 2,
            diameter,
            diameter);
    }
}

internal sealed class QuickPanelStepper : Control
{
    private static readonly int[] Values = [30, 40, 45, 60, 90, 120];
    private int _value = 60;
    private bool _hovered;

    public QuickPanelStepper()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = QuickPanelPalette.Surface;
        ForeColor = QuickPanelPalette.Foreground;
        Font = QuickPanelTypography.Metrics(14F);
        Height = QuickPanelMetrics.Scale(60, 96);
        MinimumSize = new Size(QuickPanelMetrics.Scale(144, 96), QuickPanelMetrics.Scale(48, 96));
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.SpinButton;
        AccessibleName = "FPS cible";
        MouseEnter += (_, _) =>
        {
            _hovered = true;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            Invalidate();
        };
    }

    public event EventHandler? ValueChanged;

    public IReadOnlyList<int> AllowedValues => Values;

    public int Value
    {
        get => _value;
        set
        {
            int normalized = Normalize(value);
            if (_value == normalized)
                return;

            _value = normalized;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StepDown() => SetIndex(GetIndex() - 1);

    public void StepUp() => SetIndex(GetIndex() + 1);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
        {
            StepDown();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
        {
            StepUp();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Home)
        {
            Value = Values[0];
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.End)
        {
            Value = Values[^1];
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !Enabled)
            return;

        int buttonWidth = Math.Max(48, Height);
        if (e.X <= buttonWidth)
            StepDown();
        else if (e.X >= Width - buttonWidth)
            StepUp();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 2 || Height < 2)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = QuickPanelButton.RoundedRect(bounds, 4);
        using (var brush = new SolidBrush(Enabled ? BackColor : QuickPanelPalette.Active))
            e.Graphics.FillPath(brush, path);
        using (var border = new Pen(_hovered ? QuickPanelPalette.Amber : QuickPanelPalette.Border))
            e.Graphics.DrawPath(border, path);

        int sideWidth = Math.Max(48, Height);
        using var separator = new Pen(QuickPanelPalette.Border);
        e.Graphics.DrawLine(separator, sideWidth, 8, sideWidth, Height - 8);
        e.Graphics.DrawLine(separator, Width - sideWidth, 8, Width - sideWidth, Height - 8);

        QuickPanelIconPainter.Draw(
            e.Graphics,
            QuickPanelIconKind.Minus,
            new Rectangle(0, 0, sideWidth, Height),
            QuickPanelPalette.Foreground,
            DeviceDpi / 96F);
        QuickPanelIconPainter.Draw(
            e.Graphics,
            QuickPanelIconKind.Plus,
            new Rectangle(Width - sideWidth, 0, sideWidth, Height),
            QuickPanelPalette.Foreground,
            DeviceDpi / 96F);

        Rectangle valueBounds = new(sideWidth + 4, 0, Math.Max(1, Width - sideWidth * 2 - 8), Height);
        TextRenderer.DrawText(
            e.Graphics,
            $"{Value} FPS",
            Font,
            valueBounds,
            Enabled ? ForeColor : QuickPanelPalette.Muted,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix);
    }

    private static int Normalize(int value)
    {
        int nearest = Values[0];
        int distance = Math.Abs(value - nearest);
        foreach (int candidate in Values)
        {
            int candidateDistance = Math.Abs(value - candidate);
            if (candidateDistance < distance)
            {
                nearest = candidate;
                distance = candidateDistance;
            }
        }

        return nearest;
    }

    private int GetIndex() => Array.IndexOf(Values, _value);

    private void SetIndex(int index)
    {
        Value = Values[Math.Clamp(index, 0, Values.Length - 1)];
    }
}
