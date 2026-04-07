namespace vectrun.tests.Pipeline;

using vectrun.Models;
using vectrun.Pipeline;
using vectrun.Pipeline.Models;

public class WaitNodeTests
{
    [Fact]
    public async Task ExecuteAsync_PassesInputThrough()
    {
        var node = new WaitNode("1", new WaitNodeData { DurationMs = 0, NextNodeIds = ["next"] });
        var result = await node.ExecuteAsync(new NodeExecutionContext { Input = "hello" }, default);

        Assert.Equal("hello", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNextNodeIds()
    {
        var node = new WaitNode("1", new WaitNodeData { DurationMs = 0, NextNodeIds = ["a", "b"] });
        var result = await node.ExecuteAsync(new NodeExecutionContext { Input = null }, default);

        Assert.Equal(["a", "b"], result.NextNodeIds);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledToken_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var node = new WaitNode("1", new WaitNodeData { DurationMs = 60_000, NextNodeIds = [] });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            node.ExecuteAsync(new NodeExecutionContext { Input = null }, cts.Token));
    }

    // ── Name property ─────────────────────────────────────────────────────────

    [Fact]
    public void Name_ReturnsDataName()
    {
        var node = new WaitNode("1", new WaitNodeData { Name = "Pause", DurationMs = 0, NextNodeIds = [] });
        Assert.Equal("Pause", node.Name);
    }

    [Fact]
    public void Name_WhenNotSet_ReturnsNull()
    {
        var node = new WaitNode("1", new WaitNodeData { DurationMs = 0, NextNodeIds = [] });
        Assert.Null(node.Name);
    }
}
