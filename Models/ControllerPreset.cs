namespace StarRailShaderEditor.Models;

internal sealed record ControllerMorph(string Name, string EnglishName, byte Panel, byte Type);

internal sealed record ControllerModel(string Name, string EnglishName, IReadOnlyList<ControllerMorph> Morphs);

internal sealed class ControllerPreset
{
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "当前预设";
    public string ModelName { get; set; } = string.Empty;
    public Dictionary<string, float> MorphWeights { get; set; } = new(StringComparer.Ordinal);
}
