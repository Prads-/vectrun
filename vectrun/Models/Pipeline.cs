namespace vectrun.Models;

internal class Pipeline
{
    public required string PipelineName { get; set; }
    public required string StartNodeId { get; set; }
    public required List<Node> Nodes { get; set; }
}