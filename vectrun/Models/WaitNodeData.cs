namespace vectrun.Models;

internal class WaitNodeData
{
    public string? Name { get; set; }
    public required int DurationMs { get; set; }
    public List<string> NextNodeIds { get; set; } = [];
}