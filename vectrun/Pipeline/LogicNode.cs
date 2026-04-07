namespace vectrun.Pipeline;

using Lua;
using vectrun.Models;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;

internal class LogicNode : INode
{
    private readonly LogicNodeData _data;

    public LogicNode(string id, LogicNodeData data)
    {
        Id = id;
        _data = data;
    }

    public string Id { get; }
    public string Type => "logic";
    public string? Name => _data.Name;

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken token)
    {
        string? output;

        if (_data.LogicType == "process")
            output = await ExecuteProcess(_data.ProcessPath, context.Input, token);
        else if (_data.LogicType == "script")
            output = await ExecuteScript(context.Input, token);
        else
            throw new InvalidOperationException($"Unsupported logic type: {_data.LogicType}");

        return new NodeExecutionResult
        {
            Output = output,
            NextNodeIds = _data.NextNodeIds
        };
    }

    private static async Task<string?> ExecuteProcess(string? processPath, string? input, CancellationToken token)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = processPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        if (!string.IsNullOrEmpty(input))
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var output = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync(token);

        return output;
    }

    private async Task<string?> ExecuteScript(string? input, CancellationToken token)
    {
        var state = LuaState.Create();
        state.Environment["input"] = input ?? "";

        var results = await state.DoStringAsync(_data.Script ?? "", cancellationToken: token);

        if (results.Length == 0 || results[0].Type == LuaValueType.Nil)
            return null;

        return results[0].Read<string>();
    }
}
