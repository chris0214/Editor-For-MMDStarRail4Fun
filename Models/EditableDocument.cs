namespace StarRailShaderEditor.Models;

internal interface IEditableDocument
{
    string FilePath { get; }
    string OriginalText { get; }
    IReadOnlyList<ShaderParameter> Parameters { get; }
    string GetText();
    void SetValue(ShaderParameter parameter, string value);
    void Save(bool createBackup = true);
    IReadOnlyList<StarRailShaderEditor.Services.DiagnosticItem> Validate(string shaderDirectory);
}

internal sealed record SourceChange(
    string Document,
    string ParameterId,
    string OriginalLine,
    string CurrentLine,
    int Line,
    bool Modified);
