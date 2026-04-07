namespace vectrun.Models;

public record PipelineLogEntry(
    DateTimeOffset Timestamp,
    string NodeId,
    string NodeType,
    string? NodeName,
    string Event,       // "started" | "output" | "tool_call" | "tool_result" | "error"
    string? Message
);
