namespace vectrun.Clients;

using System.Text.Json;
using vectrun.Clients.Primitives;
using vectrun.Models.Clients;

internal class AnthropicAIClient : BaseAIClient
{
    public AnthropicAIClient(HttpClient httpClient, AIClientOptions options)
        : base(httpClient, options) { }

    public override async Task<AIChatResponse> SendAsync(
        AIChatRequest request,
        CancellationToken token)
    {
        var systemMessage = request.Messages.FirstOrDefault(m => m.Role == "system");

        var payload = new Dictionary<string, object?>
        {
            ["model"] = Options.Model,
            ["max_tokens"] = 8096,
            ["messages"] = BuildMessages(request.Messages.Where(m => m.Role != "system"))
        };

        if (systemMessage is not null)
            payload["system"] = systemMessage.Content;

        if (request.Tools?.Count > 0)
            payload["tools"] = request.Tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = t.Parameters
            });

        if (request.ResponseFormat?.Type == "json")
        {
            var schema = JsonSerializer.Serialize(request.ResponseFormat.Schema, JsonOptions);
            var instruction = $"You MUST respond in valid JSON matching this schema:\n{schema}";

            payload["system"] = payload.TryGetValue("system", out var sys)
                ? $"{sys}\n\n{instruction}"
                : instruction;
        }

        var res = await PostJsonAsync("/v1/messages", payload, token);

        var text = "";
        List<AIToolCall>? toolCalls = null;

        foreach (var block in res.GetProperty("content").EnumerateArray())
        {
            switch (block.GetProperty("type").GetString())
            {
                case "text":
                    text = block.GetProperty("text").GetString() ?? "";
                    break;
                case "tool_use":
                    toolCalls ??= [];
                    toolCalls.Add(new AIToolCall
                    {
                        Id = block.GetProperty("id").GetString()!,
                        Name = block.GetProperty("name").GetString()!,
                        Arguments = JsonSerializer.Serialize(block.GetProperty("input"), JsonOptions)
                    });
                    break;
            }
        }

        return new AIChatResponse
        {
            Message = new AIMessage
            {
                Role = "assistant",
                Content = text,
                ToolCalls = toolCalls
            },
            ToolCalls = toolCalls
        };
    }

    protected override void ApplyHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Options.ApiKey))
            request.Headers.Add("x-api-key", Options.ApiKey);

        request.Headers.Add("anthropic-version", "2023-06-01");
    }

    private static List<object> BuildMessages(IEnumerable<AIMessage> messages)
    {
        var result = new List<object>();
        var list = messages.ToList();
        var i = 0;

        while (i < list.Count)
        {
            var m = list[i];

            if (m.Role == "tool")
            {
                // Consecutive tool results are grouped into a single user message
                var toolResults = new List<object>();
                while (i < list.Count && list[i].Role == "tool")
                {
                    toolResults.Add(new
                    {
                        type = "tool_result",
                        tool_use_id = list[i].ToolCallId,
                        content = list[i].Content
                    });
                    i++;
                }
                result.Add(new { role = "user", content = toolResults });
            }
            else if (m.Role == "assistant" && m.ToolCalls?.Count > 0)
            {
                result.Add(new
                {
                    role = "assistant",
                    content = m.ToolCalls.Select(tc => (object)new
                    {
                        type = "tool_use",
                        id = tc.Id,
                        name = tc.Name,
                        input = JsonSerializer.Deserialize<JsonElement>(tc.Arguments)
                    })
                });
                i++;
            }
            else
            {
                result.Add(new { role = m.Role, content = m.Content });
                i++;
            }
        }

        return result;
    }
}
