using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapAsk;

/// Freezes the whole (virtual) desktop, dims it, and lets you drag out a rectangle. Esc / right-click cancels.
public sealed class RegionSelector : Form
{
    private readonly Bitmap screen;
    private readonly Rectangle virtualBounds;
    private Point start;
    private Rectangle selection;
    private bool dragging;

    public Bitmap? Result { get; private set; }
    /// Where the drag ended, in screen pixels (used to place the popup).
    public Point EndPoint { get; private set; }

    private RegionSelector(Bitmap screen, Rectangle bounds)
    {
        this.screen = screen;
        virtualBounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
    }

    public static (Bitmap image, Point end)? Pick()
    {
        Rectangle vs = SystemInformation.VirtualScreen;
        var full = new Bitmap(vs.Width, vs.Height);
        using (var g = Graphics.FromImage(full))
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
        using var f = new RegionSelector(full, vs);
        f.ShowDialog();
        full.Dispose();
        return f.Result is { } r ? (r, f.EndPoint) : null;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = virtualBounds;   // re-apply after DPI scaling on multi-monitor setups
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Close(); return; }
        dragging = true;
        start = e.Location;
        selection = new Rectangle(e.Location, Size.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) return;
        selection = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        if (selection.Width >= 4 && selection.Height >= 4)
        {
            Result = screen.Clone(selection, screen.PixelFormat);
            EndPoint = new Point(virtualBounds.Left + e.X, virtualBounds.Top + e.Y);
        }
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(screen, 0, 0);
        using var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        using var region = new Region(ClientRectangle);
        if (selection.Width > 0) region.Exclude(selection);
        g.FillRegion(dim, region);
        if (selection.Width > 0)
        {
            using var pen = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, selection);
            string size = $"{selection.Width} × {selection.Height}";
            g.DrawString(size, SystemFonts.MessageBoxFont!, Brushes.White, selection.Left, Math.Max(0, selection.Top - 20));
        }
        else
        {
            g.DrawString("Drag to snapshot an area — Esc to cancel", new Font("Segoe UI", 14), Brushes.White, 24, 24);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { /* everything is drawn in OnPaint */ }
}
