namespace vectrun.Models.Clients;

internal class AIChatResponse
{
    public required AIMessage Message { get; init; }
    public List<AIToolCall>? ToolCalls { get; init; }
}