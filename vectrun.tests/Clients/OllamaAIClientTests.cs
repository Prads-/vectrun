namespace vectrun.tests.Clients;

using System.Text.Json;
using vectrun.Clients;
using vectrun.Models.Clients;
using vectrun.tests.Helpers;

public class OllamaAIClientTests
{
    private static (OllamaAIClient client, MockHttpMessageHandler handler) CreateClient(string responseJson)
    {
        var handler = new MockHttpMessageHandler(responseJson);
        var opts = new AIClientOptions { Endpoint = "http://localhost", Model = "llama2" };
        return (new OllamaAIClient(new HttpClient(handler), opts), handler);
    }

    [Fact]
    public async Task SendAsync_SimpleResponse_ExtractsMessageContent()
    {
        var (client, _) = CreateClient("""{"message":{"role":"assistant","content":"Hello from Ollama"}}""");

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "hi" }] },
            default);

        Assert.Equal("assistant", response.Message.Role);
        Assert.Equal("Hello from Ollama", response.Message.Content);
    }

    [Fact]
    public async Task SendAsync_JsonResponseFormat_SetsFormatInPayload()
    {
        var (client, handler) = CreateClient("""{"message":{"role":"assistant","content":"{}"}}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages = [new AIMessage { Role = "user", Content = "x" }],
            ResponseFormat = new AIResponseFormat { Type = "json", Schema = new { type = "object" } }
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.True(body.TryGetProperty("format", out _));
    }

    [Fact]
    public async Task SendAsync_WithTools_WrapsToolsCorrectly()
    {
        var (client, handler) = CreateClient("""{"message":{"role":"assistant","content":""}}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages = [new AIMessage { Role = "user", Content = "x" }],
            Tools = [new AITool { Name = "mytool", Description = "desc", Parameters = new { type = "object" } }]
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        var tool = body.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("mytool", tool.GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendAsync_ToolResultMessage_MappedCorrectly()
    {
        var (client, handler) = CreateClient("""{"message":{"role":"assistant","content":"done"}}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages =
            [
                new AIMessage { Role = "user", Content = "go" },
                new AIMessage { Role = "tool", ToolCallId = "tc1", Name = "mytool", Content = "result" }
            ]
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        var messages = body.GetProperty("messages").EnumerateArray().ToList();
        var toolMsg = messages[1];
        Assert.Equal("tool", toolMsg.GetProperty("role").GetString());
        Assert.Equal("result", toolMsg.GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendAsync_ToolCallResponse_ParsesToolCalls()
    {
        var (client, _) = CreateClient("""
            {
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                  { "id": "tc1", "function": { "name": "mytool", "arguments": {"x": 1} } }
                ]
              }
            }
            """);

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "go" }] },
            default);

        Assert.NotNull(response.ToolCalls);
        Assert.Single(response.ToolCalls);
        Assert.Equal("tc1", response.ToolCalls[0].Id);
        Assert.Equal("mytool", response.ToolCalls[0].Name);
    }

    [Fact]
    public async Task SendAsync_ToolCallWithNoId_GeneratesFallbackId()
    {
        var (client, _) = CreateClient("""
            {
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                  { "function": { "name": "mytool", "arguments": {} } }
                ]
              }
            }
            """);

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "go" }] },
            default);

        Assert.NotNull(response.ToolCalls);
        Assert.False(string.IsNullOrEmpty(response.ToolCalls[0].Id));
    }

    [Fact]
    public async Task SendAsync_NullContent_ReturnsEmptyString()
    {
        var (client, _) = CreateClient("""{"message":{"role":"assistant","content":null}}""");

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "hi" }] },
            default);

        Assert.Equal("", response.Message.Content);
    }

    [Fact]
    public async Task SendAsync_AssistantMessageWithToolCalls_SerializesArgumentsAsObject()
    {
        // Regression test: Ollama rejects tool call arguments sent as a quoted JSON string.
        // Arguments must be serialized as a JSON object in the outgoing payload.
        var (client, handler) = CreateClient("""{"message":{"role":"assistant","content":"done"}}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages =
            [
                new AIMessage { Role = "user", Content = "go" },
                new AIMessage
                {
                    Role = "assistant",
                    Content = "",
                    ToolCalls = [new AIToolCall { Id = "tc1", Name = "kv_store", Arguments = """{"key":"x","value":"y"}""" }]
                },
                new AIMessage { Role = "tool", ToolCallId = "tc1", Name = "kv_store", Content = "OK" }
            ]
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        var messages = body.GetProperty("messages").EnumerateArray().ToList();

        // The assistant message (index 1) must have tool_calls with arguments as an object, not a string
        var assistantMsg = messages[1];
        var toolCall = assistantMsg.GetProperty("tool_calls")[0];
        var arguments = toolCall.GetProperty("function").GetProperty("arguments");

        Assert.Equal(JsonValueKind.Object, arguments.ValueKind);
        Assert.Equal("x", arguments.GetProperty("key").GetString());
        Assert.Equal("y", arguments.GetProperty("value").GetString());
    }
}
