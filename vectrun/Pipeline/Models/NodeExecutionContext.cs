namespace vectrun.Pipeline.Models;

using System.Threading.Channels;
using vectrun.Models;

internal class NodeExecutionContext
{
    public string? Input { get; init; }
    public ChannelWriter<PipelineLogEntry>? Log { get; init; }
}
