using System.Globalization;
using StarRailShaderEditor.Models;
using StarRailShaderEditor.Services;

namespace StarRailShaderEditor;

internal sealed partial class MainForm
{
    private void RebuildParameterPanel(bool force = false)
    {
        var panelKey = $"{_currentDocumentKey}|{_currentGroup}|{_searchBox.Text}|{_advancedToggle.Checked}";
        if (panelKey.Equals(_parameterPanelKey, StringComparison.Ordinal))
        {
            if (force) RefreshParameterEditors();
            return;
        }

        _parameterPanelKey = panelKey;
        if (_currentDocumentKey == "controller")
        {
            RebuildControllerPanel();
            return;
        }
        if (_currentDocument is null) return;

        _rebuilding = true;
        var query = _searchBox.Text.Trim();
        var parameters = _currentDocument.Parameters.AsEnumerable();
        string title;
        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters = parameters.Where(parameter =>
                parameter.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                parameter.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                parameter.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                parameter.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
            title = "搜索结果";
        }
        else
        {
            parameters = parameters.Where(parameter =>
                parameter.Group.Equals(_currentGroup, StringComparison.OrdinalIgnoreCase));
            title = _currentGroup;
        }

        if (!_advancedToggle.Checked)
            parameters = parameters.Where(parameter => !parameter.IsAdvanced);

        var items = parameters.ToList();
        var summary = items.Count == 0
            ? "当前筛选下没有参数"
            : $"{items.Count} 个参数{(_advancedToggle.Checked ? "，完整模式" : "，常用模式")}";
        var emptyMessage = _currentDocument.Kind == ShaderDocumentKind.Reference
            ? "这是只读核心源码；可在中央源码视图中查找和复制。"
            : string.IsNullOrWhiteSpace(_searchBox.Text)
                ? "切换到完整模式查看这一组的高级参数。"
                : "没有匹配的参数，请缩短搜索词。";
        _wpfInspector.ShowParameters(title, summary, items, emptyMessage);
        _rebuilding = false;
    }

    private void RefreshParameterEditors()
    {
        _rebuilding = true;
        _wpfInspector.RefreshValues();
        _rebuilding = false;
    }

    private void InvalidateParameterPanelCache(string documentKey)
    {
        if (_parameterPanelKey.StartsWith(documentKey + "|", StringComparison.OrdinalIgnoreCase))
            _parameterPanelKey = string.Empty;
    }

    private void ApplyParameterValue(ShaderParameter parameter, string value, bool continuous = false)
    {
        if (_rebuilding || _currentDocument is null || parameter.Value.Equals(value.Trim(), StringComparison.Ordinal)) return;
        if (continuous && !_editTransactionActive) BeginEditTransaction();
        if (_editTransactionActive)
        {
            if (!_editTransactionCaptured)
            {
                CaptureHistory();
                _editTransactionCaptured = true;
            }
        }
        else
        {
            CaptureHistory();
        }
        _currentDocument.SetValue(parameter, value);
        SelectSourceLine(parameter.ValueLine);
        if (continuous)
        {
            _historyCommitTimer.Stop();
            _historyCommitTimer.Start();
        }
        ScheduleRefresh();
    }

    private void ApplyTextureEnabled(ShaderParameter parameter, bool enabled)
    {
        if (_rebuilding || _currentDocument is null || parameter.IsEnabled == enabled) return;
        CaptureHistory();
        _currentDocument.SetTextureEnabled(parameter, enabled);
        SelectSourceLine(parameter.ValueLine);
        ScheduleRefresh();
    }

