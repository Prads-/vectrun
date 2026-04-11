namespace vectrun.Pipeline;

using System.Text.Json;
using vectrun.Clients;
using vectrun.Clients.Contracts;
using vectrun.Models;
using vectrun.Models.Clients;
using vectrun.Models.Config;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;
using Node = vectrun.Models.Node;
using Pipeline = vectrun.Models.Pipeline;

internal static class PipelineBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ReservedToolNames = ["my_message_queue", "queue_message"];

    public static BuiltPipeline Build(string directory)
    {
        var pipeline = Read<Pipeline>(directory, "pipeline.json");
        var models = Read<List<ModelConfig>>(directory, "models.json").ToDictionary(m => m.Id);
        var tools = BuildTools(directory);
        var agents = LoadAgents(directory);
        var queues = new AgentMessageQueues();

        var nodes = pipeline.Nodes.ToDictionary(
            n => n.Id,
            n => BuildNode(n, models, tools, agents, queues, directory));

        return new BuiltPipeline
        {
            StartNodeId = pipeline.StartNodeId,
            Nodes = nodes
        };
    }

    private static Dictionary<string, IToolDefinition> BuildTools(string directory)
    {
        var toolsDir = Path.Combine(directory, "tools");
        return Read<List<ToolConfig>>(directory, "tools.json").ToDictionary(
            t => t.Name,
            t =>
            {
                if (ReservedToolNames.Contains(t.Name))
                    throw new InvalidOperationException($"Tool name '{t.Name}' is reserved by the runtime.");
                return (IToolDefinition)new ToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Parameters,
                    Path = t.PathType == "absolute" ? t.Path : Path.Combine(toolsDir, t.Path)
                };
            });
    }

    private static Dictionary<string, AgentConfig> LoadAgents(string directory)
    {
        var agentsDir = Path.Combine(directory, "agents");
        return Directory.GetFiles(agentsDir, "*.json")
            .Select(f => JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(f), JsonOptions)!)
            .ToDictionary(a => a.AgentName);
    }

    private static INode BuildNode(
        Node node,
        Dictionary<string, ModelConfig> models,
        Dictionary<string, IToolDefinition> tools,
        Dictionary<string, AgentConfig> agents,
        AgentMessageQueues queues,
        string directory)
    {
        var data = (JsonElement)node.Data;

        return node.Type switch
        {
            "agent" => BuildAgentNode(node.Id, data.Deserialize<AgentNodeData>(JsonOptions)!, models, tools, agents, queues),
            "branch" => new BranchNode(node.Id, data.Deserialize<BranchNodeData>(JsonOptions)!),
            "logic" => new LogicNode(node.Id, ResolveLogicNodeData(data.Deserialize<LogicNodeData>(JsonOptions)!, directory)),
            "wait" => new WaitNode(node.Id, data.Deserialize<WaitNodeData>(JsonOptions)!),
            _ => throw new InvalidOperationException($"Unknown node type: {node.Type}")
        };
    }

    private static LogicNodeData ResolveLogicNodeData(LogicNodeData data, string directory)
    {
        if (data.LogicType != "process" || data.ProcessPath is null || data.ProcessPathType == "absolute")
            return data;

        data.ProcessPath = Path.Combine(directory, data.ProcessPath);
        return data;
    }

    private static INode BuildAgentNode(
        string id,
        AgentNodeData data,
        Dictionary<string, ModelConfig> models,
        Dictionary<string, IToolDefinition> tools,
        Dictionary<string, AgentConfig> agents,
        AgentMessageQueues queues)
    {
        var agentConfig = agents[data.AgentId];
        var modelConfig = models[agentConfig.ModelId];

        // Tool IDs from agent config are the source of truth
        var toolIds = agentConfig.ToolIds;
        var resolvedTools = toolIds?.Select(tid => tools[tid]) ?? [];

        IToolDefinition[] builtInTools = [new MyMessageQueueTool(queues), new QueueMessageTool(queues)];

        var resolvedData = new AgentNodeData
        {
            Name = data.Name,
            AgentId = data.AgentId,
            NextNodeIds = data.NextNodeIds,
            ToolIds = toolIds,
            Retry = data.Retry
        };

        return new AgentNode(id, resolvedData, CreateClient(modelConfig), resolvedTools, builtInTools, agentConfig);
    }

    private static IAIClient CreateClient(ModelConfig model)
    {
        var options = new AIClientOptions
        {
            Endpoint = model.Endpoint,
            Model = model.Id,
            ApiKey = model.ApiKey
        };

        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        return model.Type switch
        {
            "anthropic" => new AnthropicAIClient(http, options),
            "open_ai" => new OpenAiAIClient(http, options),
            "ollama" => new OllamaAIClient(http, options),
            "vllm" => new VllmAIClient(http, options),
            "llama.cpp" => new LlamaCppAIClient(http, options),
            _ => throw new InvalidOperationException($"Unknown model type: {model.Type}")
        };
    }

    private static T Read<T>(string directory, string filename)
    {
        var json = File.ReadAllText(Path.Combine(directory, filename));
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }
}
