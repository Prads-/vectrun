namespace vectrun.Pipeline;

using System.Text.Json;
using vectrun.Models.Clients;
using vectrun.Pipeline.Contracts;

internal class QueueMessageTool : IToolDefinition
{
    private readonly AgentMessageQueues _queues;

    public QueueMessageTool(AgentMessageQueues queues) => _queues = queues;

    public string Name => "queue_message";

    public AITool ToAITool() => new()
    {
        Name = "queue_message",
        Description = "Enqueues a message onto the specified agent's queue.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                agentId = new { type = "string", description = "The ID of the agent to send the message to" },
                message = new { type = "string", description = "The message to enqueue" }
            },
            required = new[] { "agentId", "message" }
        }
    };

    public Task<string> ExecuteAsync(string arguments, CancellationToken token)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(arguments);

        var agentId = args.GetProperty("agentId").GetString()!;
        var message = args.GetProperty("message").GetString()!;
        
        _queues.Enqueue(agentId, message);
        
        return Task.FromResult("ok");
    }
}
