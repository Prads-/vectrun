namespace vectrun.tests.Pipeline;

using System.Threading.Channels;
using vectrun.Models;
using vectrun.Pipeline;
using vectrun.Pipeline.Models;

public class LogicNodeTests
{
    private static LogicNode ScriptNode(string script, List<string>? nextNodeIds = null, RetryPolicy? retry = null) =>
        new("1", new LogicNodeData { LogicType = "script", Script = script, NextNodeIds = nextNodeIds ?? [], Retry = retry });

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
}
