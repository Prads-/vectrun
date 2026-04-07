namespace vectrun.Models.Clients;

internal class AIToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Arguments { get; init; }
}