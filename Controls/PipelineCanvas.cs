using System.Drawing.Drawing2D;
using System.Drawing.Text;
using StarRailShaderEditor.Models;

namespace StarRailShaderEditor.Controls;

internal sealed class PipelineCanvas : Control
{
    private const float NodeWidth = 186f;
    private const float NodeHeight = 70f;
    private const float MinZoom = 0.75f;
    private const float MaxZoom = 1.8f;

    private readonly List<PipelineNode> _nodes = [];
    private readonly List<PipelineLink> _links = [];
    private readonly ContextMenuStrip _nodeMenu = new();
    private PointF _pan = new(54, 54);
    private float _zoom = 1f;
    private PipelineNode? _selected;
    private PipelineNode? _hovered;
    private PipelineNode? _dragging;
    private PointF _dragOffset;
    private bool _panning;
    private Point _panStart;
    private PointF _panOrigin;

    public bool SuspendResizePainting { get; set; }

    public PipelineCanvas()
    {
        DoubleBuffered = true;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        TabStop = true;
        Dock = DockStyle.Fill;
        Cursor = Cursors.Default;

        var toggleItem = new ToolStripMenuItem("启用或关闭阶段");
        toggleItem.Click += (_, _) => ToggleSelectedNode();
        _nodeMenu.Items.Add(toggleItem);
    }

    public event EventHandler<string>? GroupSelected;
    public event EventHandler<PipelineNode>? NodeToggleRequested;
    public event EventHandler? NodePositionsChanged;

    public IReadOnlyList<PipelineNode> Nodes => _nodes;

    public IReadOnlyDictionary<string, PointF> GetNodePositions() =>
        _nodes.ToDictionary(node => node.Id, node => node.Position, StringComparer.Ordinal);

    public void ApplyNodePositions(IReadOnlyDictionary<string, PointF> positions)
    {
        foreach (var node in _nodes)
            if (positions.TryGetValue(node.Id, out var position)) node.Position = position;
        Invalidate();
    }

    public void SetPipeline(IEnumerable<PipelineNode> nodes, IEnumerable<PipelineLink> links)
    {
        var positions = _nodes.ToDictionary(node => node.Id, node => node.Position, StringComparer.Ordinal);
        var selectedId = _selected?.Id;
        var firstLoad = _nodes.Count == 0;
        _nodes.Clear();
        _nodes.AddRange(nodes);
        foreach (var node in _nodes)
            if (positions.TryGetValue(node.Id, out var position)) node.Position = position;
        _links.Clear();
        _links.AddRange(links);
        _selected = _nodes.FirstOrDefault(node => node.Id == selectedId);
        if (firstLoad) FitToView();
        Invalidate();
    }

    public void SelectGroup(string group)
    {
        _selected = _nodes.FirstOrDefault(node => node.Group.Equals(group, StringComparison.OrdinalIgnoreCase));
        if (_selected is not null) Invalidate();
    }

