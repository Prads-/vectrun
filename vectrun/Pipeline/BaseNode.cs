namespace vectrun.Pipeline;

using vectrun.Models;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;
using PipelineLogEntry = vectrun.Models.PipelineLogEntry;

internal abstract class BaseNode<TData> : INode where TData : NodeData
{
    protected BaseNode(
        string id,
        string type,
        TData data)
    {
        Id = id;
        Type = type;
        Data = data;
    }

    public string Id { get; }
    public string Type { get; }
    public string? Name => Data.Name;

    protected TData Data { get; }

    protected abstract Task<NodeExecutionResult> ExecuteCoreAsync(
        NodeExecutionContext context,
        CancellationToken token);

    public Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken token)
    {
        var retry = Data.Retry;

        if (retry == null || retry.RetryCount == 0)
            return ExecuteCoreAsync(context, token);

        return ExecuteWithRetryAsync(
            retry,
            context,
            token);
    }

    private async Task<NodeExecutionResult> ExecuteWithRetryAsync(
        RetryPolicy retry,
        NodeExecutionContext context,
        CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ExecuteCoreAsync(context, token);
            }
            catch (Exception ex)
            {
                if (attempt < retry.RetryCount)
                {
                    context.Log?.TryWrite(new PipelineLogEntry(
                        DateTimeOffset.UtcNow,
                        Id,
                        Type,
                        Name,
                        "retry",
                        $"Attempt {attempt + 1}/{retry.RetryCount}: {ex.Message}"));

                    await Task.Delay(ComputeDelay(retry, attempt), token);
                }
                else
                {
                    context.Log?.TryWrite(new PipelineLogEntry(
                        DateTimeOffset.UtcNow,
                        Id,
                        Type,
                        Name,
                        "failed",
                        ex.Message));

                    throw;
                }
            }
        }
    }

    private static int ComputeDelay(RetryPolicy retry, int attempt) =>
        retry.DelayType == "sliding"
            ? retry.RetryDelayMs * (int)Math.Pow(2, attempt)
            : retry.RetryDelayMs;
}
