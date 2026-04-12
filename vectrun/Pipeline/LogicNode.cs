namespace vectrun.Pipeline;

using System.Text.Json;
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
            output = await ExecuteProcess(Data.ProcessPath, ResolveInput(Data.ProcessInput, context.Input), token);
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

    // Resolves the stdin payload for the process.
    // If processInput contains {PREVIOUS_AGENT_OUTPUT}, substitutes the previous node's output
    // (JSON-escaped so the result is valid JSON when embedded inside a JSON string field).
    // If processInput is set but has no {PREVIOUS_AGENT_OUTPUT}, uses it as-is.
    // If processInput is null, falls back to the raw previous node output.
    internal static string? ResolveInput(string? processInput, string? previousOutput)
    {
        if (processInput is null)
            return previousOutput;

        if (!processInput.Contains("{PREVIOUS_AGENT_OUTPUT}"))
            return processInput;

        // JSON-serialize the input to get a properly escaped string value, then strip the outer quotes
        // so it can be safely embedded inside an existing JSON string field.
        var escaped = JsonSerializer.Serialize(previousOutput ?? "");
        var inner = escaped[1..^1]; // strip surrounding quotes
        return processInput.Replace("{PREVIOUS_AGENT_OUTPUT}", inner);
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

        if (process.ExitCode != 0)
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
