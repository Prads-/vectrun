namespace vectrun.Pipeline;

using System.Collections.Concurrent;

internal class AgentMessageQueues
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _queues = new();

    public void Enqueue(string agentId, string message)
        => _queues.GetOrAdd(agentId, _ => new())
            .Enqueue(message);

    public string? Dequeue(string agentId)
    {
        if (_queues.TryGetValue(agentId, out var queue) && queue.TryDequeue(out var msg))
            return msg;

        return null;
    }
}
