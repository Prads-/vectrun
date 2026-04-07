namespace vectrun.Pipeline.Contracts;

using vectrun.Pipeline.Models;

internal interface INode
{
    string Id { get; }
    string Type { get; }
    string? Name { get; }

    Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken token);
}
