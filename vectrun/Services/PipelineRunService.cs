namespace vectrun.Services;

using System.Threading.Channels;
using vectrun.Models;
using vectrun.Models.Api;
using vectrun.Pipeline;

public class PipelineRunService
{
    private int _isRunning;
    private CancellationTokenSource? _cts;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>Cancels the currently running pipeline (all branches). No-op if nothing is running.</summary>
    public void Stop() => _cts?.Cancel();

    public async Task RunAsync(
        string directory,
        string? input,
        ChannelWriter<PipelineLogEntry>? log,
        CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            throw new PipelineAlreadyRunningException();

        // Create a CTS linked to the caller's token so that either Stop() or HTTP
        // request cancellation terminates the run.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _cts = cts;

        try
        {
            var pipeline = PipelineBuilder.Build(directory);
            await PipelineRunner.RunAsync(pipeline, input, log, cts.Token);
        }
        finally
        {
            _cts = null;
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }
}
