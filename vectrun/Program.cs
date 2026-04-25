using System.Text.Json;
using System.Threading.Channels;
using vectrun.Api;
using vectrun.Models;
using vectrun.Pipeline;
using vectrun.Services;

if (args.Length == 0)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSingleton<PipelineRunService>();
    builder.Services.AddSingleton<WorkspaceService>();
    builder.Services.AddSingleton<FilesystemService>();
    builder.Services.AddSingleton<RecentsService>();

    builder.Services.AddControllers(opts => opts.Filters.Add<ApiExceptionFilter>())
        .AddJsonOptions(opts =>
            opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
    
    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

    var app = builder.Build();

    app.UseCors();
    app.UseStaticFiles();
    app.MapControllers();
    app.MapFallbackToFile("index.html");

    app.Run();
}
else
{
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var pipeline = PipelineBuilder.Build(args[0]);
    var input = Console.IsInputRedirected ? await Console.In.ReadToEndAsync(cts.Token) : null;

    var channel = Channel.CreateUnbounded<PipelineLogEntry>();
    var logTask = Task.Run(async () =>
    {
        await foreach (var entry in channel.Reader.ReadAllAsync(cts.Token))
        {
            var trimmed = entry.Message is null ? "" : (entry.Message.Length > 200 ? entry.Message[..200] + "…" : entry.Message);
            await Console.Error.WriteLineAsync($"[{entry.NodeId}] {entry.Event}: {trimmed}");
        }
    }, cts.Token);

    await PipelineRunner.RunAsync(pipeline, input: input, log: channel.Writer, token: cts.Token);
    channel.Writer.Complete();
    await logTask;
}
