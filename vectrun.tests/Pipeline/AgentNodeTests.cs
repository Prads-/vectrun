namespace vectrun.tests.Pipeline;

using System.Threading.Channels;
using NSubstitute;
using vectrun.Clients.Contracts;
using vectrun.Models;
using vectrun.Models.Clients;
using vectrun.Pipeline;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;

public class AgentNodeTests
{
    private static AgentNode Node(
        IAIClient client,
        IEnumerable<IToolDefinition>? tools = null,
        IEnumerable<IToolDefinition>? builtInTools = null,
        string? name = null,
        RetryPolicy? retry = null) =>
        new("1", new AgentNodeData { AgentId = "agent1", Name = name, NextNodeIds = ["next"], Retry = retry }, client, tools ?? [], builtInTools);

    private static IAIClient ClientReturning(string content)
    {
        var client = Substitute.For<IAIClient>();
        
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = content } });
        
        return client;
    }

    [Fact]
    public async Task ExecuteAsync_NoToolCalls_ReturnsResponseContent()
    {
        var result = await Node(ClientReturning("the answer"))
            .ExecuteAsync(new NodeExecutionContext { Input = "question" }, default);

        Assert.Equal("the answer", result.Output);
        Assert.Equal(["next"], result.NextNodeIds);
    }

    [Fact]
    public async Task ExecuteAsync_WithInput_AddsUserMessage()
    {
        // Snapshot messages at call time — the list is mutated after each send
        List<AIMessage>? snapshot = null;
        
        var client = Substitute.For<IAIClient>();
        
        client.SendAsync(Arg.Do<AIChatRequest>(r => snapshot = r.Messages.ToList()), Arg.Any<CancellationToken>())
            .Returns(new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = "ok" } });

        await Node(client).ExecuteAsync(new NodeExecutionContext { Input = "hello" }, default);

        Assert.NotNull(snapshot);
        Assert.Single(snapshot);
        Assert.Equal("user", snapshot[0].Role);
        Assert.Equal("hello", snapshot[0].Content);
    }

    [Fact]
    public async Task ExecuteAsync_NullInput_SendsNoUserMessage()
    {
        List<AIMessage>? snapshot = null;
        
        var client = Substitute.For<IAIClient>();
        
        client.SendAsync(Arg.Do<AIChatRequest>(r => snapshot = r.Messages.ToList()), Arg.Any<CancellationToken>())
            .Returns(new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = "ok" } });

        await Node(client).ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Empty(snapshot!);
    }

    [Fact]
    public async Task ExecuteAsync_WithToolCalls_ExecutesToolAndContinues()
    {
        var tool = Substitute.For<IToolDefinition>();
        
        tool.Name.Returns("mytool");
        tool.ToAITool().Returns(new AITool { Name = "mytool", Description = "d", Parameters = new { } });
        tool.ExecuteAsync("{}", Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns("tool result");

        var callCount = 0;
        var client = Substitute.For<IAIClient>();

        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => callCount++ == 0
                ? new AIChatResponse
                {
                    Message = new AIMessage
                    {
                        Role = "assistant",
                        Content = "",
                        ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                    },
                    ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                }
                : new AIChatResponse
                {
                    Message = new AIMessage { Role = "assistant", Content = "final" }
                });

        var result = await Node(client, [tool])
            .ExecuteAsync(new NodeExecutionContext { Input = "hi" }, default);

        Assert.Equal("final", result.Output);
        await tool.Received(1).ExecuteAsync("{}", Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_BuiltInTools_AlwaysIncludedInRequest()
    {
        var builtIn = Substitute.For<IToolDefinition>();
        builtIn.Name.Returns("my_message_queue");
        builtIn.ToAITool().Returns(new AITool { Name = "my_message_queue", Description = "built-in", Parameters = new { } });

        List<AITool>? capturedTools = null;
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Do<AIChatRequest>(r => capturedTools = r.Tools), Arg.Any<CancellationToken>())
            .Returns(new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = "ok" } });

        await Node(client, builtInTools: [builtIn])
            .ExecuteAsync(new NodeExecutionContext { Input = "hi" }, default);

        Assert.NotNull(capturedTools);
        Assert.Contains(capturedTools, t => t.Name == "my_message_queue");
    }

    // ── Name property ─────────────────────────────────────────────────────────

    [Fact]
    public void Name_ReturnsDataName()
    {
        var client = ClientReturning("ok");
        var node = Node(client, name: "Classifier");
        Assert.Equal("Classifier", node.Name);
    }

    [Fact]
    public void Name_WhenNotSet_ReturnsNull()
    {
        var client = ClientReturning("ok");
        var node = Node(client);
        Assert.Null(node.Name);
    }

    // ── Tool-call logging ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithToolCall_EmitsToolCallAndToolResultLogEntries()
    {
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("mytool");
        tool.ToAITool().Returns(new AITool { Name = "mytool", Description = "d", Parameters = new { } });
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns("tool output");

        var callCount = 0;
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => callCount++ == 0
                ? new AIChatResponse
                {
                    Message = new AIMessage
                    {
                        Role = "assistant", Content = "",
                        ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                    },
                    ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                }
                : new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = "done" } });

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();
        var context = new NodeExecutionContext { Input = "hi", Log = channel.Writer };

        await Node(client, [tool]).ExecuteAsync(context, default);
        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.Contains(entries, e => e.Event == "tool_call" && e.Message!.Contains("mytool"));
        Assert.Contains(entries, e => e.Event == "tool_result" && e.Message == "tool output");
    }

    [Fact]
    public async Task ExecuteAsync_ToolCallLogEntries_ContainNodeId()
    {
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ToAITool().Returns(new AITool { Name = "t", Description = "d", Parameters = new { } });
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns("r");

        var callCount = 0;
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => callCount++ == 0
                ? new AIChatResponse
                {
                    Message = new AIMessage
                    {
                        Role = "assistant", Content = "",
                        ToolCalls = [new AIToolCall { Id = "tc1", Name = "t", Arguments = "{}" }]
                    },
                    ToolCalls = [new AIToolCall { Id = "tc1", Name = "t", Arguments = "{}" }]
                }
                : new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = "done" } });

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();
        await Node(client, [tool]).ExecuteAsync(new NodeExecutionContext { Log = channel.Writer }, default);
        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.All(entries, e => Assert.Equal("1", e.NodeId));
    }

    [Fact]
    public async Task ExecuteAsync_NoToolCalls_EmitsNoToolLogEntries()
    {
        var channel = Channel.CreateUnbounded<PipelineLogEntry>();
        await Node(ClientReturning("done"))
            .ExecuteAsync(new NodeExecutionContext { Input = "x", Log = channel.Writer }, default);
        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.DoesNotContain(entries, e => e.Event is "tool_call" or "tool_result");
    }

    // ── Retry ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithRetry_ClientThrowsOnceThenSucceeds_ReturnsResult()
    {
        var callCount = 0;
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (callCount++ == 0)
                    return Task.FromException<AIChatResponse>(new InvalidOperationException("transient"));
                return Task.FromResult(new AIChatResponse
                {
                    Message = new AIMessage { Role = "assistant", Content = "recovered" }
                });
            });

        var result = await Node(client, retry: new RetryPolicy { RetryCount = 1, RetryDelayMs = 0, DelayType = "linear" })
            .ExecuteAsync(new NodeExecutionContext { Input = "hi" }, default);

        Assert.Equal("recovered", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_WithRetry_AllRetriesExhausted_Throws()
    {
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<AIChatResponse>(new InvalidOperationException("always fails")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Node(client, retry: new RetryPolicy { RetryCount = 2, RetryDelayMs = 0, DelayType = "linear" })
                .ExecuteAsync(new NodeExecutionContext { Input = "hi" }, default));
    }

    [Fact]
    public async Task ExecuteAsync_WithRetry_EmitsRetryLogEntries()
    {
        var callCount = 0;
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (callCount++ < 2)
                    return Task.FromException<AIChatResponse>(new InvalidOperationException("fail"));
                return Task.FromResult(new AIChatResponse
                {
                    Message = new AIMessage { Role = "assistant", Content = "ok" }
                });
            });

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();
        await Node(client, retry: new RetryPolicy { RetryCount = 2, RetryDelayMs = 0, DelayType = "linear" })
            .ExecuteAsync(new NodeExecutionContext { Input = "hi", Log = channel.Writer }, default);
        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.Equal(2, entries.Count(e => e.Event == "retry"));
    }

    [Fact]
    public async Task ExecuteAsync_WithRetry_RetriesExhausted_EmitsFailedLogEntry()
    {
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<AIChatResponse>(new InvalidOperationException("boom")));

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Node(client, retry: new RetryPolicy { RetryCount = 1, RetryDelayMs = 0, DelayType = "linear" })
                .ExecuteAsync(new NodeExecutionContext { Input = "hi", Log = channel.Writer }, default));

        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.Single(entries, e => e.Event == "failed" && e.Message!.Contains("boom"));
    }

    [Fact]
    public async Task ExecuteAsync_NoRetryConfigured_ClientThrows_DoesNotEmitRetryOrFailedEvents()
    {
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<AIChatResponse>(new InvalidOperationException("fail")));

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Node(client)
                .ExecuteAsync(new NodeExecutionContext { Input = "hi", Log = channel.Writer }, default));

        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.DoesNotContain(entries, e => e.Event is "retry" or "failed");
    }

    [Fact]
    public async Task ExecuteAsync_NullLog_WithToolCall_DoesNotThrow()
    {
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("mytool");
        tool.ToAITool().Returns(new AITool { Name = "mytool", Description = "d", Parameters = new { } });
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns("r");

        var callCount = 0;
        var client = Substitute.For<IAIClient>();
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => callCount++ == 0
                ? new AIChatResponse
                {
                    Message = new AIMessage
                    {
                        Role = "assistant", Content = "",
                        ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                    },
                    ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                }
                : new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = "done" } });

        // Log is null — must not throw
        await Node(client, [tool]).ExecuteAsync(new NodeExecutionContext { Input = null, Log = null }, default);
    }

    [Fact]
    public async Task ExecuteAsync_ToolCallResult_AddedToMessageHistory()
    {
        var tool = Substitute.For<IToolDefinition>();
        
        tool.Name.Returns("mytool");
        tool.ToAITool().Returns(new AITool { Name = "mytool", Description = "d", Parameters = new { } });
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns("tool output");

        // Snapshot the messages on the second call, before the loop mutates again
        List<AIMessage>? secondCallSnapshot = null;
        var callCount = 0;
        var client = Substitute.For<IAIClient>();
        
        client.SendAsync(Arg.Any<AIChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 2)
                    secondCallSnapshot = callInfo.Arg<AIChatRequest>().Messages.ToList();

                return callCount == 1
                    ? new AIChatResponse
                    {
                        Message = new AIMessage
                        {
                            Role = "assistant",
                            Content = "",
                            ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                        },
                        ToolCalls = [new AIToolCall { Id = "tc1", Name = "mytool", Arguments = "{}" }]
                    }
                    : new AIChatResponse { Message = new AIMessage { Role = "assistant", Content = "done" } };
            });

        await Node(client, [tool]).ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.NotNull(secondCallSnapshot);
        
        var toolMsg = secondCallSnapshot.Last();
        
        Assert.Equal("tool", toolMsg.Role);
        Assert.Equal("tool output", toolMsg.Content);
    }
}