    private void BrowseTexture(ShaderParameter parameter)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择纹理",
            Filter = "纹理图像|*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.dds|所有文件|*.*",
            CheckFileExists = true,
            InitialDirectory = FindInitialTextureDirectory(parameter.Value),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var relative = Path.GetRelativePath(_shaderDirectory, dialog.FileName).Replace('\\', '/');
        var value = relative.StartsWith("..", StringComparison.Ordinal)
            ? dialog.FileName.Replace('\\', '/')
            : relative;
        ApplyParameterValue(parameter, value);
    }

    private void ChooseParameterColor(ShaderParameter parameter)
    {
        var values = parameter.NumericValues();
        using var dialog = new ColorDialog { Color = ValuesToColor(values), FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK || values.Length < 3) return;

        BeginEditTransaction();
        var components = values.ToArray();
        components[0] = dialog.Color.R / 255d;
        components[1] = dialog.Color.G / 255d;
        components[2] = dialog.Color.B / 255d;
        var raw = string.Join(", ", components.Select(FormatNumber));
        ApplyParameterValue(parameter, $"{parameter.TypeName}({raw})", continuous: true);
        EndEditTransaction();
        _wpfInspector.RefreshValues();
    }

    private void BeginEditTransaction(bool captureImmediately = false)
    {
        _historyCommitTimer.Stop();
        if (_editTransactionActive) return;
        _editTransactionActive = true;
        _editTransactionCaptured = false;
        if (captureImmediately)
        {
            CaptureHistory();
            _editTransactionCaptured = true;
        }
    }

    private void EndEditTransaction()
    {
        _historyCommitTimer.Stop();
        _editTransactionActive = false;
        _editTransactionCaptured = false;
    }

    private string FindInitialTextureDirectory(string current)
    {
        try
        {
            var resolved = FxDocument.ResolveResourcePath(current, _shaderDirectory);
            var directory = Path.GetDirectoryName(resolved);
            if (Directory.Exists(directory)) return directory;
        }
        catch
        {
        }

        var local = Path.GetFullPath(Path.Combine(_shaderDirectory, "..", "Texture2D"));
        return Directory.Exists(local) ? local : _shaderDirectory;
    }

    private void RebuildPipeline()
    {
        if (_currentDocument is null) return;
        if (_currentDocument.Kind == ShaderDocumentKind.Reference)
        {
            SetCurrentPipeline([], [], fit: false);
            return;
        }
        if (_currentDocument.Kind == ShaderDocumentKind.Shadow)
        {
            SetCurrentPipeline(
            [
                Node("scene", "场景深度", "RENDERDEPTHSTENCILTARGET", "阴影设置", Theme.NodeTexture, 90, 90),
                Node("shadow", "阴影贴图", "分辨率与清除深度", "阴影设置", Theme.NodeLighting, 340, 90),
                Node("output", "后处理输出", "Script 合成强度", "阴影设置", Theme.Accent, 590, 90),
            ],
            [new("scene", "shadow"), new("shadow", "output")], fit: true);
            return;
        }
        if (_currentDocument.Kind == ShaderDocumentKind.ShadowZBuffer)
        {
            SetCurrentPipeline(
            [
                Node("controller", "控制器方向", "ShadowRange / Up / Left", "阴影设置", Theme.NodeTexture, 90, 90),
                Node("bias", "深度偏移", "DepthBias", "阴影设置", Theme.NodeLighting, 340, 90),
                Node("output", "Z Buffer", "R32F 深度输出", "阴影设置", Theme.Accent, 590, 90),
            ],
            [new("controller", "bias"), new("bias", "output")], fit: true);
            return;
        }

        var face = _currentDocument.Find("MATERIAL_DOMAIN")?.Value == "1";
        var starEnabled = _currentDocument.Find("STARRYSKY")?.Value == "1";
        var shadowEnabled = _currentDocument.Find("SHADOW_MODE")?.Value == "1";
        var emissionEnabled = ReadScalar(_currentDocument, "EmissiveIntensity", 0) > 0.0001 ||
                              _currentDocument.Find("EMISSIVE_TEXTURE")?.IsEnabled == true;
        var nodes = new List<PipelineNode>
        {
            Node("textures", "纹理输入", "颜色、遮罩与 Ramp", "纹理资源", Theme.NodeTexture, 0, 78),
            Node("base", "基础颜色", "颜色、透明度与染色", "基础颜色", Theme.NodeSurface, 198, 0),
            Node("material", "子材质", "最多 8 组材质参数", "子材质", Theme.NodeTexture, 198, 88),
            Node("shadow", "卡通阴影", "冷暖 Ramp 与自阴影", "卡通阴影", Theme.NodeLighting, 396, 0, shadowEnabled, true),
            Node("specular", "高光", "阈值化 Blinn-Phong", "高光", Theme.NodeLighting, 396, 88),
            Node("rim", "边缘阴影", "轮廓明暗塑形", "边缘阴影", Theme.NodeLighting, 396, 176),
            Node("emission", "自发光", "颜色、遮罩与强度", "自发光", Theme.NodeEffect, 594, 0, emissionEnabled),
            Node("star", "星空效果", "星点、视差与 Fresnel", "星空效果", Theme.NodeEffect, 594, 88, starEnabled, true),
            Node("surface", "角色表面", "合并所有材质阶段", "基础颜色", Theme.NodeSurface, 792, 66),
            Node("output", "MME 输出", "Shader Model 3.0", "其他参数", Theme.Accent, 990, 66),
        };
        if (face)
            nodes.Insert(6, Node("face", "脸部细节", "脸部阴影、眼睛与鼻线", "脸部细节", Theme.NodeEffect, 396, 264));
        else
            nodes.Insert(6, Node("stockings", "丝袜材质", "厚度、明暗与纹理", "丝袜材质", Theme.NodeEffect, 396, 264));

        var links = new List<PipelineLink>
        {
            new("textures", "base"), new("textures", "material"), new("base", "shadow"), new("material", "specular"),
            new("shadow", "surface"), new("specular", "surface"), new("rim", "surface"), new("emission", "surface"),
            new("star", "surface"), new(face ? "face" : "stockings", "surface"), new("surface", "output"),
        };
        SetCurrentPipeline(nodes, links, fit: false);
    }

    private static PipelineNode Node(string id, string title, string subtitle, string group, Color color, float x, float y,
        bool enabled = true, bool canToggle = false) => new()
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Group = group,
            Color = color,
            Position = new PointF(x, y),
            Enabled = enabled,
            CanToggle = canToggle,
        };

    private void SetCurrentPipeline(IEnumerable<PipelineNode> nodes, IEnumerable<PipelineLink> links, bool fit)
    {
        _pipelineCanvas.SetPipeline(nodes, links);
        if (_session.NodePositions.TryGetValue(_currentDocumentKey, out var saved))
        {
            _pipelineCanvas.ApplyNodePositions(saved.ToDictionary(item => item.Key,
                item => new PointF(item.Value.X, item.Value.Y), StringComparer.Ordinal));
        }
        if (fit) _pipelineCanvas.FitToView();
    }

    private void SaveCurrentNodePositions()
    {
        _session.NodePositions[_currentDocumentKey] = _pipelineCanvas.GetNodePositions().ToDictionary(item => item.Key,
            item => new SessionPoint { X = item.Value.X, Y = item.Value.Y }, StringComparer.Ordinal);
    }

    private void TogglePipelineNode(PipelineNode node)
    {
        if (_currentDocument is null) return;
        var parameterName = node.Id switch { "shadow" => "SHADOW_MODE", "star" => "STARRYSKY", _ => string.Empty };
        var parameter = _currentDocument.Find(parameterName);
        if (parameter is null) return;
        ApplyParameterValue(parameter, node.Enabled ? "1" : "0");
        RebuildParameterPanel(force: true);
    }

    private void RefreshSourceView(bool force = false)
    {
        if (_currentDocument is null || (!force && _centerView != 1)) return;
        _codePreview.SetDocument(_currentDocument.GetText(), _currentDocument.OriginalText, _selectedSourceLine);
        if (_centerView == 1 && _sourceRevealPending && _selectedSourceLine >= 0)
        {
            _codePreview.RevealLine(_selectedSourceLine);
            _sourceRevealPending = false;
        }
    }

    private void RevealParameter(ShaderParameter parameter)
    {
        if (_currentDocument is null) return;
        SelectSourceLine(parameter.ValueLine);
        SetCenterView(1);
        _codePreview.SetDocument(_currentDocument.GetText(), _currentDocument.OriginalText, parameter.ValueLine);
        _codePreview.RevealLine(parameter.ValueLine);
        _sourceRevealPending = false;
    }

    private void SelectSourceLine(int line)
    {
        _selectedSourceLine = line;
        _sourceRevealPending = true;
    }

    private void NavigateSelectedDiagnostic()
    {
        if (_currentDocument is null || _diagnosticList.SelectedItems.Count == 0) return;
        var parameter = _currentDocument.Find(_diagnosticList.SelectedItems[0].SubItems.Count > 1
            ? _diagnosticList.SelectedItems[0].SubItems[1].Text
            : string.Empty);
        if (parameter is null) return;
        SelectGroup(parameter.Group, fromCanvas: false);
        RevealParameter(parameter);
    }

    private void RefreshDiagnostics(bool showPanel)
    {
        if (_currentDocument is null) return;
        _diagnosticList.BeginUpdate();
        _diagnosticList.Items.Clear();
        foreach (var diagnostic in _currentDocument.ValidateResources(_shaderDirectory))
        {
            var item = new ListViewItem(diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => "错误",
                DiagnosticSeverity.Warning => "提醒",
                _ => "信息",
            })
            {
                Tag = diagnostic.Severity,
                ForeColor = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => Theme.Error,
                    DiagnosticSeverity.Warning => Theme.Warning,
                    _ => Theme.TextMuted,
                },
            };
            item.SubItems.Add(diagnostic.Source);
            item.SubItems.Add(diagnostic.Message);
            item.ToolTipText = diagnostic.Recovery;
            _diagnosticList.Items.Add(item);
        }
        if (_diagnosticList.Items.Count == 0)
        {
            var ok = new ListViewItem("正常") { ForeColor = Theme.Success, Tag = DiagnosticSeverity.Info };
            ok.SubItems.Add(_currentDocument.Name);
            ok.SubItems.Add("当前启用的纹理和核心 Shader 均可访问。 ");
            _diagnosticList.Items.Add(ok);
        }
        _diagnosticList.EndUpdate();
        if (showPanel)
        {
            _bottomTabs.SelectedIndex = 0;
            ToggleDiagnostics(true);
        }
        UpdateStatus();
    }

    private static double ReadScalar(FxDocument document, string name, double fallback)
    {
        var values = document.Find(name)?.NumericValues();
        return values is { Length: > 0 } ? values[0] : fallback;
    }

    private static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static Color ValuesToColor(double[] values)
    {
        if (values.Length < 3) return Theme.SurfaceRaised;
        return Color.FromArgb(255,
            (int)Math.Clamp(Math.Round(values[0] * 255), 0, 255),
            (int)Math.Clamp(Math.Round(values[1] * 255), 0, 255),
            (int)Math.Clamp(Math.Round(values[2] * 255), 0, 255));
    }
}
