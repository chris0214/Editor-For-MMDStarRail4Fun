namespace StarRailShaderEditor.Models;

internal enum ShaderParameterKind
{
    Float,
    Integer,
    Boolean,
    Vector2,
    Vector3,
    Vector4,
    Define,
    Texture,
}

internal enum ShaderParameterSource
{
    Constant,
    Define,
    Texture,
    Annotated,
}

internal enum ShaderDocumentKind
{
    Material,
    Shadow,
    ShadowZBuffer,
    Reference,
}

internal sealed record ParameterComponentDefinition(
    string Label,
    double SoftMinimum,
    double SoftMaximum,
    double HardMinimum,
    double HardMaximum,
    double Step);

internal sealed record ParameterDefinition(
    string Pattern,
    string Group,
    string NodeId,
    string Unit,
    bool Advanced,
    IReadOnlyList<ParameterComponentDefinition> Components);

internal sealed class ShaderParameter
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Group { get; init; }
    public required ShaderParameterKind Kind { get; init; }
    public required ShaderParameterSource Source { get; init; }
    public required string Value { get; set; }
    public required int DeclarationLine { get; init; }
    public required int ValueLine { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsAdvanced { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Step { get; init; }
    public IReadOnlyList<ParameterOption> Options { get; init; } = [];
    public ParameterDefinition? Definition { get; init; }

    public bool IsColor => Kind is ShaderParameterKind.Vector3 or ShaderParameterKind.Vector4 &&
        !Name.Contains("MapST", StringComparison.OrdinalIgnoreCase) &&
        !Name.Contains("MapSpeed", StringComparison.OrdinalIgnoreCase) &&
        !Name.Contains("Offset", StringComparison.OrdinalIgnoreCase) &&
        !Name.Contains("Range", StringComparison.OrdinalIgnoreCase) &&
        (Name.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
         Name.Contains("Tint", StringComparison.OrdinalIgnoreCase));

    public double[] NumericValues()
    {
        var text = Value.Trim();
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            text = text[(open + 1)..close];
        }

        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d)
            .ToArray();
    }

    public ParameterComponentDefinition ComponentAt(int index)
    {
        if (Definition?.Components.Count > 0)
        {
            return Definition.Components[Math.Min(index, Definition.Components.Count - 1)];
        }

        return new ParameterComponentDefinition(index.ToString(), Minimum, Maximum, Minimum, Maximum, Step);
    }
}

internal sealed record ParameterOption(string Label, string Value);
