namespace vectrun.Models.Config;

internal class ToolConfig
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required object Parameters { get; init; }
    public required string Path { get; init; }
    public string PathType { get; init; } = "relative"; // "relative" (to tools/) or "absolute"
}
