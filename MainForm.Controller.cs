using System.Globalization;
using System.Text;
using System.Text.Json;
using StarRailShaderEditor.Models;
using StarRailShaderEditor.Services;

namespace StarRailShaderEditor;

internal sealed partial class MainForm
{
    internal string RunUiSmokeTests()
    {
        SetVerificationState("controller", showAdvanced: false);
        if (_controllerModel is null || _currentDocumentKey != "controller")
            throw new InvalidOperationException("Controller view did not bind.");
        if (!_exportVmdButton.Enabled)
            throw new InvalidOperationException("VMD export is not enabled for the controller.");

        var morph = _controllerModel.Morphs.First();
        var original = _controllerPreset.MorphWeights.GetValueOrDefault(morph.Name);
        var changed = original < 0.5f ? 0.75f : 0.25f;
        if (!_wpfInspector.SetFirstControllerValueForTest(changed))
            throw new InvalidOperationException("Controller Morph editor was not created.");
        System.Windows.Forms.Application.DoEvents();
        if (Math.Abs(_controllerPreset.MorphWeights.GetValueOrDefault(morph.Name) - changed) > 0.0001f)
            throw new InvalidOperationException("Controller Morph edit did not reach the preset model.");

        Undo();
        if (Math.Abs(_controllerPreset.MorphWeights.GetValueOrDefault(morph.Name) - original) > 0.0001f)
            throw new InvalidOperationException("Controller undo did not restore the original weight.");
        Redo();
        if (Math.Abs(_controllerPreset.MorphWeights.GetValueOrDefault(morph.Name) - changed) > 0.0001f)
            throw new InvalidOperationException("Controller redo did not restore the edited weight.");

        _controllerPreset.MorphWeights[morph.Name] = original;
        _controllerDirty = false;
        _undo.Clear();
        _redo.Clear();

        SetVerificationState("body", showAdvanced: true);
        var define = _currentDocument?.Find("SUB_INDEX_SWIZZLE")
            ?? throw new InvalidOperationException("Material Define parameter was not found.");
        var originalDefineValue = define.Value;
        var replacement = define.Options.FirstOrDefault(option => option.Value != originalDefineValue)
            ?? throw new InvalidOperationException("Material Define parameter has no alternate option.");
        SelectGroup(define.Group, fromCanvas: false);
        RebuildParameterPanel(force: true);
        System.Windows.Forms.Application.DoEvents();
        if (!_wpfInspector.SetParameterOptionForTest(define, replacement.Value))
            throw new InvalidOperationException("Material Define ComboBox was not created.");
        System.Windows.Forms.Application.DoEvents();
        if (_currentDocument?.Find("SUB_INDEX_SWIZZLE")?.Value != replacement.Value)
            throw new InvalidOperationException("Material Define selection did not reach the FX document.");
        Undo();
        if (_currentDocument?.Find("SUB_INDEX_SWIZZLE")?.Value != originalDefineValue)
            throw new InvalidOperationException("Material Define undo did not restore the original option.");
        Redo();
        if (_currentDocument?.Find("SUB_INDEX_SWIZZLE")?.Value != replacement.Value)
            throw new InvalidOperationException("Material Define redo did not restore the selected option.");
        if (_currentDocument?.Find("SUB_INDEX_SWIZZLE") is { } restoredDefine)
            _currentDocument.SetValue(restoredDefine, originalDefineValue);
        _undo.Clear();
        _redo.Clear();

        SetVerificationState("Shadow", showAdvanced: true, showSource: true);
        if (_currentDocument?.Find("SHADOW_MAP_SIZE") is null || _currentDocument.Find("Script") is null)
            throw new InvalidOperationException("Shadow.fx did not bind SHADOW_MAP_SIZE and annotated Script.");
        SetVerificationState("Shadow_zbuffer", showAdvanced: true, showSource: true);
        if (_currentDocument?.Find("DepthBias") is null || _currentDocument.Find("Script") is not null)
            throw new InvalidOperationException("Shadow_zbuffer.fx must expose DepthBias without the technique Script string.");

        return "PASS: controller and material Define edit/undo/redo, VMD command state, Shadow FX UI bindings.";
    }

    private void LoadControllerPreset()
    {
        if (_controllerModel is null) return;
        var path = DefaultControllerPresetPath();
        try
        {
            _controllerPreset = File.Exists(path) ? VmdMorphWriter.LoadPreset(path) : NewControllerPreset();
        }
        catch
        {
            _controllerPreset = NewControllerPreset();
        }
        _controllerPreset.ModelName = _controllerModel.Name;
        foreach (var morph in _controllerModel.Morphs)
            _controllerPreset.MorphWeights.TryAdd(morph.Name, 0f);
        _controllerDirty = false;
    }

    private ControllerPreset NewControllerPreset() => new()
    {
        ModelName = _controllerModel?.Name ?? string.Empty,
        MorphWeights = _controllerModel?.Morphs.ToDictionary(morph => morph.Name, _ => 0f, StringComparer.Ordinal)
            ?? new Dictionary<string, float>(StringComparer.Ordinal),
    };

