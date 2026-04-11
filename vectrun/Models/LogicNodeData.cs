namespace vectrun.Models;

internal class LogicNodeData : NodeData
{
    public required string LogicType { get; set; } // "process" or "script"
    public List<string> NextNodeIds { get; set; } = new();

    // Process
    public string? ProcessPath { get; set; }
    public string ProcessPathType { get; set; } = "relative"; // "relative" (to pipeline dir) or "absolute"
    public string? ProcessInput { get; set; }  // if set, piped to stdin instead of the previous node's output

    // Script
    public string? ScriptLanguage { get; set; }
    public string? Script { get; set; }
}
