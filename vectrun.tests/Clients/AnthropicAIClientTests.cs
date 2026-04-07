namespace vectrun.tests.Clients;

using System.Text.Json;
using vectrun.Clients;
using vectrun.Models.Clients;
using vectrun.tests.Helpers;

public class AnthropicAIClientTests
{
    private static (AnthropicAIClient client, MockHttpMessageHandler handler) CreateClient(string responseJson)
    {
        var handler = new MockHttpMessageHandler(responseJson);
        var opts = new AIClientOptions { Endpoint = "http://localhost", Model = "claude-3", ApiKey = "test-key" };
        return (new AnthropicAIClient(new HttpClient(handler), opts), handler);
    }

    [Fact]
    public async Task SendAsync_SimpleResponse_ExtractsText()
    {
        var (client, _) = CreateClient("""{"content":[{"type":"text","text":"I am Claude"}]}""");

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "hello" }] },
            default);

        Assert.Equal("assistant", response.Message.Role);
        Assert.Equal("I am Claude", response.Message.Content);
    }

    [Fact]
    public async Task SendAsync_SetsApiKeyHeader()
    {
        var (client, handler) = CreateClient("""{"content":[{"type":"text","text":""}]}""");

        await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "hi" }] },
            default);

        Assert.Equal("test-key", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task SendAsync_SetsAnthropicVersionHeader()
    {
        var (client, handler) = CreateClient("""{"content":[{"type":"text","text":""}]}""");

        await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "hi" }] },
            default);

        Assert.Equal("2023-06-01", handler.LastRequest!.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task SendAsync_WithTools_SendsToolsInPayload()
    {
        var (client, handler) = CreateClient("""{"content":[{"type":"text","text":""}]}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages = [new AIMessage { Role = "user", Content = "hi" }],
            Tools = [new AITool { Name = "mytool", Description = "desc", Parameters = new { type = "object" } }]
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        var tools = body.GetProperty("tools").EnumerateArray().ToList();
        Assert.Single(tools);
        Assert.Equal("mytool", tools[0].GetProperty("name").GetString());
        Assert.True(tools[0].TryGetProperty("input_schema", out _));
    }

    [Fact]
    public async Task SendAsync_ToolUseResponse_ParsesToolCalls()
    {
        var (client, _) = CreateClient("""
            {
              "content": [
                { "type": "tool_use", "id": "tu_1", "name": "mytool", "input": {"x": 1} }
              ]
            }
            """);

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "go" }] },
            default);

        Assert.NotNull(response.ToolCalls);
        Assert.Single(response.ToolCalls);
        Assert.Equal("tu_1", response.ToolCalls[0].Id);
        Assert.Equal("mytool", response.ToolCalls[0].Name);
    }

    [Fact]
    public async Task SendAsync_SystemMessage_ExtractedToSystemField()
    {
        var (client, handler) = CreateClient("""{"content":[{"type":"text","text":""}]}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages =
            [
                new AIMessage { Role = "system", Content = "You are helpful" },
                new AIMessage { Role = "user", Content = "hi" }
            ]
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.Equal("You are helpful", body.GetProperty("system").GetString());

        var messages = body.GetProperty("messages").EnumerateArray().ToList();
        Assert.Single(messages);
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task SendAsync_ToolResultMessages_GroupedIntoSingleUserMessage()
    {
        var (client, handler) = CreateClient("""{"content":[{"type":"text","text":"done"}]}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages =
            [
                new AIMessage { Role = "user", Content = "go" },
                new AIMessage
                {
                    Role = "assistant", Content = "",
                    ToolCalls = [
                        new AIToolCall { Id = "tc1", Name = "tool1", Arguments = "{}" },
                        new AIToolCall { Id = "tc2", Name = "tool2", Arguments = "{}" }
                    ]
                },
                new AIMessage { Role = "tool", ToolCallId = "tc1", Name = "tool1", Content = "r1" },
                new AIMessage { Role = "tool", ToolCallId = "tc2", Name = "tool2", Content = "r2" }
            ]
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        var messages = body.GetProperty("messages").EnumerateArray().ToList();

        // user, assistant (tool_use), user (grouped tool results)
        Assert.Equal(3, messages.Count);

        var toolResultMsg = messages[2];
        Assert.Equal("user", toolResultMsg.GetProperty("role").GetString());
        var content = toolResultMsg.GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, content.Count);
        Assert.Equal("tool_result", content[0].GetProperty("type").GetString());
        Assert.Equal("tool_result", content[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendAsync_JsonResponseFormat_SetsSchemaInSystemField()
    {
        var (client, handler) = CreateClient("""{"content":[{"type":"text","text":"{}"}]}""");

        await client.SendAsync(new AIChatRequest
        {
            Messages = [new AIMessage { Role = "user", Content = "hello" }],
            ResponseFormat = new AIResponseFormat { Type = "json", Schema = new { type = "object" } }
        }, default);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        var system = body.GetProperty("system").GetString()!;
        Assert.Contains("schema", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_NullTextContent_ReturnsEmptyString()
    {
        var (client, _) = CreateClient("""{"content":[{"type":"text","text":null}]}""");

        var response = await client.SendAsync(
            new AIChatRequest { Messages = [new AIMessage { Role = "user", Content = "hi" }] },
            default);

        Assert.Equal("", response.Message.Content);
    }
}
