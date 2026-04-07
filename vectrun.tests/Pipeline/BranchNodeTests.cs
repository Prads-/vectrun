namespace vectrun.tests.Pipeline;

using vectrun.Models;
using vectrun.Pipeline;
using vectrun.Pipeline.Models;

public class BranchNodeTests
{
    private static BranchNode Node(string expected, List<string> trueIds, List<string> falseIds) =>
        new("1", new BranchNodeData { ExpectedOutput = expected, TrueNodeIds = trueIds, FalseNodeIds = falseIds });

    [Fact]
    public async Task ExecuteAsync_MatchingInput_ReturnsTrueNodeIds()
    {
        var result = await Node("yes", ["a", "b"], ["c"])
            .ExecuteAsync(new NodeExecutionContext { Input = "yes" }, default);

        Assert.Equal(["a", "b"], result.NextNodeIds);
    }

    [Fact]
    public async Task ExecuteAsync_NonMatchingInput_ReturnsFalseNodeIds()
    {
        var result = await Node("yes", ["a"], ["b", "c"])
            .ExecuteAsync(new NodeExecutionContext { Input = "no" }, default);

        Assert.Equal(["b", "c"], result.NextNodeIds);
    }

    [Fact]
    public async Task ExecuteAsync_PassesInputAsOutput()
    {
        var result = await Node("yes", ["a"], ["b"])
            .ExecuteAsync(new NodeExecutionContext { Input = "test input" }, default);

        Assert.Equal("test input", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_IsCaseSensitive()
    {
        var result = await Node("Yes", ["a"], ["b"])
            .ExecuteAsync(new NodeExecutionContext { Input = "yes" }, default);

        Assert.Equal(["b"], result.NextNodeIds);
    }

    // ── Name property ─────────────────────────────────────────────────────────

    [Fact]
    public void Name_ReturnsDataName()
    {
        var node = new BranchNode("1", new BranchNodeData
        {
            Name = "Route decision",
            ExpectedOutput = "yes",
            TrueNodeIds = [],
            FalseNodeIds = []
        });
        Assert.Equal("Route decision", node.Name);
    }

    [Fact]
    public void Name_WhenNotSet_ReturnsNull()
    {
        var node = new BranchNode("1", new BranchNodeData { ExpectedOutput = "x", TrueNodeIds = [], FalseNodeIds = [] });
        Assert.Null(node.Name);
    }
}
