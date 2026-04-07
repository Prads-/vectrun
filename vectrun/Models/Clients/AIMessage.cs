namespace vectrun.Models.Clients;

internal class AIMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public string? Name { get; init; }
    public string? ToolCallId { get; init; }
    public List<AIToolCall>? ToolCalls { get; init; }
}