    private void BindController()
    {
        if (_controllerModel is null) return;
        _currentDocument = null;
        _currentDocumentKey = "controller";
        _currentGroup = "Morph 控制";
        _selectedSourceLine = -1;
        _sourceRevealPending = false;
        _rebuilding = true;
        _groupList.Items.Clear();
        _groupList.Items.Add("Morph 控制");
        _groupList.SelectedIndex = 0;
        _advancedToggle.Enabled = false;
        _rebuilding = false;
        RebuildParameterPanel(force: true);
        BuildControllerPipeline();
        RefreshControllerPreview();
        _diagnosticList.Items.Clear();
        var item = new ListViewItem("正常") { ForeColor = Theme.Success, Tag = DiagnosticSeverity.Info };
        item.SubItems.Add("fun_controller.pmx");
        item.SubItems.Add($"已读取 {_controllerModel.Morphs.Count} 个 Morph；导出 VMD 后加载到控制器模型。 ");
        _diagnosticList.Items.Add(item);
        UpdateStatus();
    }

    private void RebuildControllerPanel()
    {
        if (_controllerModel is null) return;
        _rebuilding = true;
        var query = _searchBox.Text.Trim();
        var morphs = _controllerModel.Morphs.Where(morph => string.IsNullOrWhiteSpace(query) ||
            morph.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            morph.EnglishName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _wpfInspector.ShowController("控制器 Morph", $"{morphs.Length} 个控制项 · 范围 0–1", morphs,
            name => _controllerPreset.MorphWeights.GetValueOrDefault(name));
        _rebuilding = false;
    }

    private void ApplyControllerWeight(string name, float value, bool continuous)
    {
        value = Math.Clamp(value, 0f, 1f);
        if (Math.Abs(_controllerPreset.MorphWeights.GetValueOrDefault(name) - value) < 0.00001f) return;
        if (continuous && !_editTransactionActive) BeginEditTransaction();
        if (_editTransactionActive)
        {
            if (!_editTransactionCaptured)
            {
                CaptureControllerHistory();
                _editTransactionCaptured = true;
            }
        }
        else CaptureControllerHistory();
        _controllerPreset.MorphWeights[name] = value;
        _controllerDirty = true;
        if (continuous)
        {
            _historyCommitTimer.Stop();
            _historyCommitTimer.Start();
        }
        RefreshControllerPreview();
        UpdateStatus();
    }

    private void CaptureControllerHistory() => PushUndo(new HistoryState("controller", ControllerSnapshot(), "Morph 控制", true));

    private string ControllerSnapshot() => JsonSerializer.Serialize(_controllerPreset);

    private void RestoreControllerSnapshot(string snapshot)
    {
        _controllerPreset = JsonSerializer.Deserialize<ControllerPreset>(snapshot) ?? NewControllerPreset();
        _controllerDirty = true;
    }

    private void SaveControllerPreset()
    {
        try
        {
            var path = DefaultControllerPresetPath();
            VmdMorphWriter.SavePreset(path, _controllerPreset);
            _controllerDirty = false;
            UpdateStatus($"控制器预设已保存到 {path}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法保存控制器预设：\n{exception.Message}", "保存失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveControllerPresetAs()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "另存控制器预设",
            Filter = "StarRail 控制器预设|*.json",
            FileName = "controller-preset.json",
            InitialDirectory = Path.GetDirectoryName(DefaultControllerPresetPath()),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        VmdMorphWriter.SavePreset(dialog.FileName, _controllerPreset);
        _controllerDirty = false;
        UpdateStatus($"控制器预设已保存到 {dialog.FileName}");
    }

    private void ExportControllerVmd()
    {
        if (_controllerModel is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "导出控制器 VMD",
            Filter = "Vocaloid Motion Data|*.vmd",
            FileName = "fun_controller_preset.vmd",
            InitialDirectory = _shaderDirectory,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            VmdMorphWriter.Write(dialog.FileName, _controllerModel, _controllerPreset.MorphWeights);
            UpdateStatus($"VMD 已导出：{dialog.FileName}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法导出 VMD：\n{exception.Message}", "导出失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BuildControllerPipeline()
    {
        var nodes = new[]
        {
            Node("controller", "fun_controller.pmx", "18 个运行时 Morph", "Morph 控制", Theme.NodeTexture, 80, 90),
            Node("preset", "控制器预设", "保存与复用权重", "Morph 控制", Theme.NodeSurface, 320, 90),
            Node("vmd", "帧 0 VMD", "加载到 MMD 控制器", "Morph 控制", Theme.Accent, 560, 90),
        };
        SetCurrentPipeline(nodes, [new("controller", "preset"), new("preset", "vmd")], fit: true);
    }

    private void RefreshControllerPreview()
    {
        if (_controllerModel is null) return;
        var builder = new StringBuilder();
        builder.AppendLine("# StarRail controller Morph preset");
        builder.AppendLine($"model = {_controllerModel.Name}");
        builder.AppendLine("frame = 0");
        builder.AppendLine();
        foreach (var morph in _controllerModel.Morphs)
            builder.AppendLine($"{morph.Name} = {_controllerPreset.MorphWeights.GetValueOrDefault(morph.Name).ToString("0.00", CultureInfo.InvariantCulture)}");
        _codePreview.SetPlainText(builder.ToString());
    }

    private string DefaultControllerPresetPath() =>
        Path.Combine(_shaderDirectory, ".starrail-editor", "controller-presets", "current.json");
}
