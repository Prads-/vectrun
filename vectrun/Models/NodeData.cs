namespace vectrun.Models;

internal abstract class NodeData
{
    public string? Name { get; set; }
    public RetryPolicy? Retry { get; init; }
}
