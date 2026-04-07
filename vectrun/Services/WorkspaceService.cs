namespace vectrun.Services;

using System.Text.Json;
using System.Text.Json.Nodes;
using vectrun.Models.Api;

public class WorkspaceService
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public WorkspaceData Load(string directory)
    {
        var pipelinePath = Path.Combine(directory, "pipeline.json");
        if (!File.Exists(pipelinePath))
            throw new WorkspaceMissingException(directory);

        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath));

        var modelsPath = Path.Combine(directory, "models.json");
        var models = File.Exists(modelsPath)
            ? JsonNode.Parse(File.ReadAllText(modelsPath))
            : JsonNode.Parse("[]");

        var toolsPath = Path.Combine(directory, "tools.json");
        var tools = File.Exists(toolsPath)
            ? JsonNode.Parse(File.ReadAllText(toolsPath))
            : JsonNode.Parse("[]");

        var agentsList = new JsonArray();
        var agentsDir = Path.Combine(directory, "agents");
        if (Directory.Exists(agentsDir))
        {
            foreach (var file in Directory.GetFiles(agentsDir, "*.json"))
            {
                var node = JsonNode.Parse(File.ReadAllText(file));
                if (node != null)
                    agentsList.Add(node.DeepClone());
            }
        }

        return new WorkspaceData(pipeline, models, tools, agentsList);
    }

    public WorkspaceData Scaffold(string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "agents"));
        Directory.CreateDirectory(Path.Combine(directory, "tools"));

        File.WriteAllText(
            Path.Combine(directory, "pipeline.json"),
            JsonSerializer.Serialize(
                new { pipelineName = "New Pipeline", startNodeId = "", nodes = Array.Empty<object>() },
                Indented));
        File.WriteAllText(Path.Combine(directory, "models.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "tools.json"), "[]");

        var pipeline = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "pipeline.json")));
        return new WorkspaceData(pipeline, JsonNode.Parse("[]"), JsonNode.Parse("[]"), new JsonArray());
    }

    public void Save(SaveWorkspaceRequest req)
    {
        File.WriteAllText(Path.Combine(req.Directory, "pipeline.json"),
            JsonSerializer.Serialize(req.Pipeline, Indented));
        File.WriteAllText(Path.Combine(req.Directory, "models.json"),
            JsonSerializer.Serialize(req.Models, Indented));
        File.WriteAllText(Path.Combine(req.Directory, "tools.json"),
            JsonSerializer.Serialize(req.Tools, Indented));

        var agentsDir = Path.Combine(req.Directory, "agents");
        Directory.CreateDirectory(agentsDir);
        foreach (var file in Directory.GetFiles(agentsDir, "*.json"))
            File.Delete(file);

        if (req.Agents.ValueKind == JsonValueKind.Array)
        {
            foreach (var agent in req.Agents.EnumerateArray())
            {
                var agentName = agent.TryGetProperty("agentName", out var nameProp)
                    ? nameProp.GetString() ?? "agent"
                    : "agent";
                var safeFileName = string.Concat(agentName.Split(Path.GetInvalidFileNameChars()));
                File.WriteAllText(
                    Path.Combine(agentsDir, $"{safeFileName}.json"),
                    JsonSerializer.Serialize(agent, Indented));
            }
        }
    }

    public string GetPipelineJson(string directory) =>
        File.ReadAllText(Path.Combine(directory, "pipeline.json"));
}
