using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace StarRailShaderEditor.Controls;

internal sealed class CodePreviewControl : UserControl
{
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int EmLineScroll = 0x00B6;

    private readonly RichTextBox _editor = new();
    private readonly Panel _gutter = new();
    private readonly TextBox _findBox = new();
    private readonly Label _changeLabel = new();
    private readonly HashSet<int> _changedLines = [];
    private int _selectedLine = -1;
    private string _currentText = string.Empty;
    private string _originalText = string.Empty;

    public CodePreviewControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Canvas;

        var findBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Surface, Padding = new Padding(10, 5, 10, 5) };
        var findLabel = new Label
        {
            Text = "查找",
            AutoSize = false,
            Width = 42,
            Dock = DockStyle.Left,
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _changeLabel.Dock = DockStyle.Right;
        _changeLabel.Width = 112;
        _changeLabel.ForeColor = Theme.Warning;
        _changeLabel.TextAlign = ContentAlignment.MiddleRight;
        _findBox.Dock = DockStyle.Fill;
        _findBox.BackColor = Theme.SurfaceRaised;
        _findBox.ForeColor = Theme.Text;
        _findBox.BorderStyle = BorderStyle.FixedSingle;
        _findBox.AccessibleName = "在源码中查找";
        _findBox.KeyDown += FindBoxOnKeyDown;
        findBar.Controls.Add(_findBox);
        findBar.Controls.Add(_changeLabel);
        findBar.Controls.Add(findLabel);

        _gutter.Dock = DockStyle.Left;
        _gutter.Width = 58;
        _gutter.BackColor = Theme.Surface;
        _gutter.Paint += DrawGutter;

        _editor.Dock = DockStyle.Fill;
        _editor.ReadOnly = true;
        _editor.BackColor = Theme.Canvas;
        _editor.ForeColor = Theme.Text;
        _editor.Font = Theme.MonoFont;
        _editor.BorderStyle = BorderStyle.None;
        _editor.WordWrap = false;
        _editor.HideSelection = false;
        _editor.DetectUrls = false;
        _editor.VScroll += (_, _) => _gutter.Invalidate();
        _editor.Resize += (_, _) => _gutter.Invalidate();

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        body.Controls.Add(_editor);
        body.Controls.Add(_gutter);
        Controls.Add(body);
        Controls.Add(findBar);
    }

    public int ModifiedLineCount => _changedLines.Count;

    public void SetDocument(string currentText, string originalText, int selectedLine = -1)
    {
        if (_currentText.Equals(currentText, StringComparison.Ordinal) &&
            _originalText.Equals(originalText, StringComparison.Ordinal) && _selectedLine == selectedLine) return;
        var firstVisible = IsHandleCreated ? SendMessage(_editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
        var selection = _editor.SelectionStart;
        var oldSelectedLine = _selectedLine;
        var oldChangedLines = _changedLines.ToArray();
        var affected = new HashSet<int>(oldChangedLines);
        _selectedLine = selectedLine;
        var fullRefresh = !TryApplyLineChanges(_currentText, currentText, affected);
        if (fullRefresh && !_editor.Text.Equals(currentText, StringComparison.Ordinal)) _editor.Text = currentText;
        _currentText = currentText;
        _originalText = originalText;
        ComputeChangedLines(currentText, originalText);
        affected.UnionWith(_changedLines);
        if (oldSelectedLine >= 0) affected.Add(oldSelectedLine);
        if (_selectedLine >= 0) affected.Add(_selectedLine);
        if (fullRefresh) ApplyHighlighting();
        else foreach (var line in affected) ApplyLineHighlight(line);
        _editor.SelectionStart = Math.Min(selection, _editor.TextLength);
        var nowVisible = SendMessage(_editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
        SendMessage(_editor.Handle, EmLineScroll, IntPtr.Zero, (IntPtr)(firstVisible - nowVisible));
        _changeLabel.Text = _changedLines.Count == 0 ? "无未保存修改" : $"{_changedLines.Count} 行已修改";
        _changeLabel.ForeColor = _changedLines.Count == 0 ? Theme.TextMuted : Theme.Warning;
        _gutter.Invalidate();
    }

    public void SetPlainText(string text)
    {
        SetDocument(text, text);
    }

    public void RevealLine(int zeroBasedLine)
    {
        if (_editor.Lines.Length == 0) return;
        _selectedLine = Math.Clamp(zeroBasedLine, 0, _editor.Lines.Length - 1);
        ApplyHighlighting();
        var character = _editor.GetFirstCharIndexFromLine(_selectedLine);
        if (character >= 0)
        {
            _editor.SelectionStart = character;
            _editor.SelectionLength = 0;
            _editor.ScrollToCaret();
        }
        _editor.Focus();
        _gutter.Invalidate();
    }

    public void FocusFind()
    {
        _findBox.Focus();
        _findBox.SelectAll();
    }

    private void ComputeChangedLines(string currentText, string originalText)
    {
        _changedLines.Clear();
        var current = Regex.Split(currentText, "\r?\n");
        var original = Regex.Split(originalText, "\r?\n");
        for (var index = 0; index < Math.Max(current.Length, original.Length); index++)
        {
            var left = index < current.Length ? current[index] : null;
            var right = index < original.Length ? original[index] : null;
            if (!string.Equals(left, right, StringComparison.Ordinal)) _changedLines.Add(index);
        }
    }

    private void ApplyHighlighting()
    {
        _editor.SuspendLayout();
        var selectionStart = _editor.SelectionStart;
        var selectionLength = _editor.SelectionLength;
        _editor.SelectAll();
        _editor.SelectionColor = Theme.Text;
        _editor.SelectionBackColor = Theme.Canvas;
        for (var line = 0; line < _editor.Lines.Length; line++) ApplyLineHighlight(line);

        _editor.Select(Math.Min(selectionStart, _editor.TextLength), Math.Min(selectionLength, Math.Max(0, _editor.TextLength - selectionStart)));
        _editor.ResumeLayout();
    }

    private void SetLineBackground(int line, Color color)
    {
        if (line < 0 || line >= _editor.Lines.Length) return;
        var start = _editor.GetFirstCharIndexFromLine(line);
        if (start < 0) return;
        var length = _editor.Lines[line].Length + (line < _editor.Lines.Length - 1 ? 1 : 0);
        _editor.Select(start, Math.Min(length, _editor.TextLength - start));
        _editor.SelectionBackColor = color;
    }

    private void ApplyLineHighlight(int line)
    {
        if (line < 0 || line >= _editor.Lines.Length) return;
        var start = _editor.GetFirstCharIndexFromLine(line);
        if (start < 0) return;
        var text = _editor.Lines[line];
        _editor.Select(start, text.Length);
        _editor.SelectionColor = Theme.Text;
        _editor.SelectionBackColor = line == _selectedLine ? Theme.SelectedLine :
            _changedLines.Contains(line) ? Theme.ModifiedLine : Theme.Canvas;
        if (text.TrimStart().StartsWith('#'))
        {
            _editor.Select(start, text.Length);
            _editor.SelectionColor = Theme.Accent;
        }
        var comment = text.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0)
        {
            _editor.Select(start + comment, text.Length - comment);
            _editor.SelectionColor = Theme.TextMuted;
        }
    }

    private bool TryApplyLineChanges(string previousText, string currentText, HashSet<int> affected)
    {
        if (_editor.TextLength == 0 || previousText.Length == 0) return false;
        var previous = Regex.Split(previousText, "\r?\n");
        var current = Regex.Split(currentText, "\r?\n");
        if (previous.Length != current.Length) return false;
        var changed = Enumerable.Range(0, current.Length)
            .Where(index => !previous[index].Equals(current[index], StringComparison.Ordinal)).ToArray();
        foreach (var line in changed.OrderByDescending(index => index))
        {
            var start = _editor.GetFirstCharIndexFromLine(line);
            if (start < 0) return false;
            _editor.Select(start, previous[line].Length);
            _editor.SelectedText = current[line];
            affected.Add(line);
        }
        return true;
    }

    private void FindBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || string.IsNullOrWhiteSpace(_findBox.Text)) return;
        var direction = e.Shift ? RichTextBoxFinds.Reverse : RichTextBoxFinds.None;
        var start = e.Shift ? Math.Max(0, _editor.SelectionStart - 1) : Math.Min(_editor.TextLength, _editor.SelectionStart + _editor.SelectionLength);
        var found = e.Shift
            ? _editor.Find(_findBox.Text, 0, start, direction)
            : _editor.Find(_findBox.Text, start, _editor.TextLength, direction);
        if (found < 0)
            found = e.Shift ? _editor.Find(_findBox.Text, direction) : _editor.Find(_findBox.Text, RichTextBoxFinds.None);
        if (found >= 0) _editor.ScrollToCaret();
        e.SuppressKeyPress = true;
    }

    private void DrawGutter(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
        if (_editor.TextLength == 0) return;
        var firstChar = _editor.GetCharIndexFromPosition(new Point(1, 1));
        var firstLine = Math.Max(0, _editor.GetLineFromCharIndex(firstChar));
        var lastChar = _editor.GetCharIndexFromPosition(new Point(1, Math.Max(1, _editor.ClientSize.Height - 1)));
        var lastLine = Math.Min(_editor.Lines.Length - 1, _editor.GetLineFromCharIndex(lastChar) + 1);
        for (var line = firstLine; line <= lastLine; line++)
        {
            var charIndex = _editor.GetFirstCharIndexFromLine(line);
            if (charIndex < 0) continue;
            var point = _editor.GetPositionFromCharIndex(charIndex);
            var bounds = new Rectangle(0, point.Y, _gutter.Width - 5, _editor.Font.Height + 2);
            var marker = line == _selectedLine ? ">" : _changedLines.Contains(line) ? "M" : string.Empty;
            if (!string.IsNullOrEmpty(marker))
                TextRenderer.DrawText(e.Graphics, marker, Theme.MonoFont, new Rectangle(3, point.Y, 15, bounds.Height),
                    line == _selectedLine ? Theme.Accent : Theme.Warning, TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, (line + 1).ToString(), Theme.MonoFont, bounds, Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
