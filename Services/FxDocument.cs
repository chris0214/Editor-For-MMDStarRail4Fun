using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StarRailShaderEditor.Models;

namespace StarRailShaderEditor.Services;

internal sealed class FxDocument : IEditableDocument
{
    private static readonly Regex SectionRegex = new(@"^\s*//\s*=+\s*(.+?)\s+begin\s*=+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TextureDefineRegex = new(@"^(?<indent>\s*)(?<comment>//\s*)?#define\s+(?<name>[A-Z0-9_]+_TEXTURE)\s*\\\s*$", RegexOptions.Compiled);
    private static readonly Regex DefineRegex = new(@"^(?<indent>\s*)#define\s+(?<name>[A-Z0-9_]+)\s+(?<value>[^\s/]+)(?:\s*//\s*(?<comment>.*))?$", RegexOptions.Compiled);
    private static readonly Regex ConstRegex = new(@"^(?<indent>\s*)const\s+(?<static>static\s+)?(?<type>float[234]?|int|bool)\s+(?<name>\w+)\s*=\s*(?<value>.+?)\s*;(?<tail>.*)$", RegexOptions.Compiled);
    private static readonly Regex AnnotatedStartRegex = new(@"^\s*(?<type>float[234]?|int|bool)\s+(?<name>\w+)\s*:\s*\w+\s*<", RegexOptions.Compiled);
    private static readonly Regex AnnotatedValueRegex = new(@"^(?<prefix>\s*>\s*=\s*)(?<value>.+?)(?<suffix>\s*;\s*)$", RegexOptions.Compiled);

    private readonly List<string> _lines;
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;

    private FxDocument(string filePath, List<string> lines, string newLine, Encoding encoding, byte[] preamble)
    {
        FilePath = filePath;
        Kind = DetermineKind(filePath);
        _lines = lines;
        NewLine = newLine;
        _encoding = encoding;
        _preamble = preamble;
        Parameters = ParseParameters();
    }

    public string FilePath { get; private set; }
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public ShaderDocumentKind Kind { get; }
    public string NewLine { get; }
    public IReadOnlyList<ShaderParameter> Parameters { get; private set; }
    public bool IsDirty { get; private set; }
    public string OriginalText { get; private set; } = string.Empty;

    public static FxDocument Load(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var detected = DetectEncoding(bytes);
        var text = detected.Encoding.GetString(bytes.AsSpan(detected.Preamble.Length));
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = Regex.Split(text, "\r?\n").ToList();
        var document = new FxDocument(Path.GetFullPath(filePath), lines, newLine, detected.Encoding, detected.Preamble)
        {
            OriginalText = text,
        };
        return document;
    }

    public string GetText() => string.Join(NewLine, _lines);

    public ShaderParameter? Find(string name) =>
        Parameters.FirstOrDefault(parameter => parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ShaderParameter> ForGroup(string group) =>
        Parameters.Where(parameter => parameter.Group.Equals(group, StringComparison.OrdinalIgnoreCase));

    public void SetValue(ShaderParameter parameter, string value)
    {
        value = value.Trim();
        if (parameter.Value.Equals(value, StringComparison.Ordinal))
        {
            return;
        }

        switch (parameter.Source)
        {
            case ShaderParameterSource.Texture:
                _lines[parameter.ValueLine] = ReplaceTexturePath(_lines[parameter.ValueLine], value, parameter.IsEnabled);
                break;
            case ShaderParameterSource.Define:
                _lines[parameter.DeclarationLine] = ReplaceDefineValue(_lines[parameter.DeclarationLine], value);
                break;
            case ShaderParameterSource.Annotated:
                _lines[parameter.DeclarationLine] = ReplaceAnnotatedValue(_lines[parameter.DeclarationLine], value);
                break;
            default:
                _lines[parameter.DeclarationLine] = ReplaceConstValue(_lines[parameter.DeclarationLine], value);
                break;
        }

        parameter.Value = value;
        IsDirty = true;
    }

    public void SetTextureEnabled(ShaderParameter parameter, bool enabled)
    {
        if (parameter.Kind != ShaderParameterKind.Texture || parameter.IsEnabled == enabled)
        {
            return;
        }

        _lines[parameter.DeclarationLine] = ToggleComment(_lines[parameter.DeclarationLine], enabled);
        _lines[parameter.ValueLine] = ToggleComment(_lines[parameter.ValueLine], enabled);
        parameter.IsEnabled = enabled;
        IsDirty = true;
    }

    public void RestoreText(string text)
    {
        _lines.Clear();
        _lines.AddRange(Regex.Split(text, "\r?\n"));
        Parameters = ParseParameters();
        IsDirty = !GetText().Equals(OriginalText, StringComparison.Ordinal);
    }

    public void Save(bool createBackup = true)
    {
        if (createBackup && File.Exists(FilePath))
        {
            var backupDirectory = Path.Combine(Path.GetDirectoryName(FilePath)!, ".starrail-editor-backups");
            Directory.CreateDirectory(backupDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var extension = Path.GetExtension(FilePath);
            var backupPath = Path.Combine(backupDirectory,
                $"{Path.GetFileNameWithoutExtension(FilePath)}-{stamp}-{Guid.NewGuid():N}{extension}");
            File.Copy(FilePath, backupPath, overwrite: false);
            TrimBackups(backupDirectory, Path.GetFileNameWithoutExtension(FilePath), extension);
        }

        SaveAs(FilePath);
        OriginalText = GetText();
        IsDirty = false;
    }

    public void SaveAs(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("保存路径没有有效目录。");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var content = _encoding.GetBytes(GetText());
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (_preamble.Length > 0) stream.Write(_preamble);
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                try { File.Replace(temporary, fullPath, null); }
                catch (IOException) { File.Move(temporary, fullPath, overwrite: true); }
                catch (PlatformNotSupportedException) { File.Move(temporary, fullPath, overwrite: true); }
            }
            else
            {
                File.Move(temporary, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public IReadOnlyList<DiagnosticItem> ValidateResources(string shaderDirectory)
    {
        var diagnostics = new List<DiagnosticItem>();
        foreach (var parameter in Parameters.Where(parameter => parameter.Kind == ShaderParameterKind.Texture && parameter.IsEnabled))
        {
            var resolved = ResolveResourcePath(parameter.Value, shaderDirectory);
            if (!File.Exists(resolved))
            {
                diagnostics.Add(new DiagnosticItem(DiagnosticSeverity.Error, parameter.Name,
                    $"找不到纹理：{parameter.Value}", "选择存在的纹理，或把路径改成相对于材质文件的路径。"));
            }
            else if (Path.IsPathRooted(parameter.Value))
            {
                diagnostics.Add(new DiagnosticItem(DiagnosticSeverity.Warning, parameter.Name,
                    $"使用了绝对路径：{parameter.Value}", "导出便携包时会自动复制资源并改为相对路径。"));
            }
        }

        foreach (var parameter in Parameters.Where(parameter => parameter.Definition is null))
        {
            diagnostics.Add(new DiagnosticItem(DiagnosticSeverity.Error, parameter.Name,
                "这个参数缺少显式元数据，已禁用滑块编辑。", "更新嵌入式 parameter-definitions.json 后重新检查。"));
        }

        var include = Path.Combine(shaderDirectory, "internal", "shader.hlsl");
        if (!File.Exists(include))
        {
            diagnostics.Add(new DiagnosticItem(DiagnosticSeverity.Error, "shader.hlsl", "缺少核心 Shader。", include));
        }

        return diagnostics;
    }

    public IReadOnlyList<DiagnosticItem> Validate(string shaderDirectory) => ValidateResources(shaderDirectory);

    public IReadOnlyList<SourceChange> GetSourceChanges()
    {
        var original = Regex.Split(OriginalText, "\r?\n");
        var result = new List<SourceChange>();
        for (var line = 0; line < Math.Max(original.Length, _lines.Count); line++)
        {
            var before = line < original.Length ? original[line] : string.Empty;
            var after = line < _lines.Count ? _lines[line] : string.Empty;
            if (before.Equals(after, StringComparison.Ordinal)) continue;
            var parameter = Parameters.FirstOrDefault(item => item.DeclarationLine == line || item.ValueLine == line);
            result.Add(new SourceChange(Name, parameter?.Name ?? string.Empty, before, after, line, true));
        }
        return result;
    }

    public static string ResolveResourcePath(string value, string shaderDirectory)
    {
        var clean = value.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(clean) ? clean : Path.GetFullPath(Path.Combine(shaderDirectory, clean));
    }

    private IReadOnlyList<ShaderParameter> ParseParameters()
    {
        var result = new List<ShaderParameter>();
        var currentGroup = "纹理资源";
        var lastComment = string.Empty;

        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            var section = SectionRegex.Match(line);
            if (section.Success)
            {
                currentGroup = ParameterCatalog.NormalizeGroup(section.Groups[1].Value);
                lastComment = string.Empty;
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.Contains("#define", StringComparison.Ordinal))
            {
                var comment = trimmed.TrimStart('/').Trim();
                if (!comment.StartsWith('=') && !comment.EndsWith("end =============", StringComparison.OrdinalIgnoreCase))
                {
                    lastComment = comment;
                }
                continue;
            }

            var texture = TextureDefineRegex.Match(line);
            if (Kind == ShaderDocumentKind.Material && texture.Success && index + 1 < _lines.Count)
            {
                var name = texture.Groups["name"].Value;
                var path = ExtractQuotedValue(_lines[index + 1]);
                if (path is not null)
                {
                    result.Add(new ShaderParameter
                    {
                        Name = name,
                        DisplayName = ParameterCatalog.DisplayName(name),
                        Group = "纹理资源",
                        Kind = ShaderParameterKind.Texture,
                        Source = ShaderParameterSource.Texture,
                        Value = path,
                        DeclarationLine = index,
                        ValueLine = index + 1,
                        Description = lastComment,
                        IsEnabled = !texture.Groups["comment"].Success,
                        IsAdvanced = false,
                        Definition = ParameterCatalog.ResolveDefinition(name, "纹理资源"),
                    });
                    lastComment = string.Empty;
                    index++;
                    continue;
                }
            }

            var define = DefineRegex.Match(line);
            if (define.Success && IsEditableDefine(define.Groups["name"].Value))
            {
                var name = define.Groups["name"].Value;
                var definition = ParameterCatalog.ResolveDefinition(name, currentGroup);
                var options = ParameterCatalog.OptionsFor(name);
                result.Add(new ShaderParameter
                {
                    Name = name,
                    DisplayName = ParameterCatalog.DisplayName(name),
                    Group = definition?.Group ?? currentGroup,
                    Kind = ShaderParameterKind.Define,
                    Source = ShaderParameterSource.Define,
                    Value = define.Groups["value"].Value,
                    DeclarationLine = index,
                    ValueLine = index,
                    Description = FirstNonEmpty(lastComment, define.Groups["comment"].Value),
                    IsAdvanced = definition?.Advanced ?? ParameterCatalog.IsAdvanced(name, currentGroup),
                    Options = options,
                    Definition = definition,
                });
                lastComment = string.Empty;
                continue;
            }

            var constant = ConstRegex.Match(line);
            if (constant.Success && IsEditableConstant(constant.Groups["name"].Value))
            {
                var name = constant.Groups["name"].Value;
                var type = constant.Groups["type"].Value;
                var value = constant.Groups["value"].Value.Trim();
                var kind = KindFromType(type);
                var numeric = ParseFirstNumber(value);
                var definition = ParameterCatalog.ResolveDefinition(name, currentGroup);
                var range = ParameterCatalog.RangeFor(definition, numeric);
                result.Add(new ShaderParameter
                {
                    Name = name,
                    DisplayName = ParameterCatalog.DisplayName(name),
                    Group = definition?.Group ?? currentGroup,
                    Kind = kind,
                    Source = ShaderParameterSource.Constant,
                    TypeName = type,
                    Value = value,
                    DeclarationLine = index,
                    ValueLine = index,
                    Description = lastComment,
                    IsAdvanced = definition?.Advanced ?? ParameterCatalog.IsAdvanced(name, currentGroup),
                    Minimum = range.Min,
                    Maximum = range.Max,
                    Step = range.Step,
                    Definition = definition,
                });
                lastComment = string.Empty;
                continue;
            }

            var annotated = AnnotatedStartRegex.Match(line);
            if (annotated.Success)
            {
                for (var valueLine = index + 1; valueLine < Math.Min(_lines.Count, index + 16); valueLine++)
                {
                    var assignment = AnnotatedValueRegex.Match(_lines[valueLine]);
                    if (!assignment.Success) continue;
                    var name = annotated.Groups["name"].Value;
                    if (Kind != ShaderDocumentKind.Shadow || !name.Equals("Script", StringComparison.Ordinal)) break;
                    var type = annotated.Groups["type"].Value;
                    var value = assignment.Groups["value"].Value.Trim();
                    var definition = ParameterCatalog.ResolveDefinition(name, "阴影设置");
                    var range = ParameterCatalog.RangeFor(definition, ParseFirstNumber(value));
                    result.Add(new ShaderParameter
                    {
                        Name = name,
                        DisplayName = ParameterCatalog.DisplayName(name),
                        Group = definition?.Group ?? "阴影设置",
                        Kind = KindFromType(type),
                        Source = ShaderParameterSource.Annotated,
                        TypeName = type,
                        Value = value,
                        DeclarationLine = valueLine,
                        ValueLine = valueLine,
                        Description = "控制阴影后处理写入颜色的合成强度。",
                        IsAdvanced = false,
                        Minimum = range.Min,
                        Maximum = range.Max,
                        Step = range.Step,
                        Definition = definition,
                    });
                    index = valueLine;
                    lastComment = string.Empty;
                    break;
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                lastComment = string.Empty;
            }
        }

        return result;
    }

    private static ShaderParameterKind KindFromType(string type) => type switch
    {
        "int" => ShaderParameterKind.Integer,
        "bool" => ShaderParameterKind.Boolean,
        "float2" => ShaderParameterKind.Vector2,
        "float3" => ShaderParameterKind.Vector3,
        "float4" => ShaderParameterKind.Vector4,
        _ => ShaderParameterKind.Float,
    };

    private bool IsEditableDefine(string name)
    {
        if (Kind == ShaderDocumentKind.Shadow) return name == "SHADOW_MAP_SIZE";
        if (Kind != ShaderDocumentKind.Material) return false;
        if (name.Contains("_FROM_CONST", StringComparison.Ordinal) ||
            name.Contains("_FROM_TEX", StringComparison.Ordinal) ||
            name.Contains("_FROM_PMX", StringComparison.Ordinal) ||
            name is "MATERIAL_DOMAIN_COMMON" or "MATERIAL_DOMAIN_FACE" or "SHADOW_NONE" or "SHADOW_4FUN")
        {
            return false;
        }

        return name is "MATERIAL_DOMAIN" or "SHADOW_MODE" or "STARRYSKY" ||
               name.EndsWith("_FROM", StringComparison.Ordinal) ||
               name.EndsWith("_SWIZZLE", StringComparison.Ordinal);
    }

    private bool IsEditableConstant(string name) => Kind switch
    {
        ShaderDocumentKind.Material => true,
        ShaderDocumentKind.ShadowZBuffer => name == "DepthBias",
        _ => false,
    };

    private static string? ExtractQuotedValue(string line)
    {
        var first = line.IndexOf('"');
        var last = line.LastIndexOf('"');
        return first >= 0 && last > first ? line[(first + 1)..last] : null;
    }

    private static string ReplaceTexturePath(string line, string value, bool enabled)
    {
        var indent = line[..(line.Length - line.TrimStart().Length)];
        var prefix = enabled ? string.Empty : "// ";
        return $"{indent}{prefix}\"{value.Replace('\\', '/')}\"";
    }

    private static string ReplaceDefineValue(string line, string value)
    {
        var match = DefineRegex.Match(line);
        if (!match.Success) return line;
        var comment = match.Groups["comment"].Success ? $" // {match.Groups["comment"].Value}" : string.Empty;
        return $"{match.Groups["indent"].Value}#define {match.Groups["name"].Value} {value}{comment}";
    }

    private static string ReplaceConstValue(string line, string value)
    {
        var match = ConstRegex.Match(line);
        if (!match.Success) return line;
        return $"{match.Groups["indent"].Value}const {match.Groups["static"].Value}{match.Groups["type"].Value} {match.Groups["name"].Value} = {value};{match.Groups["tail"].Value}";
    }

    private static string ReplaceAnnotatedValue(string line, string value)
    {
        var match = AnnotatedValueRegex.Match(line);
        return match.Success ? $"{match.Groups["prefix"].Value}{value}{match.Groups["suffix"].Value}" : line;
    }

    private static string ToggleComment(string line, bool enabled)
    {
        var indentLength = line.Length - line.TrimStart().Length;
        var indent = line[..indentLength];
        var content = line[indentLength..];
        if (enabled)
        {
            content = Regex.Replace(content, @"^//\s?", string.Empty);
        }
        else if (!content.StartsWith("//", StringComparison.Ordinal))
        {
            content = "// " + content;
        }
        return indent + content;
    }

    private static double ParseFirstNumber(string value)
    {
        var match = Regex.Match(value, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static (Encoding Encoding, byte[] Preamble) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
            return (new UTF8Encoding(false, true), Encoding.UTF8.GetPreamble());
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
            return (new UnicodeEncoding(false, false, true), Encoding.Unicode.GetPreamble());
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
            return (new UnicodeEncoding(true, false, true), Encoding.BigEndianUnicode.GetPreamble());

        var utf8 = new UTF8Encoding(false, true);
        try
        {
            _ = utf8.GetString(bytes);
            return (utf8, []);
        }
        catch (DecoderFallbackException) { }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var codePage in new[] { 932, 54936 })
        {
            var encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            try
            {
                var text = encoding.GetString(bytes);
                if (encoding.GetBytes(text).AsSpan().SequenceEqual(bytes)) return (encoding, []);
            }
            catch (DecoderFallbackException) { }
            catch (EncoderFallbackException) { }
        }
        throw new InvalidDataException("FX 文件不是受支持的 UTF-8、UTF-16、CP932 或 GB18030 编码。");
    }

    private static ShaderDocumentKind DetermineKind(string filePath) => Path.GetFileName(filePath).ToLowerInvariant() switch
    {
        "shadow.fx" => ShaderDocumentKind.Shadow,
        "shadow_zbuffer.fx" => ShaderDocumentKind.ShadowZBuffer,
        var name when name.EndsWith(".hlsl", StringComparison.Ordinal) => ShaderDocumentKind.Reference,
        _ => ShaderDocumentKind.Material,
    };

    private static void TrimBackups(string backupDirectory, string fileStem, string extension)
    {
        foreach (var oldBackup in Directory.GetFiles(backupDirectory, $"{fileStem}-*{extension}")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(10))
        {
            File.Delete(oldBackup);
        }
    }
}

internal enum DiagnosticSeverity { Info, Warning, Error }

internal sealed record DiagnosticItem(DiagnosticSeverity Severity, string Source, string Message, string Recovery);
