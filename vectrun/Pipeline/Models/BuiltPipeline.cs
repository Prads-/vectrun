namespace vectrun.Pipeline.Models;

using vectrun.Pipeline.Contracts;

internal class BuiltPipeline
{
    public required string StartNodeId { get; init; }
    public required Dictionary<string, INode> Nodes { get; init; }
}
