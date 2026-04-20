namespace vectrun.tests.Pipeline;

using System.Text.Json;
using System.Threading.Channels;
using NSubstitute;
using vectrun.Models;
using vectrun.Pipeline;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;

public class LogicNodeTests
{
    private static LogicNode ScriptNode(
        string script,
        List<string>? nextNodeIds = null,
        RetryPolicy? retry = null,
        IReadOnlyDictionary<string, IToolDefinition>? tools = null) =>
        new("1", new LogicNodeData { LogicType = "script", Script = script, NextNodeIds = nextNodeIds ?? [], Retry = retry }, tools);

    private static IToolDefinition StubTool(string name, string stdout = "")
    {
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns(name);
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns(stdout);
        return tool;
    }

    [Fact]
    public async Task ExecuteAsync_Script_ReturnsLuaResult()
    {
        var result = await ScriptNode("return 'hello'")
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal("hello", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Script_InputAvailableAsGlobal()
    {
        var result = await ScriptNode("return input")
            .ExecuteAsync(new NodeExecutionContext { Input = "world" }, default);

        Assert.Equal("world", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Script_CanManipulateInput()
    {
        var result = await ScriptNode("return 'hello ' .. input")
            .ExecuteAsync(new NodeExecutionContext { Input = "world" }, default);

        Assert.Equal("hello world", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Script_NilReturnYieldsNull()
    {
        var result = await ScriptNode("-- no return")
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Null(result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Script_ReturnsNextNodeIds()
    {
        var result = await ScriptNode("return 'x'", ["a", "b"])
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal(["a", "b"], result.NextNodeIds);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidLogicType_Throws()
    {
        var node = new LogicNode("1", new LogicNodeData { LogicType = "unknown" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            node.ExecuteAsync(new NodeExecutionContext { Input = null }, default));
    }

    // ── Name property ─────────────────────────────────────────────────────────

    [Fact]
    public void Name_ReturnsDataName()
    {
        var node = new LogicNode("1", new LogicNodeData { Name = "Transform", LogicType = "script", Script = "return 'x'" });
        Assert.Equal("Transform", node.Name);
    }

    [Fact]
    public void Name_WhenNotSet_ReturnsNull()
    {
        var node = new LogicNode("1", new LogicNodeData { LogicType = "script", Script = "return 'x'" });
        Assert.Null(node.Name);
    }

    // ── Retry ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Script_WithRetry_AllRetriesExhausted_Throws()
    {
        var node = ScriptNode(
            "local a = nil; return a.x",
            retry: new RetryPolicy { RetryCount = 2, RetryDelayMs = 0, DelayType = "linear" });

        var threw = false;
        try { await node.ExecuteAsync(new NodeExecutionContext { Input = null }, default); }
        catch { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    public async Task ExecuteAsync_Script_WithRetry_EmitsRetryAndFailedLogEntries()
    {
        var node = ScriptNode(
            "local a = nil; return a.x",
            retry: new RetryPolicy { RetryCount = 2, RetryDelayMs = 0, DelayType = "linear" });

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();

        try { await node.ExecuteAsync(new NodeExecutionContext { Input = null, Log = channel.Writer }, default); }
        catch { /* expected */ }

        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.Equal(2, entries.Count(e => e.Event == "retry"));
        Assert.Single(entries, e => e.Event == "failed");
    }

    [Fact]
    public async Task ExecuteAsync_Script_NoRetryConfigured_Throws_EmitsNoRetryOrFailedEvents()
    {
        var node = ScriptNode("local a = nil; return a.x");

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();

        try { await node.ExecuteAsync(new NodeExecutionContext { Input = null, Log = channel.Writer }, default); }
        catch { /* expected */ }

        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        Assert.DoesNotContain(entries, e => e.Event is "retry" or "failed");
    }

    // ── ResolveInput / {PREVIOUS_AGENT_OUTPUT} template ───────────────────────────────────────

    [Fact]
    public void ResolveInput_NullProcessInput_ReturnsPreviousOutput()
    {
        var result = LogicNode.ResolveInput(null, "previous output");
        Assert.Equal("previous output", result);
    }

    [Fact]
    public void ResolveInput_NoTemplate_ReturnsProcessInputAsIs()
    {
        var result = LogicNode.ResolveInput("{\"operation\":\"delete\"}", "previous output");
        Assert.Equal("{\"operation\":\"delete\"}", result);
    }

    [Fact]
    public void ResolveInput_InputTemplate_SubstitutesPlainText()
    {
        var result = LogicNode.ResolveInput("{\"value\":\"{PREVIOUS_AGENT_OUTPUT}\"}", "hello world");
        Assert.Equal("{\"value\":\"hello world\"}", result);
    }

    [Fact]
    public void ResolveInput_InputTemplate_EscapesQuotesInPreviousOutput()
    {
        // System.Text.Json encodes " as \u0022 — both are valid JSON
        var result = LogicNode.ResolveInput("{\"value\":\"{PREVIOUS_AGENT_OUTPUT}\"}", "say \"hello\"");
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result!);
        Assert.Equal("say \"hello\"", parsed.GetProperty("value").GetString());
    }

    [Fact]
    public void ResolveInput_InputTemplate_EscapesBackslashesInPreviousOutput()
    {
        var result = LogicNode.ResolveInput("{\"value\":\"{PREVIOUS_AGENT_OUTPUT}\"}", @"C:\path\file");
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result!);
        Assert.Equal(@"C:\path\file", parsed.GetProperty("value").GetString());
    }

    [Fact]
    public void ResolveInput_InputTemplate_EscapesNewlinesInPreviousOutput()
    {
        var result = LogicNode.ResolveInput("{\"value\":\"{PREVIOUS_AGENT_OUTPUT}\"}", "line1\nline2");
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result!);
        Assert.Equal("line1\nline2", parsed.GetProperty("value").GetString());
    }

    [Fact]
    public void ResolveInput_InputTemplate_NullPreviousOutput_SubstitutesEmptyString()
    {
        var result = LogicNode.ResolveInput("{\"value\":\"{PREVIOUS_AGENT_OUTPUT}\"}", null);
        Assert.Equal("{\"value\":\"\"}", result);
    }

    [Fact]
    public void ResolveInput_InputTemplate_ProducesValidJson()
    {
        // Verify the substitution produces valid JSON even with complex input
        var input = "summary with \"quotes\", backslash \\ and\nnewline";
        var processInput = "{\"operation\":\"append\",\"namespace\":\"ns\",\"key\":\"k\",\"value\":\"{PREVIOUS_AGENT_OUTPUT}\"}";
        var result = LogicNode.ResolveInput(processInput, input);

        // Should parse without throwing
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result!);
        Assert.Equal(input, parsed.GetProperty("value").GetString());
    }

    // ── Lua tool exposure ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Script_ToolExposedAsLuaGlobal()
    {
        var tool = StubTool("kv_store", "ok");
        var result = await ScriptNode(
                "return kv_store('stdin')",
                tools: new Dictionary<string, IToolDefinition> { ["kv_store"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal("ok", result.Output);
        await tool.Received(1).ExecuteAsync("stdin", Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Script_StringArg_PassedThroughVerbatim()
    {
        string? capturedStdin = null;
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Do<string>(s => capturedStdin = s), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "t('raw payload')",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal("raw payload", capturedStdin);
    }

    [Fact]
    public async Task ExecuteAsync_Script_TableArg_ConvertedToJsonObject()
    {
        string? capturedStdin = null;
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Do<string>(s => capturedStdin = s), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "t({operation='write', key='foo', value='bar'})",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.NotNull(capturedStdin);
        var parsed = JsonSerializer.Deserialize<JsonElement>(capturedStdin!);
        Assert.Equal("write", parsed.GetProperty("operation").GetString());
        Assert.Equal("foo", parsed.GetProperty("key").GetString());
        Assert.Equal("bar", parsed.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_Script_TableArg_WithArrayPart_ConvertedToJsonArray()
    {
        string? capturedStdin = null;
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Do<string>(s => capturedStdin = s), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "t({'a', 'b', 'c'})",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal("[\"a\",\"b\",\"c\"]", capturedStdin);
    }

    [Fact]
    public async Task ExecuteAsync_Script_TableArg_MixedTypesSerialize()
    {
        string? capturedStdin = null;
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Do<string>(s => capturedStdin = s), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "t({flag=true, count=3, label='x', missing=nil})",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.NotNull(capturedStdin);
        var parsed = JsonSerializer.Deserialize<JsonElement>(capturedStdin!);
        Assert.True(parsed.GetProperty("flag").GetBoolean());
        Assert.Equal(3, parsed.GetProperty("count").GetDouble());
        Assert.Equal("x", parsed.GetProperty("label").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_Script_TableArg_NestedTableSerialize()
    {
        string? capturedStdin = null;
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Do<string>(s => capturedStdin = s), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "t({outer={inner='value', list={1,2,3}}})",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.NotNull(capturedStdin);
        var parsed = JsonSerializer.Deserialize<JsonElement>(capturedStdin!);
        var outer = parsed.GetProperty("outer");
        Assert.Equal("value", outer.GetProperty("inner").GetString());
        var list = outer.GetProperty("list");
        Assert.Equal(3, list.GetArrayLength());
        Assert.Equal(1, list[0].GetDouble());
        Assert.Equal(2, list[1].GetDouble());
        Assert.Equal(3, list[2].GetDouble());
    }

    [Fact]
    public async Task ExecuteAsync_Script_TableArg_EscapesSpecialChars()
    {
        string? capturedStdin = null;
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Do<string>(s => capturedStdin = s), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "t({value='quote:\" backslash:\\\\ newline:\\n'})",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.NotNull(capturedStdin);
        var parsed = JsonSerializer.Deserialize<JsonElement>(capturedStdin!);
        Assert.Equal("quote:\" backslash:\\ newline:\n", parsed.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_Script_ToolReturnsStdoutAsLuaString()
    {
        var tool = StubTool("t", "the-result");
        var result = await ScriptNode(
                "return t('x') .. '!'",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal("the-result!", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Script_ToolCalled_WithNoArgs_Throws()
    {
        var tool = StubTool("t");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            ScriptNode(
                    "t()",
                    tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
                .ExecuteAsync(new NodeExecutionContext { Input = null }, default));

        // Lua wraps the tool's InvalidOperationException in a LuaRuntimeException;
        // either way the underlying cause must identify the offending tool.
        Assert.Contains("t", ex.Message);
        Assert.Contains("expected 1 argument", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Script_ToolCalled_WithInvalidArgType_Throws()
    {
        var tool = StubTool("t");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            ScriptNode(
                    "t(42)",
                    tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
                .ExecuteAsync(new NodeExecutionContext { Input = null }, default));

        Assert.Contains("must be a string or table", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Script_MultipleTools_AllExposed()
    {
        var a = StubTool("tool_a", "A");
        var b = StubTool("tool_b", "B");
        var result = await ScriptNode(
                "return tool_a('x') .. tool_b('y')",
                tools: new Dictionary<string, IToolDefinition>
                {
                    ["tool_a"] = a,
                    ["tool_b"] = b,
                })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal("AB", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Script_IpairsIteratesAndCallsToolPerElement()
    {
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "for _, k in ipairs({'a','b','c'}) do t({key=k}) end",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        await tool.Received(3).ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Script_NullToolsDict_ScriptStillRuns()
    {
        // Passing null tools is allowed (constructor default) — script should still execute.
        var result = await ScriptNode("return 'ok'", tools: null)
            .ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal("ok", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Script_ToolOnLog_FlowsToPipelineLogAsToolLogEntries()
    {
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var onLog = callInfo.Arg<Action<string>?>();
                onLog?.Invoke("line one");
                onLog?.Invoke("line two");
                return Task.FromResult("ok");
            });

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();
        await ScriptNode(
                "t('x')",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null, Log = channel.Writer }, default);
        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);

        var toolLogs = entries.Where(e => e.Event == "tool_log").Select(e => e.Message).ToList();
        Assert.Equal(["line one", "line two"], toolLogs);
        Assert.All(entries.Where(e => e.Event == "tool_log"), e => Assert.Equal("1", e.NodeId));
    }

    [Fact]
    public async Task ExecuteAsync_Script_NullLog_ToolOnLogIsNull()
    {
        Action<string>? capturedOnLog = null;
        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Do<Action<string>?>(cb => capturedOnLog = cb), Arg.Any<CancellationToken>())
            .Returns("ok");

        await ScriptNode(
                "t('x')",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null, Log = null }, default);

        Assert.Null(capturedOnLog);
    }

    [Fact]
    public async Task ExecuteAsync_Script_CancellationPropagatesToToolInFlight()
    {
        using var cts = new CancellationTokenSource();
        var toolEntered = new TaskCompletionSource();
        var toolCancelled = new TaskCompletionSource();

        var tool = Substitute.For<IToolDefinition>();
        tool.Name.Returns("t");
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                ct.Register(() => toolCancelled.TrySetResult());
                toolEntered.TrySetResult();
                // Hold the tool open until the outer token cancels.
                try { await Task.Delay(Timeout.Infinite, ct); } catch { }
                return "cancelled";
            });

        var execTask = ScriptNode(
                "t('x')",
                tools: new Dictionary<string, IToolDefinition> { ["t"] = tool })
            .ExecuteAsync(new NodeExecutionContext { Input = null }, cts.Token);

        await toolEntered.Task;
        cts.Cancel();
        await toolCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try { await execTask; } catch { /* expected when cancelled */ }
    }
}
