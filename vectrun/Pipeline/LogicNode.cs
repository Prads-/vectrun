namespace vectrun.Pipeline;

using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lua;
using Lua.Standard;
using vectrun.Models;
using vectrun.Pipeline.Contracts;
using vectrun.Pipeline.Models;

internal class LogicNode : BaseNode<LogicNodeData>
{
    private static readonly IReadOnlyDictionary<string, IToolDefinition> EmptyTools =
        new Dictionary<string, IToolDefinition>();

    private readonly IReadOnlyDictionary<string, IToolDefinition> _tools;

    public LogicNode(
        string id,
        LogicNodeData data,
        IReadOnlyDictionary<string, IToolDefinition>? tools = null)
        : base(id, "logic", data)
    {
        _tools = tools ?? EmptyTools;
    }

    protected override async Task<NodeExecutionResult> ExecuteCoreAsync(
        NodeExecutionContext context,
        CancellationToken token)
    {
        string? output;

        if (Data.LogicType == "process")
            output = await ExecuteProcess(Data.ProcessPath, ResolveInput(Data.ProcessInput, context.Input), token);
        else if (Data.LogicType == "script")
            output = await ExecuteScript(context.Input, context.Log, token);
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
        ChannelWriter<PipelineLogEntry>? log,
        CancellationToken token)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["input"] = input ?? "";

        foreach (var (name, tool) in _tools)
            state.Environment[name] = BuildToolFunction(name, tool, log, token);

        var results = await state.DoStringAsync(
            Data.Script ?? "",
            cancellationToken: token);

        if (results.Length == 0 || results[0].Type == LuaValueType.Nil)
            return null;

        return results[0].Read<string>();
    }

    // Each tool in tools.json becomes a Lua global function with the same name.
    // Call it with a table (JSON-encoded automatically) or a string (passed as-is).
    // The function returns the tool's stdout as a Lua string.
    private LuaFunction BuildToolFunction(
        string name,
        IToolDefinition tool,
        ChannelWriter<PipelineLogEntry>? log,
        CancellationToken outerToken)
    {
        Action<string>? onLog = log == null ? null : line =>
            log.TryWrite(new PipelineLogEntry(DateTimeOffset.UtcNow, Id, Type, Name, "tool_log", line));

        return new LuaFunction(name, async (ctx, ct) =>
        {
            if (ctx.ArgumentCount < 1)
                throw new InvalidOperationException($"{name}: expected 1 argument");

            var arg = ctx.GetArgument(0);
            var stdin = arg.Type switch
            {
                LuaValueType.String => arg.Read<string>(),
                LuaValueType.Table  => LuaTableToJson(arg.Read<LuaTable>()),
                _ => throw new InvalidOperationException(
                    $"{name}: argument must be a string or table (got {arg.Type})")
            };

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerToken, ct);
            var stdout = await tool.ExecuteAsync(stdin, onLog, linked.Token);

            ctx.Return(stdout);
            return 1;
        });
    }

    private static string LuaValueToJson(LuaValue v) => v.Type switch
    {
        LuaValueType.Nil => "null",
        LuaValueType.Boolean => v.Read<bool>() ? "true" : "false",
        LuaValueType.Number => JsonSerializer.Serialize(v.Read<double>()),
        LuaValueType.String => JsonSerializer.Serialize(v.Read<string>()),
        LuaValueType.Table => LuaTableToJson(v.Read<LuaTable>()),
        _ => throw new InvalidOperationException($"Cannot convert Lua type {v.Type} to JSON")
    };

    private static string LuaTableToJson(LuaTable t)
    {
        // Pure array part with no hash entries → JSON array.
        // Mixed tables (both array and hash entries) fall through to the object branch:
        // numeric keys are stringified, yielding e.g. {"1":"a","2":"b","extra":"x"}.
        // This is a corner case — tool JSON schemas should pick one shape.
        if (t.HashMapCount == 0 && t.ArrayLength > 0)
        {
            var arr = new StringBuilder("[");
            for (int i = 1; i <= t.ArrayLength; i++)
            {
                if (i > 1) arr.Append(',');
                arr.Append(LuaValueToJson(t[i]));
            }
            arr.Append(']');
            return arr.ToString();
        }

        var obj = new StringBuilder("{");
        var first = true;
        LuaValue key = LuaValue.Nil;
        while (t.TryGetNext(key, out var pair))
        {
            if (!first) obj.Append(',');
            first = false;

            var keyString = pair.Key.Type == LuaValueType.String
                ? pair.Key.Read<string>()
                : pair.Key.ToString();
            obj.Append(JsonSerializer.Serialize(keyString));
            obj.Append(':');
            obj.Append(LuaValueToJson(pair.Value));

            key = pair.Key;
        }
        obj.Append('}');
        return obj.ToString();
    }
}
