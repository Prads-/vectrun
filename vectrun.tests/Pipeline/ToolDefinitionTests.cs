namespace vectrun.tests.Pipeline;

using vectrun.Pipeline;

public class ToolDefinitionTests
{
    [Fact]
    public void ToAITool_MapsPropertiesCorrectly()
    {
        var parameters = new { type = "object" };
        var def = new ToolDefinition
        {
            Name = "my_tool",
            Description = "does stuff",
            Parameters = parameters,
            Path = "/tools/my_tool"
        };

        var tool = def.ToAITool();

        Assert.Equal("my_tool", tool.Name);
        Assert.Equal("does stuff", tool.Description);
        Assert.Equal(parameters, tool.Parameters);
    }
}
