namespace vectrun.Models.Clients;

internal class AIResponseFormat
{
    public required string Type { get; init; } // "json" or "text"
    public object? Schema { get; init; } // JSON schema when Type == "json"
}