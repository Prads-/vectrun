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
        CancellationToken token)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start tool");

        await process.StandardInput.WriteAsync(arguments);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(token);

        return output;
    }
}