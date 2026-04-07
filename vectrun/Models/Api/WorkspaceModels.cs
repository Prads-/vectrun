namespace vectrun.Models.Api;

using System.Text.Json;
using System.Text.Json.Nodes;

public record ScaffoldRequest(string Directory);

public record SaveWorkspaceRequest(
    string Directory,
    JsonElement Pipeline,
    JsonElement Models,
    JsonElement Tools,
    JsonElement Agents);

public record WorkspaceData(
    JsonNode? Pipeline,
    JsonNode? Models,
    JsonNode? Tools,
    JsonArray Agents);

public record BrowseEntry(string Name, string Path);

public record BrowseResult(string Current, string? Parent, IReadOnlyList<BrowseEntry> Entries);

public class WorkspaceMissingException(string directory)
    : Exception($"No pipeline.json found in '{directory}'.");

public class PipelineAlreadyRunningException()
    : Exception("A pipeline run is already in progress.");
