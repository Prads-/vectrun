namespace vectrun.Models.Config;

internal class ModelConfig
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; } // "ollama", "vllm", "llama.cpp", "open_ai", "anthropic"
    public required string Endpoint { get; init; }
    public string? ApiKey { get; init; }
}
