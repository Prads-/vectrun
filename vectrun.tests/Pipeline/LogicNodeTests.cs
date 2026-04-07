namespace vectrun.tests.Pipeline;

using vectrun.Models;
using vectrun.Pipeline;
using vectrun.Pipeline.Models;

public class LogicNodeTests
{
    private static LogicNode ScriptNode(string script, List<string>? nextNodeIds = null) =>
        new("1", new LogicNodeData { LogicType = "script", Script = script, NextNodeIds = nextNodeIds ?? [] });

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
}
