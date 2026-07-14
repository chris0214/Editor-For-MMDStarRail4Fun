using System.Diagnostics;
using StarRailShaderEditor.Services;

namespace StarRailShaderEditor;

internal sealed partial class MainForm
{
    private void CaptureHistory()
    {
        if (_currentDocument is null) return;
        PushUndo(new HistoryState(_currentDocumentKey, _currentDocument.GetText(), _currentGroup));
    }

    private void PushUndo(HistoryState state)
    {
        _undo.Push(state);
        while (_undo.Count > 120)
        {
            var retained = _undo.Take(120).Reverse().ToArray();
            _undo.Clear();
            foreach (var retainedState in retained) _undo.Push(retainedState);
        }
        _redo.Clear();
        UpdateStatus();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        var state = _undo.Pop();
        if (state.IsController)
        {
            _redo.Push(new HistoryState("controller", ControllerSnapshot(), "Morph 控制", true));
            RestoreControllerSnapshot(state.Text);
            BindController();
            UpdateStatus("已撤销控制器调整。");
            return;
        }
        var document = FindDocument(state.Key);
        if (document is null) return;
        _redo.Push(new HistoryState(state.Key, document.GetText(), _currentGroup));
        document.RestoreText(state.Text);
        InvalidateParameterPanelCache(state.Key);
        ActivateDocument(document, state.Group);
        ScheduleRefresh();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        var state = _redo.Pop();
        if (state.IsController)
        {
            _undo.Push(new HistoryState("controller", ControllerSnapshot(), "Morph 控制", true));
            RestoreControllerSnapshot(state.Text);
            BindController();
            UpdateStatus("已重做控制器调整。");
            return;
        }
        var document = FindDocument(state.Key);
        if (document is null) return;
        _undo.Push(new HistoryState(state.Key, document.GetText(), _currentGroup));
        document.RestoreText(state.Text);
        InvalidateParameterPanelCache(state.Key);
        ActivateDocument(document, state.Group);
        ScheduleRefresh();
    }

    private FxDocument? FindDocument(string key) => _documents.TryGetValue(key, out var document) ? document : null;

    private void ActivateDocument(FxDocument document, string group)
    {
        var entry = _documents.FirstOrDefault(item => ReferenceEquals(item.Value, document));
        var tab = _materialTabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Name == entry.Key);
        if (tab is not null)
        {
            _materialTabs.SelectedTab = tab;
            var pickerIndex = _documentChoices.FindIndex(choice => choice.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
            if (pickerIndex >= 0) _documentPicker.SelectedIndex = pickerIndex;
        }
        _currentDocument = document;
        _currentDocumentKey = entry.Key;
        _currentGroup = group;
        RebuildGroups();
        RebuildPipeline();
        RebuildParameterPanel();
        RefreshDiagnostics(showPanel: false);
        RefreshSourceView(force: true);
    }

    private void SaveCurrent()
    {
        if (_currentDocumentKey == "controller")
        {
            SaveControllerPreset();
            return;
        }
        if (_currentDocument is null || !_currentDocument.IsDirty) return;
        try
        {
            _currentDocument.Save(createBackup: true);
            RefreshSourceView(force: true);
            UpdateStatus("已保存，并在 .starrail-editor-backups 中创建备份。");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法保存材质：\n{exception.Message}\n\n当前修改仍保留在编辑器中。",
                "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveCurrentAs()
    {
        if (_currentDocumentKey == "controller")
        {
            SaveControllerPresetAs();
            return;
        }
        if (_currentDocument is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "另存材质",
            Filter = "MME Effect|*.fx|所有文件|*.*",
            FileName = Path.GetFileName(_currentDocument.FilePath),
            InitialDirectory = Path.GetDirectoryName(_currentDocument.FilePath),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _currentDocument.SaveAs(dialog.FileName);
            UpdateStatus($"副本已保存到 {dialog.FileName}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法保存副本：\n{exception.Message}", "另存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenFxFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开 StarRail 材质",
            Filter = "MME Effect|*.fx|所有文件|*.*",
            CheckFileExists = true,
            InitialDirectory = _shaderDirectory,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var document = FxDocument.Load(dialog.FileName);
            if (document.Parameters.Count == 0)
            {
                MessageBox.Show(this, "这个文件没有识别到 StarRail 可视参数。", "无法作为材质打开",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var key = "custom_" + Guid.NewGuid().ToString("N");
            _documents[key] = document;
            var page = new TabPage(Path.GetFileNameWithoutExtension(dialog.FileName))
            {
                Name = key,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
            };
            _materialTabs.TabPages.Add(page);
            var choice = new DocumentChoice(key, page.Text + "  ·  " + dialog.FileName);
            _documentChoices.Add(choice);
            _documentPicker.Items.Add(choice);
            _materialTabs.SelectedTab = page;
            _documentPicker.SelectedIndex = _materialTabs.SelectedIndex;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法读取 FX 文件：\n{exception.Message}", "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportPortablePackage()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择便携包的输出目录",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var result = PortableExporter.Export(_shaderDirectory, _documents.Values, dialog.SelectedPath);
            if (!result.Success)
            {
                var missing = string.Join("\n", result.Issues.Take(12).Select(issue => $"{issue.Document} / {issue.Parameter}: {issue.Path}"));
                if (result.Issues.Count > 12) missing += $"\n……另有 {result.Issues.Count - 12} 项";
                MessageBox.Show(this, $"便携包未生成。请先修复这些启用中的纹理：\n\n{missing}",
                    "资源不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshDiagnostics(showPanel: true);
                return;
            }
            UpdateStatus($"便携包已导出到 {result.OutputPath}");
            Process.Start(new ProcessStartInfo { FileName = result.OutputPath, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法导出便携包：\n{exception.Message}", "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ConfirmDiscardOrSave()
    {
        var dirty = _documents.Values.Where(document => document.IsDirty).Distinct().ToList();
        var dirtyCount = dirty.Count + (_controllerDirty ? 1 : 0);
        if (dirtyCount == 0) return true;
        var result = MessageBox.Show(this, $"有 {dirtyCount} 个文件或预设尚未保存。是否全部保存后退出？",
            "保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.No) return true;
        try
        {
            foreach (var document in dirty) document.Save(createBackup: true);
            if (_controllerDirty)
            {
                VmdMorphWriter.SavePreset(DefaultControllerPresetPath(), _controllerPreset);
                _controllerDirty = false;
            }
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"保存时发生错误：\n{exception.Message}", "无法退出", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void OpenGuide()
    {
        var guide = Path.Combine(AppContext.BaseDirectory, "GUIDE.md");
        if (!File.Exists(guide)) guide = Path.Combine(_shaderDirectory, "..", "StarRailShaderEditor", "GUIDE.md");
        if (File.Exists(guide))
            Process.Start(new ProcessStartInfo { FileName = guide, UseShellExecute = true });
        else
            MessageBox.Show(this, "没有找到 GUIDE.md。", "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S)) { SaveCurrent(); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
        if (keyData == (Keys.Control | Keys.F))
        {
            if (_centerView == 1) _codePreview.FocusFind();
            else { _searchBox.Focus(); _searchBox.SelectAll(); }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
