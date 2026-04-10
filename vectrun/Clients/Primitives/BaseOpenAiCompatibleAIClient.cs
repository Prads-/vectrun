namespace vectrun.Clients.Primitives;

using System.Net.Http.Headers;
using System.Text.Json;
using vectrun.Models.Clients;

internal abstract class BaseOpenAiCompatibleAIClient : BaseAIClient
{
    protected BaseOpenAiCompatibleAIClient(
        HttpClient httpClient,
        AIClientOptions options)
        : base(httpClient, options) { }

    public override async Task<AIChatResponse> SendAsync(
        AIChatRequest request,
        CancellationToken token)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Options.Model,
            ["messages"] = request.Messages.Select(m => new
            {
                role = m.Role,
                content = m.Content,
                name = m.Name,
                tool_call_id = m.ToolCallId,
                tool_calls = m.ToolCalls?.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments }
                })
            }),
            ["tools"] = request.Tools?.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = t.Parameters
                }
            })
        };

        if (request.ResponseFormat is not null)
        {
            if (request.ResponseFormat.Type == "json" && request.ResponseFormat.Schema is not null)
            {
                payload["response_format"] = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "response",
                        schema = request.ResponseFormat.Schema
                    }
                };
            }
            else if (request.ResponseFormat.Type == "text")
            {
                payload["response_format"] = new { type = "text" };
            }
        }

        var res = await PostJsonAsync("/v1/chat/completions", payload, token);

        var msg = res.GetProperty("choices")[0].GetProperty("message");

        var toolCalls = msg.TryGetProperty("tool_calls", out var tcEl)
            ? tcEl.EnumerateArray().Select(tc => new AIToolCall
            {
                Id = tc.GetProperty("id").GetString()!,
                Name = tc.GetProperty("function").GetProperty("name").GetString()!,
                Arguments = tc.GetProperty("function").GetProperty("arguments").GetString()!
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

    protected override void ApplyHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);
    }
}