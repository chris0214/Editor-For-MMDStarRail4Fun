using System.Globalization;
using StarRailShaderEditor.Models;
using StarRailShaderEditor.Services;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;

namespace StarRailShaderEditor.Controls;

internal sealed class WpfInspectorControl : WpfControls.UserControl
{
    private static readonly WpfMedia.SolidColorBrush Surface = Brush(31, 32, 36);
    private static readonly WpfMedia.SolidColorBrush SurfaceRaised = Brush(39, 40, 45);
    private static readonly WpfMedia.SolidColorBrush SurfaceHover = Brush(48, 49, 55);
    private static readonly WpfMedia.SolidColorBrush Border = Brush(64, 66, 73);
    private static readonly WpfMedia.SolidColorBrush Text = Brush(235, 234, 230);
    private static readonly WpfMedia.SolidColorBrush TextMuted = Brush(171, 171, 167);
    private static readonly WpfMedia.SolidColorBrush Accent = Brush(218, 112, 92);
    private static readonly WpfMedia.SolidColorBrush Success = Brush(111, 176, 134);
    private static readonly WpfMedia.SolidColorBrush Warning = Brush(218, 169, 91);
    private static readonly WpfMedia.SolidColorBrush Error = Brush(218, 101, 110);

    private readonly string _shaderDirectory;
    private readonly WpfControls.TextBlock _title = new();
    private readonly WpfControls.TextBlock _summary = new();
    private readonly WpfControls.StackPanel _items = new();
    private readonly Dictionary<ShaderParameter, Action> _parameterRefreshers = new();
    private readonly Dictionary<ShaderParameter, WpfControls.ComboBox> _defineEditors = new();
    private readonly Dictionary<string, Action> _controllerRefreshers = new(StringComparer.Ordinal);
    private bool _syncing;

