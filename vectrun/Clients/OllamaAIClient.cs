namespace vectrun.Clients;

using System.Text.Json;
using vectrun.Clients.Primitives;
using vectrun.Models.Clients;

internal class OllamaAIClient : BaseAIClient
{
    public OllamaAIClient(HttpClient httpClient, AIClientOptions options)
        : base(httpClient, options) { }

    public override async Task<AIChatResponse> SendAsync(
        AIChatRequest request,
        CancellationToken token)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Options.Model,
            ["stream"] = false,
            ["messages"] = request.Messages.Select(m =>
            {
                if (m.Role == "tool")
                    return (object)new { role = "tool", content = m.Content };

                if (m.Role == "assistant" && m.ToolCalls?.Count > 0)
                    return new
                    {
                        role = "assistant",
                        content = m.Content,
                        tool_calls = m.ToolCalls.Select(tc => new
                        {
                            function = new { name = tc.Name, arguments = tc.Arguments }
                        }).ToList()
                    };

                return new { role = m.Role, content = m.Content };
            }).ToList(),
            ["tools"] = request.Tools?.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters }
            }).ToList()
        };

        if (request.ResponseFormat?.Type == "json")
            payload["format"] = request.ResponseFormat.Schema ?? "json";

        var res = await PostJsonAsync("/api/chat", payload, token);

        var msg = res.GetProperty("message");

        var toolCalls = msg.TryGetProperty("tool_calls", out var tcEl)
            ? tcEl.EnumerateArray().Select(tc =>
            {
                var fn = tc.GetProperty("function");
                var args = fn.GetProperty("arguments");
                return new AIToolCall
                {
                    Id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString()! : Guid.NewGuid().ToString(),
                    Name = fn.GetProperty("name").GetString()!,
                    // Ollama returns arguments as an object; serialize back to JSON string
                    Arguments = args.ValueKind == JsonValueKind.String
                        ? args.GetString()!
                        : JsonSerializer.Serialize(args, JsonOptions)
                };
            }).ToList()
            : null;

        return new AIChatResponse
        {
            Message = new AIMessage
            {
                Role = msg.GetProperty("role").GetString()!,
                Content = msg.GetProperty("content").GetString() ?? "",
                ToolCalls = toolCalls
            },
            ToolCalls = toolCalls
        };
    }
}
