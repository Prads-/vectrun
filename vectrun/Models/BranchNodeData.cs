namespace vectrun.Models;

internal class BranchNodeData
{
    public string? Name { get; set; }
    public required string ExpectedOutput { get; set; }
    public required List<string> TrueNodeIds { get; set; }
    public required List<string> FalseNodeIds { get; set; }
}