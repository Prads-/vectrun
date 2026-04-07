namespace vectrun.Pipeline.Contracts;

using vectrun.Models.Clients;

internal interface IToolDefinition
{
    string Name { get; }
    AITool ToAITool();
    Task<string> ExecuteAsync(string arguments, CancellationToken token);
}
