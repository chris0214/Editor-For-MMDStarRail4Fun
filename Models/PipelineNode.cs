namespace StarRailShaderEditor.Models;

internal sealed class PipelineNode
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Group { get; init; }
    public required Color Color { get; init; }
    public PointF Position { get; set; }
    public bool Enabled { get; set; } = true;
    public bool CanToggle { get; init; }
}

internal sealed record PipelineLink(string SourceId, string TargetId);
