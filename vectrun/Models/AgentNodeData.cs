namespace vectrun.Models;

internal class AgentNodeData
{
    public string? Name { get; set; }
    public required string AgentId { get; set; }
    public List<string> NextNodeIds { get; set; } = [];
    public List<string>? ToolIds { get; init; }
}