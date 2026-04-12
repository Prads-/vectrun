namespace vectrun.Pipeline;

using vectrun.Models.Clients;
using vectrun.Pipeline.Contracts;

internal class ToolDefinition : IToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required object Parameters { get; init; }
    public required string Path { get; init; }

    public AITool ToAITool()
    {
        return new AITool
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters
        };
    }

    public async Task<string> ExecuteAsync(
        string arguments,
        Action<string>? onLog,
        CancellationToken token)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start tool");

        await process.StandardInput.WriteAsync(arguments);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
                onLog?.Invoke(line);
        });

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(token));

        return await stdoutTask;
    }
}