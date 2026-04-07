namespace vectrun.Models.Config;

internal class AgentConfig
{
    public required string AgentName { get; init; }
    public required string SystemPrompt { get; init; }
    public required string ModelId { get; init; }
    public string Output { get; init; } = "plain_text"; // "plain_text" or "json"
    public object? OutputSchema { get; init; }
    public string? Prompt { get; init; }
    public List<string>? ToolIds { get; init; }
}
