namespace vectrun.Models.Clients;

internal class AITool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required object Parameters { get; init; }
}