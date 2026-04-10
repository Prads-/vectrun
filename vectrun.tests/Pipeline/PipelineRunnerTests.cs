namespace vectrun.tests.Pipeline;

using System.Threading.Channels;
using NSubstitute;
using vectrun.Models;
using vectrun.Pipeline;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;

public class PipelineRunnerTests
{
    private static INode MockNode(
        string id,
        string? output,
        List<string> nextIds,
        string type = "agent",
        string? name = null)
    {
        var node = Substitute.For<INode>();
        node.Id.Returns(id);
        node.Type.Returns(type);
        node.Name.Returns(name);
        node.ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new NodeExecutionResult { Output = output, NextNodeIds = nextIds });
        return node;
    }

    private static BuiltPipeline Pipeline(string startId, params INode[] nodes) =>
        new() { StartNodeId = startId, Nodes = nodes.ToDictionary(n => n.Id) };

    private static async Task<List<PipelineLogEntry>> CollectLogs(
        BuiltPipeline pipeline,
        string? input = null)
    {
        var channel = Channel.CreateUnbounded<PipelineLogEntry>();
        await PipelineRunner.RunAsync(pipeline, input, channel.Writer);
        channel.Writer.Complete();

        var entries = new List<PipelineLogEntry>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            entries.Add(e);
        return entries;
    }

    [Fact]
    public async Task RunAsync_SingleNode_ExecutesAndStops()
    {
        var node = MockNode("a", "out", []);
        await PipelineRunner.RunAsync(Pipeline("a", node));

        await node.Received(1).ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ChainedNodes_PassesOutputAsInput()
    {
        NodeExecutionContext? capturedContext = null;
        var nodeB = Substitute.For<INode>();
        nodeB.Id.Returns("b");
        nodeB.ExecuteAsync(Arg.Do<NodeExecutionContext>(c => capturedContext = c), Arg.Any<CancellationToken>())
            .Returns(new NodeExecutionResult { Output = null, NextNodeIds = [] });

        var nodeA = MockNode("a", "hello", ["b"]);

        await PipelineRunner.RunAsync(Pipeline("a", nodeA, nodeB));

        Assert.Equal("hello", capturedContext!.Input);
    }

    [Fact]
    public async Task RunAsync_MultipleNextNodes_ExecutesBothBranches()
    {
        var nodeA = MockNode("a", "out", ["b", "c"]);
        var nodeB = MockNode("b", null, []);
        var nodeC = MockNode("c", null, []);

        await PipelineRunner.RunAsync(Pipeline("a", nodeA, nodeB, nodeC));

        await nodeB.Received(1).ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>());
        await nodeC.Received(1).ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MultipleNextNodes_AllReceiveSameOutput()
    {
        var nodeA = MockNode("a", "shared", ["b", "c"]);

        NodeExecutionContext? bContext = null;
        NodeExecutionContext? cContext = null;

        var nodeB = Substitute.For<INode>();
        nodeB.Id.Returns("b");
        nodeB.ExecuteAsync(Arg.Do<NodeExecutionContext>(c => bContext = c), Arg.Any<CancellationToken>())
            .Returns(new NodeExecutionResult { Output = null, NextNodeIds = [] });

        var nodeC = Substitute.For<INode>();
        nodeC.Id.Returns("c");
        nodeC.ExecuteAsync(Arg.Do<NodeExecutionContext>(c => cContext = c), Arg.Any<CancellationToken>())
            .Returns(new NodeExecutionResult { Output = null, NextNodeIds = [] });

        await PipelineRunner.RunAsync(Pipeline("a", nodeA, nodeB, nodeC));

        Assert.Equal("shared", bContext!.Input);
        Assert.Equal("shared", cContext!.Input);
    }

    [Fact]
    public async Task RunAsync_NullNextNodeIds_Stops()
    {
        var node = Substitute.For<INode>();
        node.Id.Returns("a");
        node.ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new NodeExecutionResult { Output = "out", NextNodeIds = null });

        await PipelineRunner.RunAsync(Pipeline("a", node));

        await node.Received(1).ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>());
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithLog_EmitsStartedAndOutputForSingleNode()
    {
        var node = MockNode("a", "result", [], type: "agent", name: "My Agent");
        var entries = await CollectLogs(Pipeline("a", node), input: "input-value");

        Assert.Equal(2, entries.Count);

        var started = entries[0];
        Assert.Equal("started", started.Event);
        Assert.Equal("a", started.NodeId);
        Assert.Equal("agent", started.NodeType);
        Assert.Equal("My Agent", started.NodeName);
        Assert.Equal("input-value", started.Message);

        var output = entries[1];
        Assert.Equal("output", output.Event);
        Assert.Equal("a", output.NodeId);
        Assert.Equal("result", output.Message);
    }

    [Fact]
    public async Task RunAsync_WithLog_StartedEvent_ContainsInputAsMessage()
    {
        var node = MockNode("a", null, []);
        var entries = await CollectLogs(Pipeline("a", node), input: "my-input");

        Assert.Equal("my-input", entries.First(e => e.Event == "started").Message);
    }

    [Fact]
    public async Task RunAsync_WithLog_OutputEvent_ContainsNodeOutputAsMessage()
    {
        var node = MockNode("a", "node-output", []);
        var entries = await CollectLogs(Pipeline("a", node));

        Assert.Equal("node-output", entries.First(e => e.Event == "output").Message);
    }

    [Fact]
    public async Task RunAsync_WithLog_ChainedNodes_EachNodeEmitsBothEvents()
    {
        var nodeA = MockNode("a", "from-a", ["b"], name: "A");
        var nodeB = MockNode("b", "from-b", [], name: "B");

        var entries = await CollectLogs(Pipeline("a", nodeA, nodeB));

        Assert.Equal(4, entries.Count);
        Assert.Single(entries, e => e.Event == "started" && e.NodeId == "a");
        Assert.Single(entries, e => e.Event == "output" && e.NodeId == "a");
        Assert.Single(entries, e => e.Event == "started" && e.NodeId == "b");
        Assert.Single(entries, e => e.Event == "output" && e.NodeId == "b");
    }

    [Fact]
    public async Task RunAsync_WithLog_StartedEventPrecedesOutputEvent_ForSameNode()
    {
        var node = MockNode("a", "out", []);
        var entries = await CollectLogs(Pipeline("a", node));

        var startedIdx = entries.FindIndex(e => e.Event == "started" && e.NodeId == "a");
        var outputIdx  = entries.FindIndex(e => e.Event == "output"  && e.NodeId == "a");

        Assert.True(startedIdx < outputIdx);
    }

    [Fact]
    public async Task RunAsync_WithLog_BranchingNodes_BothBranchesLog()
    {
        var nodeA = MockNode("a", "shared", ["b", "c"]);
        var nodeB = MockNode("b", null, []);
        var nodeC = MockNode("c", null, []);

        var entries = await CollectLogs(Pipeline("a", nodeA, nodeB, nodeC));

        // a: started + output; b: started + output; c: started + output
        Assert.Equal(6, entries.Count);
        Assert.Contains(entries, e => e.NodeId == "b" && e.Event == "started");
        Assert.Contains(entries, e => e.NodeId == "c" && e.Event == "started");
    }

    [Fact]
    public async Task RunAsync_WithLog_TimestampsAreReasonablyRecent()
    {
        var node = MockNode("a", null, []);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var entries = await CollectLogs(Pipeline("a", node));

        Assert.All(entries, e => Assert.True(e.Timestamp >= before));
    }

    // ── Branch failure ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NodeThrows_EmitsBranchFailedLogEntry()
    {
        var node = Substitute.For<INode>();
        node.Id.Returns("a");
        node.Type.Returns("agent");
        node.Name.Returns((string?)null);
        node.ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NodeExecutionResult>(new InvalidOperationException("boom")));

        var entries = await CollectLogs(Pipeline("a", node));

        Assert.Single(entries, e => e.Event == "branch_failed" && e.Message!.Contains("boom"));
    }

    [Fact]
    public async Task RunAsync_NodeThrows_DoesNotPropagateException()
    {
        var node = Substitute.For<INode>();
        node.Id.Returns("a");
        node.Type.Returns("agent");
        node.Name.Returns((string?)null);
        node.ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NodeExecutionResult>(new InvalidOperationException("fail")));

        await PipelineRunner.RunAsync(Pipeline("a", node));
    }

    [Fact]
    public async Task RunAsync_NodeThrows_DoesNotEmitOutputEvent()
    {
        var node = Substitute.For<INode>();
        node.Id.Returns("a");
        node.Type.Returns("agent");
        node.Name.Returns((string?)null);
        node.ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NodeExecutionResult>(new InvalidOperationException("fail")));

        var entries = await CollectLogs(Pipeline("a", node));

        Assert.DoesNotContain(entries, e => e.Event == "output");
    }

    [Fact]
    public async Task RunAsync_OneOfTwoBranchesThrows_OtherBranchStillExecutes()
    {
        var nodeA = MockNode("a", "out", ["b", "c"]);

        var nodeB = Substitute.For<INode>();
        nodeB.Id.Returns("b");
        nodeB.Type.Returns("agent");
        nodeB.Name.Returns((string?)null);
        nodeB.ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NodeExecutionResult>(new InvalidOperationException("b failed")));

        var nodeC = MockNode("c", null, []);

        await PipelineRunner.RunAsync(Pipeline("a", nodeA, nodeB, nodeC));

        await nodeC.Received(1).ExecuteAsync(Arg.Any<NodeExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NullLog_DoesNotThrow()
    {
        var node = MockNode("a", "out", []);
        // Passing null log — should complete without error
        await PipelineRunner.RunAsync(Pipeline("a", node), input: null, log: null);
    }

    [Fact]
    public async Task RunAsync_WithLog_ContextContainsLogWriter()
    {
        NodeExecutionContext? captured = null;
        var node = Substitute.For<INode>();
        node.Id.Returns("a");
        node.Type.Returns("agent");
        node.ExecuteAsync(
            Arg.Do<NodeExecutionContext>(c => captured = c),
            Arg.Any<CancellationToken>())
            .Returns(new NodeExecutionResult { Output = null, NextNodeIds = [] });

        var channel = Channel.CreateUnbounded<PipelineLogEntry>();
        await PipelineRunner.RunAsync(Pipeline("a", node), log: channel.Writer);

        Assert.NotNull(captured!.Log);
    }
}
