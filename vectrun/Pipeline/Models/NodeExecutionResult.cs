namespace vectrun.Pipeline.Models;

internal class NodeExecutionResult
{
    public string? Output { get; init; }
    public List<string>? NextNodeIds { get; init; }
}
