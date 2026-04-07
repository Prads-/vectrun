namespace vectrun.Models;

internal class LogicNodeData
{
    public string? Name { get; set; }
    public required string LogicType { get; set; } // "process" or "script"
    public List<string> NextNodeIds { get; set; } = new();

    // Process
    public string? ProcessPath { get; set; }

    // Script
    public string? ScriptLanguage { get; set; }
    public string? Script { get; set; }
}