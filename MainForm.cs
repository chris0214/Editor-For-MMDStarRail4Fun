using System.Text.Json;
using System.Runtime.InteropServices;
using StarRailShaderEditor.Controls;
using StarRailShaderEditor.Models;
using StarRailShaderEditor.Services;

namespace StarRailShaderEditor;

internal sealed partial class MainForm : Form
{
    private readonly string _shaderDirectory;
    private readonly Dictionary<string, FxDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<HistoryState> _undo = new();
    private readonly Stack<HistoryState> _redo = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _historyCommitTimer = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 180 };

    private readonly TabControl _materialTabs = new();
    private readonly ComboBox _documentPicker = new();
    private readonly List<DocumentChoice> _documentChoices = [];
    private readonly TextBox _searchBox = new();
    private readonly ListBox _groupList = new();
    private readonly CheckBox _advancedToggle = new();
    private readonly PipelineCanvas _pipelineCanvas = new();
    private readonly WpfInspectorControl _wpfInspector;
    private readonly System.Windows.Forms.Integration.ElementHost _wpfInspectorHost = new();
    private readonly TabControl _bottomTabs = new();
    private readonly Panel _centerViewHost = new();
    private readonly Button _nodeViewButton = new();
    private readonly Button _sourceViewButton = new();
    private readonly ListView _diagnosticList = new();
    private readonly CodePreviewControl _codePreview = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _dirtyLabel = new();
    private readonly ToolStripStatusLabel _resourceLabel = new();
    private readonly ToolStripButton _saveButton = new();
    private readonly ToolStripButton _undoButton = new();
    private readonly ToolStripButton _redoButton = new();
    private readonly ToolStripButton _fitButton = new();
    private readonly ToolStripButton _exportVmdButton = new();
    private readonly ToolStripButton _diagnosticsButton = new();
    private readonly SplitContainer _outerSplit = new();
    private readonly SplitContainer _workspaceSplit = new();
    private readonly SplitContainer _canvasSplit = new();

    private FxDocument? _currentDocument;
    private ControllerModel? _controllerModel;
    private ControllerPreset _controllerPreset = new();
    private bool _controllerDirty;
    private int _centerView;
    private int _selectedSourceLine = -1;
    private bool _sourceRevealPending;
    private bool _diagnosticsVisible;
    private bool _windowSizingOrMoving;
    private bool _resumeRefreshAfterMove;
    private bool _resumeSearchAfterMove;
    private bool _resumeHistoryAfterMove;
    private string _parameterPanelKey = string.Empty;
    private string _currentDocumentKey = "sr_body";
    private string _currentGroup = "基础颜色";
    private bool _rebuilding;
    private bool _editTransactionActive;
    private bool _editTransactionCaptured;
    private EditorSession _session = new();

    public MainForm(string shaderDirectory)
    {
        _shaderDirectory = Path.GetFullPath(shaderDirectory);
        _wpfInspector = new WpfInspectorControl(_shaderDirectory);
        Text = "StarRail 材质节点编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 700);
        Size = new Size(1480, 920);
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        KeyPreview = true;

        BuildLayout();
        WireEvents();
        LoadSession();
        LoadDocuments();
        ApplySession();
        Shown += (_, _) => BeginInvoke(ApplyInitialSplitLayout);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmDiscardOrSave())
        {
            e.Cancel = true;
            return;
        }
        SaveSession();
        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!OperatingSystem.IsWindows()) return;
        var enabled = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
    }

    protected override void WndProc(ref Message message)
    {
        const int enterSizeMove = 0x0231;
        const int exitSizeMove = 0x0232;
        if (message.Msg == enterSizeMove) BeginWindowSizeMove();

        base.WndProc(ref message);

        if (message.Msg == exitSizeMove) EndWindowSizeMove();
    }

    private void BeginWindowSizeMove()
    {
        if (_windowSizingOrMoving) return;
        _windowSizingOrMoving = true;
        _resumeRefreshAfterMove = _refreshTimer.Enabled;
        _resumeSearchAfterMove = _searchTimer.Enabled;
        _resumeHistoryAfterMove = _historyCommitTimer.Enabled;
        _refreshTimer.Stop();
        _searchTimer.Stop();
        _historyCommitTimer.Stop();
        _pipelineCanvas.SuspendResizePainting = true;
        SuspendLayout();
        _outerSplit.SuspendLayout();
        _workspaceSplit.SuspendLayout();
        _canvasSplit.SuspendLayout();
    }

    private void EndWindowSizeMove()
    {
        if (!_windowSizingOrMoving) return;
        _canvasSplit.ResumeLayout(false);
        _workspaceSplit.ResumeLayout(false);
        _outerSplit.ResumeLayout(false);
        ResumeLayout(true);
        _pipelineCanvas.SuspendResizePainting = false;
        _windowSizingOrMoving = false;
        if (_resumeRefreshAfterMove) _refreshTimer.Start();
        if (_resumeSearchAfterMove) _searchTimer.Start();
        if (_resumeHistoryAfterMove) _historyCommitTimer.Start();
    }

    internal void SetVerificationState(string material, bool showAdvanced, bool showSource = false)
    {
        _advancedToggle.Checked = showAdvanced;
        if (material.Equals("controller", StringComparison.OrdinalIgnoreCase))
        {
            var controllerIndex = _documentChoices.FindIndex(choice => choice.Key == "controller");
            if (controllerIndex >= 0) _documentPicker.SelectedIndex = controllerIndex;
            SetCenterView(showSource ? 1 : 0);
            return;
        }
        var normalized = material.Replace('\\', '/');
        var documentIndex = _documentChoices.FindIndex(choice =>
            choice.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(choice.Key).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (documentIndex >= 0)
        {
            _documentPicker.SelectedIndex = documentIndex;
            SetCenterView(showSource ? 1 : 0);
            return;
        }
        _materialTabs.SelectedIndex = material.ToLowerInvariant() switch
        {
            "face" or "sr_face" => 1,
            "hair" or "sr_hair" => 2,
            _ => 0,
        };
        SelectMaterialTab();
        SetCenterView(showSource ? 1 : 0);
    }

    internal void SetVerificationChange(string name, string value)
    {
        if (_currentDocument?.Find(name) is not { } parameter) return;
        _currentDocument.SetValue(parameter, value);
        SelectSourceLine(parameter.ValueLine);
        RebuildParameterPanel(force: true);
        RefreshSourceView(force: true);
        UpdateStatus();
    }

    internal void RunWindowMoveCycleForTest()
    {
        BeginWindowSizeMove();
        EndWindowSizeMove();
    }

    internal string RunBenchmarks()
    {
        SetVerificationState("body", showAdvanced: true);
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var initialHandles = process.HandleCount;
        var initialUserObjects = OperatingSystem.IsWindows() ? GetGuiResources(process.Handle, 1) : 0;
        var initialGdiObjects = OperatingSystem.IsWindows() ? GetGuiResources(process.Handle, 0) : 0;
        var panel = Measure(() => RebuildParameterPanel(force: true), 40);
        var node = MeasureNodeSwitch(20);
        SetCenterView(1);
        var parameter = _currentDocument?.Find("AlphaMultiplier")
            ?? throw new InvalidOperationException("Benchmark parameter AlphaMultiplier was not found.");
        var originalValue = parameter.Value;
        var iteration = 0;
        var source = Measure(() =>
        {
            _currentDocument!.SetValue(parameter, iteration++ % 2 == 0 ? "0.731" : "0.732");
            RefreshSourceView(force: true);
        }, 40);
        iteration = 0;
        var feedback = Measure(() =>
        {
            _currentDocument!.SetValue(parameter, iteration++ % 2 == 0 ? "0.741" : "0.742");
            RefreshSourceView(force: true);
            RebuildPipeline();
        }, 40);
        _currentDocument.SetValue(parameter, originalValue);
        RefreshSourceView(force: true);
        RebuildPipeline();
        SetCenterView(0);
        var canvas = Measure(() => { _pipelineCanvas.Invalidate(); _pipelineCanvas.Update(); }, 40);
        var resize = MeasureInteractiveResize(40);
        var handles = process.HandleCount;
        var userObjects = OperatingSystem.IsWindows() ? GetGuiResources(process.Handle, 1) : 0;
        var gdiObjects = OperatingSystem.IsWindows() ? GetGuiResources(process.Handle, 0) : 0;
        return $"BENCH: node-switch mean={node.Mean:0.00}ms p95={node.P95:0.00}ms; " +
               $"panel-refresh mean={panel.Mean:0.00}ms p95={panel.P95:0.00}ms ({_wpfInspector.ItemCount} items); " +
               $"source mean={source.Mean:0.00}ms p95={source.P95:0.00}ms; " +
               $"feedback mean={feedback.Mean:0.00}ms p95={feedback.P95:0.00}ms; " +
               $"canvas mean={canvas.Mean:0.00}ms p95={canvas.P95:0.00}ms; " +
               $"resize mean={resize.Mean:0.00}ms p95={resize.P95:0.00}ms; " +
               $"handles={handles}({handles - initialHandles:+#;-#;0}) " +
               $"user={userObjects}({userObjects - initialUserObjects:+#;-#;0}) " +
               $"gdi={gdiObjects}({gdiObjects - initialGdiObjects:+#;-#;0})";
    }

    private (double Mean, double P95) MeasureInteractiveResize(int iterations)
    {
        var original = Size;
        BeginWindowSizeMove();
        try
        {
            var offset = 0;
            return Measure(() =>
            {
                offset = offset == 0 ? 1 : 0;
                Size = new Size(original.Width + offset, original.Height + offset);
            }, iterations);
        }
        finally
        {
            Size = original;
            EndWindowSizeMove();
        }
    }

    private (double Mean, double P95) MeasureNodeSwitch(int iterations)
    {
        if (_currentDocument is null) return (0, 0);
        var originalGroup = _currentGroup;
        var groups = _currentDocument.Parameters.Select(parameter => parameter.Group)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length < 2) return (0, 0);

        var samples = new double[iterations];
        for (var index = 0; index < iterations; index++)
        {
            var next = groups[(index + 1) % groups.Length];
            if (next.Equals(_currentGroup, StringComparison.OrdinalIgnoreCase)) next = groups[(index + 2) % groups.Length];
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _currentGroup = next;
            RebuildParameterPanel();
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        _currentGroup = originalGroup;
        RebuildParameterPanel();
        Array.Sort(samples);
        return (samples.Average(), samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * 0.95) - 1)]);
    }

    private static (double Mean, double P95) Measure(Action action, int iterations)
    {
        var samples = new double[iterations];
        for (var index = 0; index < iterations; index++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        return (samples.Average(), samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * 0.95) - 1)]);
    }

    private void BuildLayout()
    {
        Controls.Add(BuildMainArea());
        Controls.Add(BuildStatusStrip());
        Controls.Add(BuildToolStrip());
        Controls.Add(BuildDocumentBar());
    }

    private Control BuildDocumentBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Surface, Padding = new Padding(12, 7, 12, 7) };
        var label = new Label
        {
            Text = "当前文件",
            Dock = DockStyle.Left,
            Width = 72,
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _documentPicker.Dock = DockStyle.Left;
        _documentPicker.Width = 310;
        _documentPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        _documentPicker.FlatStyle = FlatStyle.Flat;
        _documentPicker.BackColor = Theme.SurfaceRaised;
        _documentPicker.ForeColor = Theme.Text;
        _documentPicker.DisplayMember = nameof(DocumentChoice.Label);
        _documentPicker.AccessibleName = "当前 Shader 文件";
        var scope = new Label
        {
            Text = "固定 StarRail 渲染管线",
            Dock = DockStyle.Right,
            Width = 190,
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.MiddleRight,
        };
        bar.Controls.Add(scope);
        bar.Controls.Add(_documentPicker);
        bar.Controls.Add(label);
        return bar;
    }

    private Control BuildToolStrip()
    {
        var strip = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 44,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Padding = new Padding(10, 5, 10, 5),
            Renderer = new DarkToolStripRenderer(),
        };

        strip.Items.Add(MakeButton("打开", "打开其他 StarRail FX 文件", (_, _) => OpenFxFile()));
        _saveButton.Text = "保存";
        _saveButton.ToolTipText = "保存当前材质并创建备份 (Ctrl+S)";
        _saveButton.Click += (_, _) => SaveCurrent();
        strip.Items.Add(_saveButton);
        strip.Items.Add(MakeButton("另存", "保存为新的 FX 文件", (_, _) => SaveCurrentAs()));
        strip.Items.Add(MakeButton("导出包", "导出使用相对纹理路径的便携目录", (_, _) => ExportPortablePackage()));
        _exportVmdButton.Text = "导出 VMD";
        _exportVmdButton.ToolTipText = "把控制器 Morph 预设导出为帧 0 VMD";
        _exportVmdButton.Click += (_, _) => ExportControllerVmd();
        strip.Items.Add(_exportVmdButton);
        strip.Items.Add(new ToolStripSeparator());
        _undoButton.Text = "撤销";
        _undoButton.Click += (_, _) => Undo();
        _redoButton.Text = "重做";
        _redoButton.Click += (_, _) => Redo();
        strip.Items.Add(_undoButton);
        strip.Items.Add(_redoButton);
        strip.Items.Add(new ToolStripSeparator());
        _fitButton.Text = "适合画布";
        _fitButton.Click += (_, _) => _pipelineCanvas.FitToView();
        strip.Items.Add(_fitButton);
        strip.Items.Add(MakeButton("检查资源", "检查纹理和核心 Shader 路径", (_, _) => RefreshDiagnostics(showPanel: true)));
        _diagnosticsButton.Text = "诊断";
        _diagnosticsButton.ToolTipText = "展开或收起诊断面板";
        _diagnosticsButton.Click += (_, _) => ToggleDiagnostics();
        strip.Items.Add(_diagnosticsButton);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(MakeButton("使用说明", "打开随附的零基础使用文档", (_, _) => OpenGuide()));
        return strip;
    }

    private Control BuildMainArea()
    {
        _outerSplit.Dock = DockStyle.Fill;
        _outerSplit.FixedPanel = FixedPanel.Panel1;
        _outerSplit.SplitterWidth = 5;
        _outerSplit.BackColor = Theme.Border;
        _outerSplit.Panel1.Controls.Add(BuildNavigationPanel());

        _workspaceSplit.Dock = DockStyle.Fill;
        _workspaceSplit.FixedPanel = FixedPanel.Panel2;
        _workspaceSplit.SplitterWidth = 5;
        _workspaceSplit.BackColor = Theme.Border;
        _workspaceSplit.Panel1.Controls.Add(BuildCanvasAndDiagnostics());
        _workspaceSplit.Panel2.Controls.Add(BuildInspector());
        _outerSplit.Panel2.Controls.Add(_workspaceSplit);
        return _outerSplit;
    }

    private Control BuildNavigationPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        var header = new Label
        {
            Text = "参数分组",
            Font = Theme.HeadingFont,
            ForeColor = Theme.Text,
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(14, 13, 8, 0),
        };
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.PlaceholderText = "搜索参数";
        _searchBox.BackColor = Theme.SurfaceRaised;
        _searchBox.ForeColor = Theme.Text;
        _searchBox.BorderStyle = BorderStyle.FixedSingle;

        var searchHost = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(12, 3, 12, 7) };
        var searchLabel = new Label
        {
            Text = "搜索参数或源码名",
            Dock = DockStyle.Top,
            Height = 23,
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        searchHost.Controls.Add(_searchBox);
        searchHost.Controls.Add(searchLabel);

        _advancedToggle.Text = "显示全部参数";
        _advancedToggle.Dock = DockStyle.Bottom;
        _advancedToggle.Height = 44;
        _advancedToggle.Padding = new Padding(13, 0, 8, 0);
        _advancedToggle.ForeColor = Theme.TextMuted;
        _advancedToggle.FlatStyle = FlatStyle.Flat;

        _groupList.Dock = DockStyle.Fill;
        _groupList.BackColor = Theme.Surface;
        _groupList.ForeColor = Theme.Text;
        _groupList.BorderStyle = BorderStyle.None;
        _groupList.ItemHeight = 34;
        _groupList.IntegralHeight = false;
        _groupList.DrawMode = DrawMode.OwnerDrawFixed;

        panel.Controls.Add(_groupList);
        panel.Controls.Add(_advancedToggle);
        panel.Controls.Add(searchHost);
        panel.Controls.Add(header);
        return panel;
    }

    private Control BuildCanvasAndDiagnostics()
    {
        _canvasSplit.Dock = DockStyle.Fill;
        _canvasSplit.Orientation = Orientation.Horizontal;
        _canvasSplit.SplitterWidth = 5;
        _canvasSplit.Panel2MinSize = 125;
        _canvasSplit.BackColor = Theme.Border;
        _canvasSplit.Panel1.Controls.Add(BuildCenterTabs());
        _canvasSplit.Panel2.Controls.Add(BuildBottomTabs());
        _canvasSplit.Panel2Collapsed = true;
        return _canvasSplit;
    }

    private Control BuildCenterTabs()
    {
        var container = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        var header = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Theme.Surface, Padding = new Padding(6, 4, 6, 4) };
        ConfigureViewButton(_nodeViewButton, "节点", 0);
        ConfigureViewButton(_sourceViewButton, "源码", 1);
        header.Controls.Add(_sourceViewButton);
        header.Controls.Add(_nodeViewButton);

        _centerViewHost.Dock = DockStyle.Fill;
        _centerViewHost.BackColor = Theme.Canvas;
        _centerViewHost.Controls.Add(_codePreview);
        _centerViewHost.Controls.Add(_pipelineCanvas);
        container.Controls.Add(_centerViewHost);
        container.Controls.Add(header);
        SetCenterView(0);
        return container;
    }

    private void ConfigureViewButton(Button button, string text, int view)
    {
        button.Text = text;
        button.Dock = DockStyle.Left;
        button.Width = 76;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.ForeColor = Theme.TextMuted;
        button.BackColor = Theme.Surface;
        button.Click += (_, _) => SetCenterView(view);
    }

    private void SetCenterView(int view)
    {
        _centerView = Math.Clamp(view, 0, 1);
        _pipelineCanvas.Visible = _centerView == 0;
        _codePreview.Visible = _centerView == 1;
        _nodeViewButton.BackColor = _centerView == 0 ? Theme.SurfaceRaised : Theme.Surface;
        _sourceViewButton.BackColor = _centerView == 1 ? Theme.SurfaceRaised : Theme.Surface;
        _nodeViewButton.ForeColor = _centerView == 0 ? Theme.Text : Theme.TextMuted;
        _sourceViewButton.ForeColor = _centerView == 1 ? Theme.Text : Theme.TextMuted;
        _session.CenterView = _centerView;
        if (_centerView == 1) RefreshSourceView(force: true);
    }

    private Control BuildBottomTabs()
    {
        _bottomTabs.Dock = DockStyle.Fill;
        _bottomTabs.Appearance = TabAppearance.FlatButtons;
        _bottomTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _bottomTabs.Padding = new Point(12, 4);
        var diagnosticsPage = new TabPage("诊断") { BackColor = Theme.Surface };

        _diagnosticList.Dock = DockStyle.Fill;
        _diagnosticList.View = View.Details;
        _diagnosticList.FullRowSelect = true;
        _diagnosticList.BorderStyle = BorderStyle.None;
        _diagnosticList.BackColor = Theme.Surface;
        _diagnosticList.ForeColor = Theme.Text;
        _diagnosticList.Columns.Add("级别", 72);
        _diagnosticList.Columns.Add("来源", 170);
        _diagnosticList.Columns.Add("说明", 520);
        diagnosticsPage.Controls.Add(_diagnosticList);

        _bottomTabs.TabPages.Add(diagnosticsPage);
        return _bottomTabs;
    }

    private Control BuildInspector()
    {
        _wpfInspector.ParameterValueChanged = (parameter, value, continuous) =>
        {
            ApplyParameterValue(parameter, value, continuous);
            if (parameter.Kind == ShaderParameterKind.Define) RebuildPipeline();
        };
        _wpfInspector.TextureEnabledChanged = ApplyTextureEnabled;
        _wpfInspector.TextureBrowseRequested = BrowseTexture;
        _wpfInspector.ColorRequested = ChooseParameterColor;
        _wpfInspector.RevealRequested = RevealParameter;
        _wpfInspector.BeginEditRequested = () => BeginEditTransaction();
        _wpfInspector.EndEditRequested = EndEditTransaction;
        _wpfInspector.ControllerWeightChanged = ApplyControllerWeight;
        _wpfInspectorHost.Dock = DockStyle.Fill;
        _wpfInspectorHost.BackColor = Theme.Surface;
        _wpfInspectorHost.Child = _wpfInspector;
        return _wpfInspectorHost;
    }

    private Control BuildStatusStrip()
    {
        var status = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextMuted,
            SizingGrip = false,
            Renderer = new DarkToolStripRenderer(),
        };
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        status.Items.Add(_statusLabel);
        status.Items.Add(_resourceLabel);
        status.Items.Add(_dirtyLabel);
        return status;
    }

    private static ToolStripButton MakeButton(string text, string tooltip, EventHandler click)
    {
        var button = new ToolStripButton(text) { ToolTipText = tooltip, DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += click;
        return button;
    }

    private void WireEvents()
    {
        _materialTabs.SelectedIndexChanged += (_, _) => SelectMaterialTab();
        _documentPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_documentPicker.SelectedItem is not DocumentChoice choice) return;
            if (choice.Key == "controller")
            {
                BindController();
                return;
            }
            var page = _materialTabs.TabPages.Cast<TabPage>().FirstOrDefault(tab => tab.Name == choice.Key);
            if (page is not null) _materialTabs.SelectedTab = page;
            if (!_currentDocumentKey.Equals(choice.Key, StringComparison.OrdinalIgnoreCase) &&
                _documents.TryGetValue(choice.Key, out var document))
                BindDocument(document, choice.Key);
        };
        _groupList.SelectedIndexChanged += (_, _) => SelectGroupFromList();
        _groupList.DrawItem += DrawGroupItem;
        _searchBox.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _advancedToggle.CheckedChanged += (_, _) => RebuildParameterPanel();
        _pipelineCanvas.GroupSelected += (_, group) => SelectGroup(group, fromCanvas: true);
        _pipelineCanvas.NodeToggleRequested += (_, node) => TogglePipelineNode(node);
        _pipelineCanvas.NodePositionsChanged += (_, _) => SaveCurrentNodePositions();
        _bottomTabs.DrawItem += DrawTabItem;
        _diagnosticList.DoubleClick += (_, _) => NavigateSelectedDiagnostic();
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshSourceView();
            RefreshDiagnostics(showPanel: false);
        };
        _historyCommitTimer.Tick += (_, _) => EndEditTransaction();
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RebuildParameterPanel(force: true);
        };
    }

    private void LoadDocuments()
    {
        _documents.Clear();
        _materialTabs.TabPages.Clear();
        _documentChoices.Clear();
        _documentPicker.Items.Clear();
        var paths = new List<string>();
        foreach (var stem in new[] { "sr_body.fx", "sr_face.fx", "sr_hair.fx", "Shadow.fx", "Shadow_zbuffer.fx" })
        {
            var path = Path.Combine(_shaderDirectory, stem);
            if (File.Exists(path)) paths.Add(path);
        }
        var lohen = Path.Combine(_shaderDirectory, "Lohen");
        if (Directory.Exists(lohen)) paths.AddRange(Directory.GetFiles(lohen, "*.fx").OrderBy(Path.GetFileName));
        var reference = Path.Combine(_shaderDirectory, "internal", "shader.hlsl");
        if (File.Exists(reference)) paths.Add(reference);

        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(_shaderDirectory, path).Replace('\\', '/');
            var key = relative.Equals("sr_body.fx", StringComparison.OrdinalIgnoreCase) ? "sr_body" : relative;
            var document = FxDocument.Load(path);
            _documents[key] = document;
            var label = relative switch
            {
                "sr_body.fx" => "身体",
                "sr_face.fx" => "脸部",
                "sr_hair.fx" => "头发",
                "Shadow.fx" => "阴影输出",
                "Shadow_zbuffer.fx" => "阴影深度",
                "internal/shader.hlsl" => "核心源码",
                _ => "Lohen · " + Path.GetFileNameWithoutExtension(path).Trim(),
            };
            _materialTabs.TabPages.Add(new TabPage(label) { Name = key, BackColor = Theme.Surface, ForeColor = Theme.Text });
            var choice = new DocumentChoice(key, label + "  ·  " + relative);
            _documentChoices.Add(choice);
            _documentPicker.Items.Add(choice);
        }
        var controllerPath = Path.Combine(_shaderDirectory, "fun_controller.pmx");
        if (File.Exists(controllerPath))
        {
            try
            {
                _controllerModel = PmxControllerReader.Read(controllerPath);
                LoadControllerPreset();
                var choice = new DocumentChoice("controller", $"控制器 Morph  ·  {_controllerModel.Morphs.Count} 项");
                _documentChoices.Add(choice);
                _documentPicker.Items.Add(choice);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, $"无法读取 fun_controller.pmx：\n{exception.Message}", "控制器不可用",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        if (_documents.Count == 0)
        {
            MessageBox.Show(this, $"在以下目录没有找到 sr_body.fx、sr_face.fx 或 sr_hair.fx：\n{_shaderDirectory}",
                "没有找到材质", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (_documents.TryGetValue("sr_body", out var body))
            BindDocument(body, "sr_body");
        else
        {
            var first = _documents.First();
            BindDocument(first.Value, first.Key);
        }
    }

    private void SelectMaterialTab()
    {
        if (_materialTabs.SelectedTab is null)
            return;
        var key = _materialTabs.SelectedTab.Name;
        if (!_documents.TryGetValue(key, out var document))
        {
            key = _materialTabs.SelectedIndex switch { 0 => "sr_body", 1 => "sr_face", 2 => "sr_hair", _ => key };
            if (!_documents.TryGetValue(key, out document)) return;
        }
        BindDocument(document, key);
        var pickerIndex = _documentChoices.FindIndex(choice => choice.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (pickerIndex >= 0 && _documentPicker.SelectedIndex != pickerIndex)
            _documentPicker.SelectedIndex = pickerIndex;
    }

    private void BindDocument(FxDocument document, string key)
    {
        _currentDocument = document;
        _currentDocumentKey = key;
        _selectedSourceLine = -1;
        _sourceRevealPending = false;
        _advancedToggle.Enabled = document.Kind != ShaderDocumentKind.Reference;
        _currentGroup = _session.Groups.TryGetValue(key, out var savedGroup) ? savedGroup : "基础颜色";
        RebuildGroups();
        RebuildPipeline();
        RebuildParameterPanel(force: true);
        RefreshDiagnostics(showPanel: false);
        RefreshSourceView(force: true);
        if (document.Kind == ShaderDocumentKind.Reference) SetCenterView(1);
        UpdateStatus();
    }

    private void RebuildGroups()
    {
        if (_currentDocument is null) return;
        _rebuilding = true;
        var groups = _currentDocument.Parameters.Select(parameter => parameter.Group).Distinct().ToList();
        if (groups.Count == 0 && _currentDocument.Kind == ShaderDocumentKind.Reference) groups.Add("只读源码");
        _groupList.Items.Clear();
        foreach (var group in groups) _groupList.Items.Add(group);
        var index = groups.FindIndex(group => group.Equals(_currentGroup, StringComparison.OrdinalIgnoreCase));
        _groupList.SelectedIndex = index >= 0 ? index : 0;
        if (_groupList.SelectedItem is string selected) _currentGroup = selected;
        _rebuilding = false;
    }

    private void SelectGroupFromList()
    {
        if (_rebuilding || _groupList.SelectedItem is not string group) return;
        SelectGroup(group, fromCanvas: false);
    }

    private void SelectGroup(string group, bool fromCanvas)
    {
        var changed = !_currentGroup.Equals(group, StringComparison.OrdinalIgnoreCase);
        _currentGroup = group;
        _session.Groups[_currentDocumentKey] = group;
        if (fromCanvas)
        {
            var index = _groupList.Items.IndexOf(group);
            if (index >= 0 && _groupList.SelectedIndex != index) _groupList.SelectedIndex = index;
        }
        else
        {
            _pipelineCanvas.SelectGroup(group);
        }
        if (changed) RebuildParameterPanel();
    }

    private void DrawGroupItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Theme.AccentMuted : Theme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
        {
            using var marker = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(marker, e.Bounds.X, e.Bounds.Y + 5, 3, e.Bounds.Height - 10);
        }
        var text = _groupList.Items[e.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(e.Graphics, text, selected ? Theme.UiFontMedium : Theme.UiFont,
            new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height),
            selected ? Theme.Text : Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void UpdateStatus(string? message = null)
    {
        if (_currentDocument is null)
        {
            if (_currentDocumentKey != "controller") return;
            _statusLabel.Text = message ?? "fun_controller.pmx · Morph 预设";
            _resourceLabel.Text = _controllerModel is null ? "控制器不可用" : $"{_controllerModel.Morphs.Count} 个 Morph";
            _resourceLabel.ForeColor = _controllerModel is null ? Theme.Error : Theme.Success;
            _dirtyLabel.Text = _controllerDirty ? "预设未保存" : "预设已保存";
            _dirtyLabel.ForeColor = _controllerDirty ? Theme.Warning : Theme.TextMuted;
            _saveButton.Enabled = _controllerDirty;
            _exportVmdButton.Enabled = _controllerModel is not null;
            _undoButton.Enabled = _undo.Count > 0;
            _redoButton.Enabled = _redo.Count > 0;
            Text = $"{(_controllerDirty ? "*" : string.Empty)}控制器 Morph - StarRail 材质节点编辑器";
            return;
        }
        _statusLabel.Text = message ?? _currentDocument.FilePath;
        var errors = _diagnosticList.Items.Cast<ListViewItem>().Count(item => item.Tag is DiagnosticSeverity.Error);
        var warnings = _diagnosticList.Items.Cast<ListViewItem>().Count(item => item.Tag is DiagnosticSeverity.Warning);
        _resourceLabel.Text = errors > 0 ? $"{errors} 个错误" : warnings > 0 ? $"{warnings} 个提醒" : "资源正常";
        _resourceLabel.ForeColor = errors > 0 ? Theme.Error : warnings > 0 ? Theme.Warning : Theme.Success;
        _dirtyLabel.Text = _currentDocument.IsDirty ? "未保存" : "已保存";
        _dirtyLabel.ForeColor = _currentDocument.IsDirty ? Theme.Warning : Theme.TextMuted;
        _saveButton.Enabled = _currentDocument.IsDirty;
        _exportVmdButton.Enabled = _controllerModel is not null;
        _undoButton.Enabled = _undo.Count > 0;
        _redoButton.Enabled = _redo.Count > 0;
        Text = $"{(_currentDocument.IsDirty ? "*" : string.Empty)}{_currentDocument.Name} - StarRail 材质节点编辑器";
    }

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
        UpdateStatus();
    }

    private void LoadSession()
    {
        var path = SessionPath();
        try
        {
            if (File.Exists(path)) _session = JsonSerializer.Deserialize<EditorSession>(File.ReadAllText(path)) ?? new EditorSession();
        }
        catch
        {
            _session = new EditorSession();
        }
    }

    private void ApplySession()
    {
        _advancedToggle.Checked = _session.ShowAdvanced;
        if (_session.WindowWidth >= MinimumSize.Width && _session.WindowHeight >= MinimumSize.Height)
            Size = new Size(_session.WindowWidth, _session.WindowHeight);
        var index = _documentChoices.FindIndex(choice => choice.Key.Equals(_session.DocumentKey, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = Math.Clamp(_session.MaterialTab, 0, Math.Max(0, _materialTabs.TabCount - 1));
        if (_materialTabs.TabCount > 0 && index < _materialTabs.TabCount) _materialTabs.SelectedIndex = index;
        if (_documentPicker.Items.Count > index) _documentPicker.SelectedIndex = index;
        SetCenterView(_session.CenterView);
    }

    private void SaveSession()
    {
        _session.ShowAdvanced = _advancedToggle.Checked;
        _session.MaterialTab = _materialTabs.SelectedIndex;
        _session.DocumentKey = _currentDocumentKey;
        _session.CenterView = _centerView;
        if (WindowState == FormWindowState.Normal)
        {
            _session.WindowWidth = Width;
            _session.WindowHeight = Height;
        }
        var path = SessionPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(_session, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string SessionPath() => Path.Combine(_shaderDirectory, ".starrail-editor", "session.json");

    private void ApplyInitialSplitLayout()
    {
        _outerSplit.SplitterDistance = Math.Clamp(220, _outerSplit.Panel1MinSize, Math.Max(_outerSplit.Panel1MinSize, _outerSplit.Width - 760));
        _workspaceSplit.SplitterDistance = Math.Max(540, _workspaceSplit.Width - 360);
        if (!_canvasSplit.Panel2Collapsed) _canvasSplit.SplitterDistance = Math.Max(360, _canvasSplit.Height - 190);
        _pipelineCanvas.FitToView();
        RebuildParameterPanel(force: true);
    }

    private void DrawTabItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0) return;
        var selected = tabs.SelectedIndex == e.Index;
        using var background = new SolidBrush(selected ? Theme.SurfaceRaised : Theme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
        {
            using var marker = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(marker, e.Bounds.X + 4, e.Bounds.Bottom - 3, e.Bounds.Width - 8, 3);
        }
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
            selected ? Theme.UiFontMedium : Theme.UiFont, e.Bounds,
            selected ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private sealed record HistoryState(string Key, string Text, string Group, bool IsController = false);
    private sealed record DocumentChoice(string Key, string Label);

    private void ToggleDiagnostics(bool? visible = null)
    {
        _diagnosticsVisible = visible ?? !_diagnosticsVisible;
        _canvasSplit.Panel2Collapsed = !_diagnosticsVisible;
        _diagnosticsButton.Checked = _diagnosticsVisible;
        if (_diagnosticsVisible)
            BeginInvoke(() => _canvasSplit.SplitterDistance = Math.Max(320, _canvasSplit.Height - 190));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);

}

internal sealed class EditorSession
{
    public int Version { get; set; } = 2;
    public bool ShowAdvanced { get; set; }
    public int MaterialTab { get; set; }
    public string DocumentKey { get; set; } = "sr_body";
    public int CenterView { get; set; }
    public int WindowWidth { get; set; } = 1480;
    public int WindowHeight { get; set; } = 920;
    public Dictionary<string, string> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, SessionPoint>> NodePositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SessionPoint
{
    public float X { get; set; }
    public float Y { get; set; }
}

internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
}

internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => Theme.Surface;
    public override Color ToolStripGradientMiddle => Theme.Surface;
    public override Color ToolStripGradientEnd => Theme.Surface;
    public override Color MenuItemSelected => Theme.SurfaceHover;
    public override Color ButtonSelectedHighlight => Theme.SurfaceHover;
    public override Color ButtonSelectedGradientBegin => Theme.SurfaceHover;
    public override Color ButtonSelectedGradientEnd => Theme.SurfaceHover;
    public override Color ButtonPressedGradientBegin => Theme.AccentMuted;
    public override Color ButtonPressedGradientEnd => Theme.AccentMuted;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
}
