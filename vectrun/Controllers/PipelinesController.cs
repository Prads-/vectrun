namespace vectrun.Controllers;

using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using vectrun.Models;
using vectrun.Models.Api;
using vectrun.Services;

[ApiController, Route("api")]
public class PipelinesController(
    PipelineRunService runService,
    WorkspaceService workspaceService) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [HttpPost("pipelines/stop")]
    public IActionResult StopPipeline()
    {
        runService.Stop();
        return Ok();
    }

    [HttpGet("pipeline")]
    public IActionResult GetPipeline([FromQuery] string directory) =>
        Content(workspaceService.GetPipelineJson(directory), "application/json");

    /// <summary>
    /// Runs the pipeline and streams log entries as Server-Sent Events.
    /// The response closes automatically when the run completes.
    /// </summary>
    [HttpPost("pipelines/run/stream")]
    public async Task RunStream([FromBody] RunPipelineRequest req, CancellationToken token)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var channel = Channel.CreateUnbounded<PipelineLogEntry>(
            new UnboundedChannelOptions { SingleWriter = false, AllowSynchronousContinuations = false });

        // Run pipeline in background, writing log entries to channel
        var pipelineTask = Task.Run(async () =>
        {
            try
            {
                await runService.RunAsync(req.Directory, req.Input, channel.Writer, token);
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryWrite(new PipelineLogEntry(
                    DateTimeOffset.UtcNow, "pipeline", "system", null, "error", "Run was cancelled."));
            }
            catch (Exception ex)
            {
                channel.Writer.TryWrite(new PipelineLogEntry(
                    DateTimeOffset.UtcNow, "pipeline", "system", null, "error", ex.Message));
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, CancellationToken.None); // don't cancel the background task with the HTTP token

        // Forward channel entries to SSE stream
        await foreach (var entry in channel.Reader.ReadAllAsync(token))
        {
            var json = JsonSerializer.Serialize(entry, SseJsonOptions);
            await Response.WriteAsync($"data: {json}\n\n", token);
            await Response.Body.FlushAsync(token);
        }

        await pipelineTask;
    }
}
