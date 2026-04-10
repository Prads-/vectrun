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
                return new NodeExecutionResult
                {
                    Output = response.Message.Content,
                    NextNodeIds = Data.NextNodeIds
                };
            }

            foreach (var toolCall in response.ToolCalls)
            {
                context.Log?.TryWrite(new PipelineLogEntry(
                    DateTimeOffset.UtcNow, Id, Type, Name, "tool_call",
                    $"{toolCall.Name}({toolCall.Arguments})"));

                var result = await ExecuteTool(toolCall, token);

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

    private Task<string> ExecuteTool(AIToolCall call, CancellationToken token)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
            throw new InvalidOperationException($"Tool not found: {call.Name}");

        return tool.ExecuteAsync(call.Arguments, token);
    }
}
