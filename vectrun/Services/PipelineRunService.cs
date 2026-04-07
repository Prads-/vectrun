namespace vectrun.Services;

using System.Threading.Channels;
using vectrun.Models;
using vectrun.Models.Api;
using vectrun.Pipeline;

public class PipelineRunService
{
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public async Task RunAsync(
        string directory,
        string? input,
        ChannelWriter<PipelineLogEntry>? log,
        CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            throw new PipelineAlreadyRunningException();

        try
        {
            var pipeline = PipelineBuilder.Build(directory);
            await PipelineRunner.RunAsync(pipeline, input, log, token);
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }
}
