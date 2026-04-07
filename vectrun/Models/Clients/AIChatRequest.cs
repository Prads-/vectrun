namespace vectrun.Models.Clients;

internal class AIChatRequest
{
    public required List<AIMessage> Messages { get; init; }
    public List<AITool>? Tools { get; init; }
    public AIResponseFormat? ResponseFormat { get; init; }
}