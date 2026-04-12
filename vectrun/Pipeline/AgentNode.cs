namespace vectrun.Pipeline;

using vectrun.Clients.Contracts;
using vectrun.Models;
using vectrun.Models.Clients;
using vectrun.Models.Config;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;
using PipelineLogEntry = vectrun.Models.PipelineLogEntry;

internal class AgentNode : BaseNode<AgentNodeData>
{
    private readonly IAIClient _aiClient;
    private readonly Dictionary<string, IToolDefinition> _tools;
    private readonly List<IToolDefinition> _builtInTools;
    private readonly AgentConfig? _agentConfig;

    public AgentNode(
        string id,
        AgentNodeData data,
        IAIClient aiClient,
        IEnumerable<IToolDefinition> tools,
        IEnumerable<IToolDefinition>? builtInTools = null,
        AgentConfig? agentConfig = null)
        : base(id, "agent", data)
    {
        _aiClient = aiClient;
        _agentConfig = agentConfig;
        _builtInTools = builtInTools?.ToList() ?? [];
        _tools = tools.Concat(_builtInTools).ToDictionary(s => s.Name);
    }

    protected override async Task<NodeExecutionResult> ExecuteCoreAsync(
        NodeExecutionContext context,
        CancellationToken token)
    {
        var messages = new List<AIMessage>();

        if (!string.IsNullOrEmpty(_agentConfig?.SystemPrompt))
            messages.Add(new AIMessage { Role = "system", Content = _agentConfig.SystemPrompt });

        var userContent = _agentConfig?.Prompt is not null
            ? _agentConfig.Prompt.Replace("{PREVIOUS_AGENT_OUTPUT}", context.Input ?? "")
            : context.Input;

        if (!string.IsNullOrEmpty(userContent))
            messages.Add(new AIMessage { Role = "user", Content = userContent });

        var request = new AIChatRequest
        {
            Messages = messages,
            Tools = ResolveTools(),
            ResponseFormat = _agentConfig?.Output == "json"
                ? new AIResponseFormat { Type = "json", Schema = _agentConfig.OutputSchema }
                : null
        };

        while (true)
        {
            var response = await _aiClient.SendAsync(request, token);

            request.Messages.Add(response.Message);

            if (response.ToolCalls == null || response.ToolCalls.Count == 0)
            {
                var output = response.Message.Content;
                if (_agentConfig?.Output == "json")
                    output = StripMarkdownCodeFence(output);

                return new NodeExecutionResult
                {
                    Output = output,
                    NextNodeIds = Data.NextNodeIds
                };
            }

            if (!string.IsNullOrWhiteSpace(response.Message.Content))
            {
                context.Log?.TryWrite(new PipelineLogEntry(
                    DateTimeOffset.UtcNow,
                    Id,
                    Type,
                    Name,
                    "output",
                    response.Message.Content));
            }

            foreach (var toolCall in response.ToolCalls)
            {
                context.Log?.TryWrite(new PipelineLogEntry(
                    DateTimeOffset.UtcNow, Id, Type, Name, "tool_call",
                    $"{toolCall.Name}({toolCall.Arguments})"));

                var result = await ExecuteTool(toolCall, context.Log, token);

                context.Log?.TryWrite(new PipelineLogEntry(
                    DateTimeOffset.UtcNow, Id, Type, Name, "tool_result", result));

                request.Messages.Add(new AIMessage
                {
                    Role = "tool",
                    Name = toolCall.Name,
                    ToolCallId = toolCall.Id,
                    Content = result
                });
            }
        }
    }

    private List<AITool>? ResolveTools()
    {
        var userTools = Data
            .ToolIds?
            .Select(id => _tools[id].ToAITool())
            ?? [];

        var all = userTools
            .Concat(_builtInTools.Select(t => t.ToAITool()))
            .ToList();

        return all.Count > 0 ? all : null;
    }

    // Models sometimes wrap JSON output in markdown code fences (```json ... ```)
    // even when a structured format is requested. Strip them so downstream nodes
    // always receive raw JSON.
    private static string StripMarkdownCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith('`')) return trimmed;

        var start = trimmed.IndexOfAny(['{', '[']);
        if (start < 0) return trimmed;

        return trimmed[start..].TrimEnd('`');
    }

    private Task<string> ExecuteTool(
        AIToolCall call,
        System.Threading.Channels.ChannelWriter<PipelineLogEntry>? log,
        CancellationToken token)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
            throw new InvalidOperationException($"Tool not found: {call.Name}");

        Action<string>? onLog = log == null ? null : line =>
            log.TryWrite(new PipelineLogEntry(DateTimeOffset.UtcNow, Id, Type, Name, "tool_log", line));

        return tool.ExecuteAsync(call.Arguments, onLog, token);
    }
}
