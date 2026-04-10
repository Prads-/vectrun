namespace vectrun.Pipeline;

using Lua;
using vectrun.Models;
using vectrun.Pipeline.Models;

internal class LogicNode : BaseNode<LogicNodeData>
{
    public LogicNode(string id, LogicNodeData data)
        : base(id, "logic", data)
    {
    }

    protected override async Task<NodeExecutionResult> ExecuteCoreAsync(
        NodeExecutionContext context,
        CancellationToken token)
    {
        string? output;

        if (Data.LogicType == "process")
            output = await ExecuteProcess(Data.ProcessPath, context.Input, token);
        else if (Data.LogicType == "script")
            output = await ExecuteScript(context.Input, token);
        else
            throw new InvalidOperationException($"Unsupported logic type: {Data.LogicType}");

        return new NodeExecutionResult
        {
            Output = output,
            NextNodeIds = Data.NextNodeIds
        };
    }

    private static async Task<string?> ExecuteProcess(
        string? processPath,
        string? input,
        CancellationToken token)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = processPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        if (!string.IsNullOrEmpty(input))
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(token));

        var output = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(stderr))
        {
            throw new InvalidOperationException(
                $"Process exited with code {process.ExitCode}. Stderr: {stderr}");
        }

        return output;
    }

    private async Task<string?> ExecuteScript(
        string? input,
        CancellationToken token)
    {
        var state = LuaState.Create();
        state.Environment["input"] = input ?? "";

        var results = await state.DoStringAsync(
            Data.Script ?? "",
            cancellationToken: token);

        if (results.Length == 0 || results[0].Type == LuaValueType.Nil)
            return null;

        return results[0].Read<string>();
    }
}