    public void FitToView()
    {
        if (_nodes.Count == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var left = _nodes.Min(node => node.Position.X);
        var top = _nodes.Min(node => node.Position.Y);
        var right = _nodes.Max(node => node.Position.X + NodeWidth);
        var bottom = _nodes.Max(node => node.Position.Y + NodeHeight);
        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);
        _zoom = Math.Clamp(Math.Min((ClientSize.Width - 90f) / width, (ClientSize.Height - 90f) / height), MinZoom, 1.15f);
        _pan = new PointF(
            (ClientSize.Width - width * _zoom) * 0.5f - left * _zoom,
            (ClientSize.Height - height * _zoom) * 0.5f - top * _zoom);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!SuspendResizePainting && _nodes.Count > 0 && Width > 200 && Height > 160)
        {
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        DrawGrid(e.Graphics);

        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(_pan.X, _pan.Y);
        e.Graphics.ScaleTransform(_zoom, _zoom);
        foreach (var link in _links) DrawLink(e.Graphics, link);
        foreach (var node in _nodes) DrawNode(e.Graphics, node);
        e.Graphics.Restore(state);

        DrawViewportStatus(e.Graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var canvasPoint = ScreenToCanvas(e.Location);
        var node = HitTest(canvasPoint);

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Space)))
        {
            _panning = true;
            _panStart = e.Location;
            _panOrigin = _pan;
            Cursor = Cursors.Hand;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _selected = node;
            if (node is not null)
            {
                _dragging = node;
                _dragOffset = new PointF(canvasPoint.X - node.Position.X, canvasPoint.Y - node.Position.Y);
                GroupSelected?.Invoke(this, node.Group);
            }
            Invalidate();
        }
        else if (e.Button == MouseButtons.Right && node is not null)
        {
            _selected = node;
            GroupSelected?.Invoke(this, node.Group);
            _nodeMenu.Items[0].Enabled = node.CanToggle;
            _nodeMenu.Items[0].Text = node.Enabled ? "关闭此阶段" : "启用此阶段";
            _nodeMenu.Show(this, e.Location);
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            _pan = new PointF(_panOrigin.X + e.X - _panStart.X, _panOrigin.Y + e.Y - _panStart.Y);
            Invalidate();
            return;
        }

        var canvasPoint = ScreenToCanvas(e.Location);
        if (_dragging is not null)
        {
            _dragging.Position = new PointF(canvasPoint.X - _dragOffset.X, canvasPoint.Y - _dragOffset.Y);
            Invalidate();
            return;
        }

        var hovered = HitTest(canvasPoint);
        if (!ReferenceEquals(hovered, _hovered))
        {
            _hovered = hovered;
            Cursor = hovered is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var movedNode = _dragging is not null;
        _dragging = null;
        _panning = false;
        Cursor = _hovered is null ? Cursors.Default : Cursors.Hand;
        if (movedNode) NodePositionsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(ScreenToCanvas(e.Location)) is null)
        {
            FitToView();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var before = ScreenToCanvas(e.Location);
        var next = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1f : 0.9f), MinZoom, MaxZoom);
        if (Math.Abs(next - _zoom) < 0.001f) return;
        _zoom = next;
        _pan = new PointF(e.X - before.X * _zoom, e.Y - before.Y * _zoom);
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.D0))
        {
            FitToView();
            return true;
        }
        if (keyData == Keys.Space && _selected?.CanToggle == true)
        {
            ToggleSelectedNode();
            return true;
        }
        if (keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down && _nodes.Count > 0)
        {
            var current = _selected ?? _nodes[0];
            var candidates = _nodes.Where(node => !ReferenceEquals(node, current));
            _selected = keyData switch
            {
                Keys.Left => candidates.Where(node => node.Position.X < current.Position.X).OrderBy(node => current.Position.X - node.Position.X).FirstOrDefault(),
                Keys.Right => candidates.Where(node => node.Position.X > current.Position.X).OrderBy(node => node.Position.X - current.Position.X).FirstOrDefault(),
                Keys.Up => candidates.Where(node => node.Position.Y < current.Position.Y).OrderBy(node => current.Position.Y - node.Position.Y).FirstOrDefault(),
                _ => candidates.Where(node => node.Position.Y > current.Position.Y).OrderBy(node => node.Position.Y - current.Position.Y).FirstOrDefault(),
            } ?? current;
            GroupSelected?.Invoke(this, _selected.Group);
            Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleSelectedNode()
    {
        if (_selected?.CanToggle != true) return;
        _selected.Enabled = !_selected.Enabled;
        NodeToggleRequested?.Invoke(this, _selected);
        Invalidate();
    }

    private void DrawGrid(Graphics graphics)
    {
        using var minor = new Pen(Color.FromArgb(31, 255, 255, 255), 1f);
        using var major = new Pen(Color.FromArgb(48, 255, 255, 255), 1f);
        var step = 22f * _zoom;
        if (step < 8) return;
        var offsetX = _pan.X % step;
        var offsetY = _pan.Y % step;
        for (var x = offsetX; x < Width; x += step)
            graphics.DrawLine(((int)((x - offsetX) / step) % 4 == 0) ? major : minor, x, 0, x, Height);
        for (var y = offsetY; y < Height; y += step)
            graphics.DrawLine(((int)((y - offsetY) / step) % 4 == 0) ? major : minor, 0, y, Width, y);
    }

    private void DrawLink(Graphics graphics, PipelineLink link)
    {
        var source = _nodes.FirstOrDefault(node => node.Id == link.SourceId);
        var target = _nodes.FirstOrDefault(node => node.Id == link.TargetId);
        if (source is null || target is null) return;
        var start = new PointF(source.Position.X + NodeWidth, source.Position.Y + NodeHeight * 0.5f);
        var end = new PointF(target.Position.X, target.Position.Y + NodeHeight * 0.5f);
        var bend = Math.Max(48, Math.Abs(end.X - start.X) * 0.48f);
        var active = source.Enabled && target.Enabled;
        using var pen = new Pen(active ? Color.FromArgb(178, source.Color) : Color.FromArgb(72, Theme.TextMuted), active ? 2.2f : 1.4f);
        graphics.DrawBezier(pen, start, new PointF(start.X + bend, start.Y), new PointF(end.X - bend, end.Y), end);
    }

    private void DrawNode(Graphics graphics, PipelineNode node)
    {
        var bounds = new RectangleF(node.Position.X, node.Position.Y, NodeWidth, NodeHeight);
        var selected = ReferenceEquals(node, _selected);
        var hovered = ReferenceEquals(node, _hovered);
        using var path = RoundedRectangle(bounds, Theme.Radius);
        using var fill = new SolidBrush(node.Enabled ? (hovered ? Theme.SurfaceHover : Theme.SurfaceRaised) : Color.FromArgb(29, 30, 33));
        using var border = new Pen(selected ? Theme.Accent : (node.Enabled ? Theme.BorderStrong : Theme.Border), selected ? 2.2f : 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        using var strip = new SolidBrush(node.Enabled ? node.Color : Color.FromArgb(86, 86, 88));
        graphics.FillRectangle(strip, bounds.X, bounds.Y, 5, bounds.Height);
        using var titleBrush = new SolidBrush(node.Enabled ? Theme.Text : Theme.TextMuted);
        using var subBrush = new SolidBrush(Theme.TextMuted);
        graphics.DrawString(node.Title, Theme.UiFontMedium, titleBrush, bounds.X + 16, bounds.Y + 12);
        graphics.DrawString(node.Subtitle, Theme.UiFont, subBrush, new RectangleF(bounds.X + 16, bounds.Y + 37, bounds.Width - 42, 20));

        if (node.CanToggle)
        {
            var toggleBounds = new RectangleF(bounds.Right - 28, bounds.Y + 14, 14, 14);
            using var toggleBrush = new SolidBrush(node.Enabled ? Theme.Success : Theme.BorderStrong);
            graphics.FillEllipse(toggleBrush, toggleBounds);
            using var center = new SolidBrush(Theme.SurfaceRaised);
            graphics.FillEllipse(center, toggleBounds.X + 4, toggleBounds.Y + 4, 6, 6);
        }

        using var pinBrush = new SolidBrush(node.Enabled ? node.Color : Theme.BorderStrong);
        graphics.FillEllipse(pinBrush, bounds.X - 4, bounds.Y + bounds.Height / 2 - 4, 8, 8);
        graphics.FillEllipse(pinBrush, bounds.Right - 4, bounds.Y + bounds.Height / 2 - 4, 8, 8);
    }

    private void DrawViewportStatus(Graphics graphics)
    {
        var text = $"{Math.Round(_zoom * 100)}%";
        using var brush = new SolidBrush(Color.FromArgb(185, Theme.TextMuted));
        graphics.DrawString(text, Theme.UiFont, brush, 12, Height - 28);
    }

    private PipelineNode? HitTest(PointF point) => _nodes.LastOrDefault(node =>
        new RectangleF(node.Position.X, node.Position.Y, NodeWidth, NodeHeight).Contains(point));

    private PointF ScreenToCanvas(Point point) => new((point.X - _pan.X) / _zoom, (point.Y - _pan.Y) / _zoom);

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
