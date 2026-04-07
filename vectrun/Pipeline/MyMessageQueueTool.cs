namespace vectrun.Pipeline;

using System.Text.Json;
using vectrun.Models.Clients;
using vectrun.Pipeline.Contracts;

internal class MyMessageQueueTool : IToolDefinition
{
    private readonly AgentMessageQueues _queues;

    public MyMessageQueueTool(AgentMessageQueues queues) => _queues = queues;

    public string Name => "my_message_queue";

    public AITool ToAITool() => new()
    {
        Name = "my_message_queue",
        Description = "Dequeues and returns a single message from the specified agent's queue. Returns an empty string if the queue is empty.",
        Parameters = new
        {
            type = "object",
            properties = new { agentId = new { type = "string", description = "The ID of the agent whose queue to read from" } },
            required = new[] { "agentId" }
        }
    };

    public Task<string> ExecuteAsync(string arguments, CancellationToken token)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(arguments);
        var agentId = args.GetProperty("agentId").GetString()!;

        return Task.FromResult(_queues.Dequeue(agentId) ?? "");
    }
}
