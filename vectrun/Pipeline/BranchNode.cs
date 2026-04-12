namespace vectrun.Pipeline;

using vectrun.Models;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;

internal class BranchNode : INode
{
    private readonly BranchNodeData _data;

    public BranchNode(string id, BranchNodeData data)
    {
        Id = id;
        _data = data;
    }

    public string Id { get; }
    public string Type => "branch";
    public string? Name => _data.Name;

    public Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken token)
    {
        var isMatch = string.IsNullOrEmpty(_data.ExpectedOutput)
            || string.Equals(context.Input?.Trim(), _data.ExpectedOutput, StringComparison.Ordinal);

        return Task.FromResult(new NodeExecutionResult
        {
            Output = context.Input,
            NextNodeIds = isMatch 
                ? _data.TrueNodeIds
                : _data.FalseNodeIds
        });
    }
}