namespace vectrun.tests.Clients;

using System.Text.Json;
using vectrun.Clients.Primitives;
using vectrun.Models.Clients;
using vectrun.tests.Helpers;

// Concrete subclass for testing the abstract base
internal class ConcreteOpenAiClient(HttpClient http, AIClientOptions opts)
    : BaseOpenAiCompatibleAIClient(http, opts);

public class BaseOpenAiCompatibleAIClientTests
{
    private static (ConcreteOpenAiClient client, MockHttpMessageHandler handler) CreateClient(
        string responseJson, string? apiKey = null)
    {
        var handler = new MockHttpMessageHandler(responseJson);
        var opts = new AIClientOptions { Endpoint = "http://localhost", Model = "gpt-4", ApiKey = apiKey };
        return (new ConcreteOpenAiClient(new HttpClient(handler), opts), handler);
    }

    [Fact]
    public async Task SendAsync_SimpleResponse_ReturnsParsedMessage()
    {
        var (client, _) = CreateClient("""
            {
              "choices": [{ "message": { "role": "assistant", "content": "Hello" } }]
            }
            """);

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "Hi" }] },
            default);

        Assert.Equal("assistant", response.Message.Role);
        Assert.Equal("Hello", response.Message.Content);
        Assert.Null(response.ToolCalls);
    }

    [Fact]
    public async Task SendAsync_WithToolCalls_ParsesToolCalls()
    {
        var (client, _) = CreateClient("""
            {
              "choices": [{
                "message": {
                  "role": "assistant",
                  "content": "",
                  "tool_calls": [{
                    "id": "call_1",
                    "function": { "name": "mytool", "arguments": "{\"x\":1}" }
                  }]
                }
              }]
            }
            """);

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "go" }] },
            default);

        Assert.NotNull(response.ToolCalls);
        Assert.Single(response.ToolCalls);
        Assert.Equal("call_1", response.ToolCalls[0].Id);
        Assert.Equal("mytool", response.ToolCalls[0].Name);
        Assert.Equal("{\"x\":1}", response.ToolCalls[0].Arguments);
    }

    [Fact]
    public async Task SendAsync_WithApiKey_SetsAuthorizationHeader()
    {
        var (client, handler) = CreateClient(
            """{"choices":[{"message":{"role":"assistant","content":""}}]}""",
            apiKey: "my-secret-key");

        await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "x" }] },
            default);

        Assert.Equal("Bearer my-secret-key",
            handler.LastRequest!.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task SendAsync_WithoutApiKey_NoAuthorizationHeader()
    {
        var (client, handler) = CreateClient(
            """{"choices":[{"message":{"role":"assistant","content":""}}]}""",
            apiKey: null);

        await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "x" }] },
            default);

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_JsonResponseFormat_SetsJsonSchemaInPayload()
    {
        var (client, handler) = CreateClient(
            """{"choices":[{"message":{"role":"assistant","content":""}}]}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages = [new AIMessage { Role = "user", Content = "x" }],
            ResponseFormat = new AIResponseFormat { Type = "json", Schema = new { type = "object" } }
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);

        var format = body.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendAsync_TextResponseFormat_SetsTextInPayload()
    {
        var (client, handler) = CreateClient(
            """{"choices":[{"message":{"role":"assistant","content":""}}]}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages = [new AIMessage { Role = "user", Content = "x" }],
            ResponseFormat = new AIResponseFormat { Type = "text" }
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);

        var format = body.GetProperty("response_format");
        Assert.Equal("text", format.GetProperty("type").GetString());
    }
}
