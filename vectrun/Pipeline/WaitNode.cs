namespace vectrun.Pipeline;

using vectrun.Models;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;

internal class WaitNode : INode
{
    private readonly WaitNodeData _data;

    public WaitNode(string id, WaitNodeData data)
    {
        Id = id;
        _data = data;
    }

    public string Id { get; }
    public string Type => "wait";
    public string? Name => _data.Name;

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken token)
    {
        await Task.Delay(_data.DurationMs, token);

        return new NodeExecutionResult
        {
            Output = context.Input,
            NextNodeIds = _data.NextNodeIds
        };
    }
}