using System.Drawing.Drawing2D;

namespace AllyAutoTDP.UI;

internal enum QuickPanelIconKind
{
    None,
    Back,
    Battery,
    ChevronRight,
    Close,
    Gamepad,
    Gauge,
    Minus,
    Plus,
    Profiles,
    Power
}

internal static class QuickPanelIconPainter
{
    public static void Draw(
        Graphics graphics,
        QuickPanelIconKind kind,
        Rectangle bounds,
        Color color,
        float scale = 1f)
    {
        if (kind == QuickPanelIconKind.None || bounds.Width < 2 || bounds.Height < 2)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, Math.Max(1f, scale * 1.5f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        float centerX = bounds.Left + bounds.Width / 2f;
        float centerY = bounds.Top + bounds.Height / 2f;
        float half = Math.Min(bounds.Width, bounds.Height) * 0.32f;

        switch (kind)
        {
            case QuickPanelIconKind.Back:
                graphics.DrawLines(
                    pen,
                    [
                        new PointF(centerX + half * .65f, centerY - half),
                        new PointF(centerX - half * .45f, centerY),
                        new PointF(centerX + half * .65f, centerY + half)
                    ]);
                graphics.DrawLine(
                    pen,
                    centerX - half * .45f,
                    centerY,
                    centerX + half * 1.1f,
                    centerY);
                break;
            case QuickPanelIconKind.Battery:
                DrawBattery(graphics, pen, bounds);
                break;
            case QuickPanelIconKind.ChevronRight:
                graphics.DrawLines(
                    pen,
                    [
                        new PointF(centerX - half * .35f, centerY - half),
                        new PointF(centerX + half * .55f, centerY),
                        new PointF(centerX - half * .35f, centerY + half)
                    ]);
                break;
            case QuickPanelIconKind.Close:
                graphics.DrawLine(
                    pen,
                    centerX - half,
                    centerY - half,
                    centerX + half,
                    centerY + half);
                graphics.DrawLine(
                    pen,
                    centerX + half,
                    centerY - half,
                    centerX - half,
                    centerY + half);
                break;
            case QuickPanelIconKind.Gamepad:
                DrawGamepad(graphics, pen, bounds);
                break;
            case QuickPanelIconKind.Gauge:
                using (var arcPen = new Pen(pen.Color, pen.Width))
                {
                    arcPen.StartCap = LineCap.Round;
                    arcPen.EndCap = LineCap.Round;
                    graphics.DrawArc(
                        arcPen,
                        centerX - half,
                        centerY - half,
                        half * 2,
                        half * 2,
                        205,
                        130);
                }
                graphics.DrawLine(
                    pen,
                    centerX,
                    centerY,
                    centerX + half * .65f,
                    centerY - half * .55f);
                using (var dotBrush = new SolidBrush(color))
                {
                    graphics.FillEllipse(
                        dotBrush,
                        centerX - scale * 1.8f,
                        centerY - scale * 1.8f,
                        scale * 3.6f,
                        scale * 3.6f);
                }
                break;
            case QuickPanelIconKind.Minus:
                graphics.DrawLine(
                    pen,
                    centerX - half * .7f,
                    centerY,
                    centerX + half * .7f,
                    centerY);
                break;
            case QuickPanelIconKind.Plus:
                graphics.DrawLine(
                    pen,
                    centerX - half * .7f,
                    centerY,
                    centerX + half * .7f,
                    centerY);
                graphics.DrawLine(
                    pen,
                    centerX,
                    centerY - half * .7f,
                    centerX,
                    centerY + half * .7f);
                break;
            case QuickPanelIconKind.Power:
                graphics.DrawArc(
                    pen,
                    centerX - half,
                    centerY - half,
                    half * 2,
                    half * 2,
                    35,
                    290);
                graphics.DrawLine(
                    pen,
                    centerX,
                    centerY - half * 1.2f,
                    centerX,
                    centerY + half * .1f);
                break;
            case QuickPanelIconKind.Profiles:
                DrawProfiles(graphics, pen, bounds);
                break;
        }
    }

    private static void DrawBattery(
        Graphics graphics,
        Pen pen,
        Rectangle bounds)
    {
        float left = bounds.Left + bounds.Width * .2f;
        float top = bounds.Top + bounds.Height * .3f;
        float width = bounds.Width * .55f;
        float height = bounds.Height * .4f;
        graphics.DrawRectangle(pen, left, top, width, height);
        graphics.DrawLine(
            pen,
            left + width,
            top + height * .3f,
            left + width + bounds.Width * .12f,
            top + height * .3f);
        graphics.DrawLine(
            pen,
            left + width,
            top + height * .7f,
            left + width + bounds.Width * .12f,
            top + height * .7f);
    }

    private static void DrawGamepad(
        Graphics graphics,
        Pen pen,
        Rectangle bounds)
    {
        using var path = new GraphicsPath();
        float left = bounds.Left + bounds.Width * .15f;
        float top = bounds.Top + bounds.Height * .32f;
        float right = bounds.Right - bounds.Width * .15f;
        float bottom = bounds.Bottom - bounds.Height * .25f;
        path.AddArc(left, top, bounds.Width * .2f, bounds.Height * .6f, 90, 180);
        path.AddArc(right - bounds.Width * .2f, top, bounds.Width * .2f, bounds.Height * .6f, 270, 180);
        path.AddLine(right - bounds.Width * .1f, bottom, left + bounds.Width * .1f, bottom);
        path.CloseFigure();
        graphics.DrawPath(pen, path);

        float centerY = top + (bottom - top) * .48f;
        graphics.DrawLine(pen, left + bounds.Width * .23f, centerY, left + bounds.Width * .4f, centerY);
        graphics.DrawLine(pen, left + bounds.Width * .315f, centerY - bounds.Height * .09f, left + bounds.Width * .315f, centerY + bounds.Height * .09f);
        using var buttonBrush = new SolidBrush(pen.Color);
        graphics.FillEllipse(buttonBrush, right - bounds.Width * .35f, centerY - bounds.Height * .06f, bounds.Width * .12f, bounds.Height * .12f);
    }

    private static void DrawProfiles(
        Graphics graphics,
        Pen pen,
        Rectangle bounds)
    {
        float left = bounds.Left + bounds.Width * .2f;
        float right = bounds.Right - bounds.Width * .2f;
        float first = bounds.Top + bounds.Height * .3f;
        float gap = bounds.Height * .2f;
        for (int index = 0; index < 3; index++)
        {
            float y = first + gap * index;
            graphics.DrawLine(pen, left, y, right, y);
            using var markerBrush = new SolidBrush(pen.Color);
            graphics.FillEllipse(
                markerBrush,
                left + (index % 2 == 0 ? bounds.Width * .2f : bounds.Width * .5f),
                y - bounds.Height * .06f,
                bounds.Width * .12f,
                bounds.Height * .12f);
        }
    }
}
