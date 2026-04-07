namespace vectrun.Models.Clients;

internal class AIClientOptions
{
    public required string Endpoint { get; init; }
    public required string Model { get; init; }
    public string? ApiKey { get; init; }
}