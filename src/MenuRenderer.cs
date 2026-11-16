using System.Drawing.Drawing2D;

namespace SpotifyToast;

/// <summary>Menu do tray plano, no tema claro ou escuro, com check verde.</summary>
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    readonly bool dark;
    public Color Background { get; }
    readonly Color hover, text, disabled, separator;

    public MenuRenderer(bool dark)
    {
        this.dark = dark;
        RoundedEdges = false;
        Background = dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(249, 249, 251);
        hover = dark ? Color.FromArgb(52, 52, 58) : Color.FromArgb(228, 228, 233);
        text = dark ? Color.FromArgb(240, 240, 242) : Color.FromArgb(24, 24, 26);
        disabled = dark ? Color.FromArgb(140, 140, 146) : Color.FromArgb(130, 130, 136);
        separator = dark ? Color.FromArgb(58, 58, 64) : Color.FromArgb(222, 222, 228);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Background);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { /* borda fica com o DWM */ }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Fx.RoundedRect(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 4);
        using var b = new SolidBrush(hover);
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? text : disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = dark ? Color.FromArgb(200, 200, 205) : Color.FromArgb(70, 70, 75);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(separator);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        float u = Math.Min(r.Width, r.Height) / 16f;
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        using var pen = new Pen(Color.FromArgb(30, 215, 96), 2f * u)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, new[]
        {
            new PointF(cx - 4.5f * u, cy + 0.2f * u),
            new PointF(cx - 1.4f * u, cy + 3.2f * u),
            new PointF(cx + 4.8f * u, cy - 3.6f * u),
        });
    }
}