    public WpfInspectorControl(string shaderDirectory)
    {
        _shaderDirectory = shaderDirectory;
        Background = Surface;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Resources[Wpf.SystemColors.ControlBrushKey] = SurfaceRaised;
        Resources[Wpf.SystemColors.ControlTextBrushKey] = Text;
        Resources[Wpf.SystemColors.WindowBrushKey] = SurfaceRaised;
        Resources[Wpf.SystemColors.WindowTextBrushKey] = Text;
        Resources[Wpf.SystemColors.HighlightBrushKey] = Accent;
        Resources[Wpf.SystemColors.HighlightTextBrushKey] = Text;
        Resources[Wpf.SystemColors.InactiveSelectionHighlightBrushKey] = SurfaceHover;
        Resources[Wpf.SystemColors.GrayTextBrushKey] = TextMuted;
        Resources.MergedDictionaries.Add(new Wpf.ResourceDictionary
        {
            Source = new Uri("/StarRailShaderEditor;component/Controls/WpfInspectorStyles.xaml", UriKind.RelativeOrAbsolute),
        });

        var root = new WpfControls.Grid { Background = Surface };
        root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        _title.Margin = new Wpf.Thickness(14, 12, 12, 1);
        _title.FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI");
        _title.FontSize = 16;
        _title.FontWeight = Wpf.FontWeights.SemiBold;
        _title.Foreground = Text;
        _title.TextTrimming = Wpf.TextTrimming.CharacterEllipsis;

        _summary.Margin = new Wpf.Thickness(15, 2, 12, 10);
        _summary.FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI");
        _summary.FontSize = 12;
        _summary.Foreground = TextMuted;
        _summary.TextTrimming = Wpf.TextTrimming.CharacterEllipsis;

        var scroll = new WpfControls.ScrollViewer
        {
            VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = WpfControls.ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Background = Surface,
            Content = _items,
        };
        var scrollBarStyle = new Wpf.Style(typeof(WpfControls.Primitives.ScrollBar));
        scrollBarStyle.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, Surface));
        scrollBarStyle.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, SurfaceHover));
        scrollBarStyle.Setters.Add(new Wpf.Setter(Wpf.FrameworkElement.WidthProperty, 10d));
        scrollBarStyle.Setters.Add(new Wpf.Setter(Wpf.UIElement.OpacityProperty, 0.45d));
        scroll.Resources[typeof(WpfControls.Primitives.ScrollBar)] = scrollBarStyle;
        _items.Margin = new Wpf.Thickness(14, 0, 14, 18);

        WpfControls.Grid.SetRow(_title, 0);
        WpfControls.Grid.SetRow(_summary, 1);
        WpfControls.Grid.SetRow(scroll, 2);
        root.Children.Add(_title);
        root.Children.Add(_summary);
        root.Children.Add(scroll);
        Content = root;
    }

    public Action<ShaderParameter, string, bool>? ParameterValueChanged { get; set; }
    public Action<ShaderParameter, bool>? TextureEnabledChanged { get; set; }
    public Action<ShaderParameter>? TextureBrowseRequested { get; set; }
    public Action<ShaderParameter>? ColorRequested { get; set; }
    public Action<ShaderParameter>? RevealRequested { get; set; }
    public Action? BeginEditRequested { get; set; }
    public Action? EndEditRequested { get; set; }
    public Action<string, float, bool>? ControllerWeightChanged { get; set; }

    public int ItemCount { get; private set; }

    public void ShowParameters(string title, string summary, IReadOnlyList<ShaderParameter> parameters, string emptyMessage)
    {
        _syncing = true;
        _title.Text = title;
        _summary.Text = summary;
        _items.Children.Clear();
        _parameterRefreshers.Clear();
        _defineEditors.Clear();
        _controllerRefreshers.Clear();
        ItemCount = parameters.Count;
        if (parameters.Count == 0)
            _items.Children.Add(CreateEmptyState(emptyMessage));
        else
            foreach (var parameter in parameters) _items.Children.Add(CreateParameterRow(parameter));
        _syncing = false;
    }

    public void ShowController(string title, string summary, IReadOnlyList<ControllerMorph> morphs,
        Func<string, float> valueProvider)
    {
        _syncing = true;
        _title.Text = title;
        _summary.Text = summary;
        _items.Children.Clear();
        _parameterRefreshers.Clear();
        _defineEditors.Clear();
        _controllerRefreshers.Clear();
        ItemCount = morphs.Count;
        if (morphs.Count == 0)
            _items.Children.Add(CreateEmptyState("没有匹配的控制项，请缩短搜索词。"));
        else
            foreach (var morph in morphs) _items.Children.Add(CreateControllerRow(morph, valueProvider));
        _syncing = false;
    }

    public void RefreshValues()
    {
        _syncing = true;
        foreach (var refresh in _parameterRefreshers.Values) refresh();
        foreach (var refresh in _controllerRefreshers.Values) refresh();
        _syncing = false;
    }

    internal bool SetFirstControllerValueForTest(float value)
    {
        var name = _controllerRefreshers.Keys.FirstOrDefault();
        if (name is null) return false;
        BeginEditRequested?.Invoke();
        ControllerWeightChanged?.Invoke(name, value, true);
        EndEditRequested?.Invoke();
        RefreshValues();
        return true;
    }

    internal bool SetParameterOptionForTest(ShaderParameter parameter, string value)
    {
        if (!_defineEditors.TryGetValue(parameter, out var combo)) return false;
        var option = parameter.Options.FirstOrDefault(candidate => candidate.Value == value);
        if (option is null) return false;
        combo.SelectedItem = option;
        return true;
    }

    private Wpf.FrameworkElement CreateParameterRow(ShaderParameter parameter)
    {
        var body = new WpfControls.StackPanel();
        var header = new WpfControls.Grid();
        header.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        header.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });

        var names = new WpfControls.StackPanel();
        names.Children.Add(new WpfControls.TextBlock
        {
            Text = parameter.DisplayName,
            FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = Wpf.FontWeights.SemiBold,
            Foreground = Text,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis,
        });
        names.Children.Add(new WpfControls.TextBlock
        {
            Text = parameter.Name,
            Margin = new Wpf.Thickness(0, 2, 0, 0),
            FontFamily = new WpfMedia.FontFamily("Cascadia Mono"),
            FontSize = 10,
            Foreground = TextMuted,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis,
        });
        WpfControls.Grid.SetColumn(names, 0);
        header.Children.Add(names);

        var reveal = MakeButton("\uE71C", "在源码中定位");
        reveal.FontFamily = new WpfMedia.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        reveal.Width = 32;
        reveal.Height = 30;
        reveal.Margin = new Wpf.Thickness(8, 0, 0, 0);
        reveal.Click += (_, _) => RevealRequested?.Invoke(parameter);
        WpfControls.Grid.SetColumn(reveal, 1);
        header.Children.Add(reveal);
        body.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(parameter.Description) && parameter.Kind != ShaderParameterKind.Texture)
        {
            body.Children.Add(new WpfControls.TextBlock
            {
                Text = parameter.Description,
                Margin = new Wpf.Thickness(0, 6, 0, 0),
                FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
                FontSize = 11,
                Foreground = TextMuted,
                TextWrapping = Wpf.TextWrapping.Wrap,
            });
        }

        var editor = parameter.Kind switch
        {
            ShaderParameterKind.Texture => CreateTextureEditor(parameter),
            ShaderParameterKind.Define => CreateDefineEditor(parameter),
            ShaderParameterKind.Boolean => CreateBooleanEditor(parameter),
            ShaderParameterKind.Vector2 or ShaderParameterKind.Vector3 or ShaderParameterKind.Vector4 => CreateVectorEditor(parameter),
            _ => CreateScalarEditor(parameter),
        };
        editor.Margin = new Wpf.Thickness(0, 10, 0, 0);
        body.Children.Add(editor);

        return new WpfControls.Border
        {
            Background = Surface,
            BorderBrush = Border,
            BorderThickness = new Wpf.Thickness(0, 0, 0, 1),
            Padding = new Wpf.Thickness(0, 10, 0, 12),
            Child = body,
        };
    }

    private Wpf.FrameworkElement CreateTextureEditor(ShaderParameter parameter)
    {
        var root = new WpfControls.StackPanel();
        var line = new WpfControls.Grid();
        line.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        line.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        line.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });

        var enabled = new WpfControls.CheckBox
        {
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Foreground = Text,
            MinWidth = 58,
            Margin = new Wpf.Thickness(0, 0, 8, 0),
        };
        var path = MakeTextBox();
        var browse = MakeButton("...", "选择纹理");
        browse.Width = 38;
        browse.Margin = new Wpf.Thickness(6, 0, 0, 0);
        var status = new WpfControls.TextBlock
        {
            Margin = new Wpf.Thickness(66, 6, 0, 0),
            FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Foreground = TextMuted,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis,
        };

        WpfControls.Grid.SetColumn(enabled, 0);
        WpfControls.Grid.SetColumn(path, 1);
        WpfControls.Grid.SetColumn(browse, 2);
        line.Children.Add(enabled);
        line.Children.Add(path);
        line.Children.Add(browse);
        root.Children.Add(line);
        root.Children.Add(status);

        void Refresh()
        {
            enabled.IsChecked = parameter.IsEnabled;
            enabled.Content = parameter.IsEnabled ? "使用" : "关闭";
            enabled.Foreground = parameter.IsEnabled ? Success : TextMuted;
            path.Text = parameter.Value;
            path.IsEnabled = browse.IsEnabled = parameter.IsEnabled;
            var textureStatus = GetTextureStatus(parameter);
            status.Text = textureStatus.Text;
            status.Foreground = textureStatus.Brush;
            status.ToolTip = textureStatus.ToolTip;
        }

        enabled.Click += (_, _) =>
        {
            if (_syncing) return;
            TextureEnabledChanged?.Invoke(parameter, enabled.IsChecked == true);
            Refresh();
        };
        path.LostKeyboardFocus += (_, _) => CommitText(parameter, path.Text);
        path.KeyDown += (_, e) =>
        {
            if (e.Key != WpfInput.Key.Enter) return;
            CommitText(parameter, path.Text);
            e.Handled = true;
        };
        browse.Click += (_, _) =>
        {
            TextureBrowseRequested?.Invoke(parameter);
            Refresh();
        };
        _parameterRefreshers[parameter] = Refresh;
        Refresh();
        return root;
    }

    private Wpf.FrameworkElement CreateDefineEditor(ShaderParameter parameter)
    {
        if (parameter.Options.Count == 0)
        {
            var field = MakeTextBox();
            field.Text = parameter.Value;
            field.LostKeyboardFocus += (_, _) => CommitText(parameter, field.Text);
            field.KeyDown += (_, e) =>
            {
                if (e.Key != WpfInput.Key.Enter) return;
                CommitText(parameter, field.Text);
                e.Handled = true;
            };
            _parameterRefreshers[parameter] = () => field.Text = parameter.Value;
            return field;
        }

        var combo = new WpfControls.ComboBox
        {
            Style = (Wpf.Style)FindResource("DarkComboBox"),
            Background = SurfaceRaised,
            Foreground = Text,
            BorderBrush = Border,
            BorderThickness = new Wpf.Thickness(1),
            ItemsSource = parameter.Options,
            DisplayMemberPath = nameof(ParameterOption.Label),
        };
        void Refresh() => combo.SelectedItem = parameter.Options.FirstOrDefault(option => option.Value == parameter.Value)
            ?? parameter.Options[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedItem is not ParameterOption option) return;
            ParameterValueChanged?.Invoke(parameter, option.Value, false);
        };
        _defineEditors[parameter] = combo;
        _parameterRefreshers[parameter] = Refresh;
        Refresh();
        return combo;
    }

    private Wpf.FrameworkElement CreateBooleanEditor(ShaderParameter parameter)
    {
        var toggle = new WpfControls.CheckBox
        {
            Content = "启用",
            Foreground = Text,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            MinHeight = 30,
        };
        void Refresh() => toggle.IsChecked = parameter.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        toggle.Click += (_, _) =>
        {
            if (_syncing) return;
            ParameterValueChanged?.Invoke(parameter, toggle.IsChecked == true ? "true" : "false", false);
        };
        _parameterRefreshers[parameter] = Refresh;
        Refresh();
        return toggle;
    }

    private Wpf.FrameworkElement CreateScalarEditor(ShaderParameter parameter)
    {
        var component = parameter.ComponentAt(0);
        var current = parameter.NumericValues().FirstOrDefault();
        var slider = new WpfControls.Slider
        {
            Minimum = Math.Min(component.SoftMinimum, current),
            Maximum = Math.Max(component.SoftMaximum, current),
            TickFrequency = Math.Max(component.Step, parameter.Kind == ShaderParameterKind.Integer ? 1 : 0.001),
            IsSnapToTickEnabled = parameter.Kind == ShaderParameterKind.Integer,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, 0, 10, 0),
        };
        var numeric = MakeTextBox();
        numeric.Width = 88;
        numeric.HorizontalContentAlignment = Wpf.HorizontalAlignment.Right;
        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        WpfControls.Grid.SetColumn(slider, 0);
        WpfControls.Grid.SetColumn(numeric, 1);
        grid.Children.Add(slider);
        grid.Children.Add(numeric);

        void Refresh()
        {
            var value = parameter.NumericValues().FirstOrDefault();
            slider.Minimum = Math.Min(component.SoftMinimum, value);
            slider.Maximum = Math.Max(component.SoftMaximum, value);
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
            numeric.Text = FormatNumber(value);
        }
        slider.PreviewMouseLeftButtonDown += (_, _) => BeginEditRequested?.Invoke();
        slider.PreviewMouseLeftButtonUp += (_, _) => EndEditRequested?.Invoke();
        slider.LostMouseCapture += (_, _) => EndEditRequested?.Invoke();
        slider.GotKeyboardFocus += (_, _) => BeginEditRequested?.Invoke();
        slider.LostKeyboardFocus += (_, _) => EndEditRequested?.Invoke();
        slider.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            numeric.Text = FormatNumber(slider.Value);
            ParameterValueChanged?.Invoke(parameter, numeric.Text, true);
        };
        numeric.GotKeyboardFocus += (_, _) => BeginEditRequested?.Invoke();
        numeric.LostKeyboardFocus += (_, _) =>
        {
            CommitScalar(parameter, numeric, component);
            EndEditRequested?.Invoke();
        };
        numeric.KeyDown += (_, e) =>
        {
            if (e.Key != WpfInput.Key.Enter) return;
            CommitScalar(parameter, numeric, component);
            e.Handled = true;
        };
        _parameterRefreshers[parameter] = Refresh;
        Refresh();
        return grid;
    }

    private Wpf.FrameworkElement CreateVectorEditor(ShaderParameter parameter)
    {
        var count = parameter.Kind switch
        {
            ShaderParameterKind.Vector2 => 2,
            ShaderParameterKind.Vector3 => 3,
            _ => 4,
        };
        var grid = new WpfControls.Grid();
        var fields = new List<WpfControls.TextBox>();
        for (var index = 0; index < count; index++)
        {
            grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            var component = parameter.ComponentAt(index);
            var stack = new WpfControls.StackPanel { Margin = new Wpf.Thickness(index == 0 ? 0 : 5, 0, 0, 0) };
            stack.Children.Add(new WpfControls.TextBlock
            {
                Text = component.Label,
                Margin = new Wpf.Thickness(2, 0, 0, 3),
                Foreground = TextMuted,
                FontSize = 10,
            });
            var field = MakeTextBox();
            field.HorizontalContentAlignment = Wpf.HorizontalAlignment.Right;
            fields.Add(field);
            stack.Children.Add(field);
            WpfControls.Grid.SetColumn(stack, index);
            grid.Children.Add(stack);
        }
        if (parameter.IsColor)
        {
            grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
            var color = MakeButton(string.Empty, "选择颜色");
            color.Width = 38;
            color.Height = 30;
            color.Margin = new Wpf.Thickness(7, 16, 0, 0);
            color.Click += (_, _) => ColorRequested?.Invoke(parameter);
            WpfControls.Grid.SetColumn(color, count);
            grid.Children.Add(color);
            _parameterRefreshers[parameter] = () =>
            {
                RefreshVectorFields(parameter, fields);
                color.Background = ColorBrush(parameter.NumericValues());
            };
        }
        else
        {
            _parameterRefreshers[parameter] = () => RefreshVectorFields(parameter, fields);
        }

        void CommitVector()
        {
            if (_syncing) return;
            var values = new double[count];
            for (var index = 0; index < count; index++)
            {
                var fallback = parameter.NumericValues().ElementAtOrDefault(index);
                if (!double.TryParse(fields[index].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    parsed = fallback;
                var component = parameter.ComponentAt(index);
                values[index] = Math.Clamp(parsed,
                    Math.Min(component.HardMinimum, fallback), Math.Max(component.HardMaximum, fallback));
                fields[index].Text = FormatNumber(values[index]);
            }
            var raw = string.Join(", ", values.Select(FormatNumber));
            ParameterValueChanged?.Invoke(parameter, $"{parameter.TypeName}({raw})", true);
        }

        foreach (var field in fields)
        {
            field.GotKeyboardFocus += (_, _) => BeginEditRequested?.Invoke();
            field.LostKeyboardFocus += (_, _) =>
            {
                CommitVector();
                EndEditRequested?.Invoke();
            };
            field.KeyDown += (_, e) =>
            {
                if (e.Key != WpfInput.Key.Enter) return;
                CommitVector();
                e.Handled = true;
            };
        }
        _parameterRefreshers[parameter]();
        return grid;
    }

    private Wpf.FrameworkElement CreateControllerRow(ControllerMorph morph, Func<string, float> valueProvider)
    {
        var root = new WpfControls.StackPanel();
        root.Children.Add(new WpfControls.TextBlock
        {
            Text = morph.Name,
            FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = Wpf.FontWeights.SemiBold,
            Foreground = Text,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(morph.EnglishName))
        {
            root.Children.Add(new WpfControls.TextBlock
            {
                Text = morph.EnglishName,
                Margin = new Wpf.Thickness(0, 2, 0, 0),
                FontFamily = new WpfMedia.FontFamily("Cascadia Mono"),
                FontSize = 10,
                Foreground = TextMuted,
                TextTrimming = Wpf.TextTrimming.CharacterEllipsis,
            });
        }

        var editor = new WpfControls.Grid { Margin = new Wpf.Thickness(0, 10, 0, 0) };
        editor.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        var slider = new WpfControls.Slider
        {
            Minimum = 0,
            Maximum = 1,
            TickFrequency = 0.01,
            Margin = new Wpf.Thickness(0, 0, 10, 0),
            VerticalAlignment = Wpf.VerticalAlignment.Center,
        };
        var numeric = MakeTextBox();
        numeric.Width = 82;
        numeric.HorizontalContentAlignment = Wpf.HorizontalAlignment.Right;
        WpfControls.Grid.SetColumn(slider, 0);
        WpfControls.Grid.SetColumn(numeric, 1);
        editor.Children.Add(slider);
        editor.Children.Add(numeric);
        root.Children.Add(editor);

        void Refresh()
        {
            var value = Math.Clamp(valueProvider(morph.Name), 0f, 1f);
            slider.Value = value;
            numeric.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
        }
        slider.PreviewMouseLeftButtonDown += (_, _) => BeginEditRequested?.Invoke();
        slider.PreviewMouseLeftButtonUp += (_, _) => EndEditRequested?.Invoke();
        slider.LostMouseCapture += (_, _) => EndEditRequested?.Invoke();
        slider.GotKeyboardFocus += (_, _) => BeginEditRequested?.Invoke();
        slider.LostKeyboardFocus += (_, _) => EndEditRequested?.Invoke();
        slider.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            numeric.Text = slider.Value.ToString("0.00", CultureInfo.InvariantCulture);
            ControllerWeightChanged?.Invoke(morph.Name, (float)slider.Value, true);
        };
        numeric.GotKeyboardFocus += (_, _) => BeginEditRequested?.Invoke();
        numeric.LostKeyboardFocus += (_, _) =>
        {
            CommitController(morph.Name, numeric);
            EndEditRequested?.Invoke();
        };
        numeric.KeyDown += (_, e) =>
        {
            if (e.Key != WpfInput.Key.Enter) return;
            CommitController(morph.Name, numeric);
            e.Handled = true;
        };
        _controllerRefreshers[morph.Name] = Refresh;
        Refresh();
        return new WpfControls.Border
        {
            BorderBrush = Border,
            BorderThickness = new Wpf.Thickness(0, 0, 0, 1),
            Padding = new Wpf.Thickness(0, 10, 0, 12),
            Child = root,
        };
    }

    private void CommitText(ShaderParameter parameter, string value)
    {
        if (_syncing) return;
        ParameterValueChanged?.Invoke(parameter, value, false);
    }

    private void CommitScalar(ShaderParameter parameter, WpfControls.TextBox field,
        ParameterComponentDefinition component)
    {
        var fallback = parameter.NumericValues().FirstOrDefault();
        if (!double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) value = fallback;
        value = Math.Clamp(value, Math.Min(component.HardMinimum, fallback), Math.Max(component.HardMaximum, fallback));
        if (parameter.Kind == ShaderParameterKind.Integer) value = Math.Round(value);
        field.Text = FormatNumber(value);
        if (!_syncing) ParameterValueChanged?.Invoke(parameter, field.Text, true);
    }

    private void CommitController(string name, WpfControls.TextBox field)
    {
        if (!float.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) value = 0;
        value = Math.Clamp(value, 0f, 1f);
        field.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
        if (!_syncing) ControllerWeightChanged?.Invoke(name, value, true);
    }

    private static void RefreshVectorFields(ShaderParameter parameter, IReadOnlyList<WpfControls.TextBox> fields)
    {
        var values = parameter.NumericValues();
        for (var index = 0; index < fields.Count; index++)
            fields[index].Text = FormatNumber(values.ElementAtOrDefault(index));
    }

    private (string Text, WpfMedia.Brush Brush, string ToolTip) GetTextureStatus(ShaderParameter parameter)
    {
        if (!parameter.IsEnabled) return ("未参与当前材质", TextMuted, string.Empty);
        var resolved = FxDocument.ResolveResourcePath(parameter.Value, _shaderDirectory);
        var exists = File.Exists(resolved);
        if (!exists) return ("找不到这个纹理", Error, resolved);
        return Path.IsPathRooted(parameter.Value)
            ? ("资源存在，建议导出为相对路径", Warning, resolved)
            : ("资源正常", Success, resolved);
    }

    private static WpfControls.TextBox MakeTextBox() => new()
    {
        Height = 30,
        Background = SurfaceRaised,
        Foreground = Text,
        CaretBrush = Text,
        BorderBrush = Border,
        BorderThickness = new Wpf.Thickness(1),
        Padding = new Wpf.Thickness(8, 4, 8, 3),
        FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
        FontSize = 12,
        VerticalContentAlignment = Wpf.VerticalAlignment.Center,
    };

    private static WpfControls.Button MakeButton(string content, string toolTip) => new()
    {
        Content = content,
        ToolTip = toolTip,
        Height = 30,
        MinWidth = 30,
        Background = SurfaceRaised,
        Foreground = Text,
        BorderBrush = Border,
        BorderThickness = new Wpf.Thickness(1),
        Padding = new Wpf.Thickness(7, 2, 7, 2),
        Cursor = WpfInput.Cursors.Hand,
    };

    private static WpfControls.TextBlock CreateEmptyState(string message) => new()
    {
        Text = message,
        Margin = new Wpf.Thickness(0, 18, 0, 0),
        Foreground = TextMuted,
        FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
        FontSize = 12,
        TextWrapping = Wpf.TextWrapping.Wrap,
    };

    private static WpfMedia.Brush ColorBrush(IReadOnlyList<double> values)
    {
        if (values.Count < 3) return SurfaceRaised;
        return new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(
            (byte)Math.Clamp(Math.Round(values[0] * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(values[1] * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(values[2] * 255), 0, 255)));
    }

    private static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static WpfMedia.SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
