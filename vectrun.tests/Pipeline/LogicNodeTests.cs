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
}
