namespace vectrun.Pipeline;

using System.Threading.Channels;
using vectrun.Models;
using vectrun.Pipeline.Models;

internal static class PipelineRunner
{
    public static Task RunAsync(
        BuiltPipeline pipeline,
        string? input = null,
        ChannelWriter<PipelineLogEntry>? log = null,
        CancellationToken token = default)
        => ExecuteNodeAsync(pipeline, pipeline.StartNodeId, input, log, token);

    private static async Task ExecuteNodeAsync(
        BuiltPipeline pipeline,
        string nodeId,
        string? input,
        ChannelWriter<PipelineLogEntry>? log,
        CancellationToken token)
    {
        var node = pipeline.Nodes[nodeId];

        log?.TryWrite(new PipelineLogEntry(
            DateTimeOffset.UtcNow, node.Id, node.Type, node.Name, "started", input));

        var result = await node.ExecuteAsync(
            new NodeExecutionContext { Input = input, Log = log }, token);

        log?.TryWrite(new PipelineLogEntry(
            DateTimeOffset.UtcNow, node.Id, node.Type, node.Name, "output", result.Output));

        if (result.NextNodeIds is not { Count: > 0 })
            return;

        await Task.WhenAll(result.NextNodeIds.Select(
            nextId => ExecuteNodeAsync(pipeline, nextId, result.Output, log, token)));
    }
}
