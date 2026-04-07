using System.Text.Json;
using vectrun.Api;
using vectrun.Pipeline;
using vectrun.Services;

if (args.Length == 0)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSingleton<PipelineRunService>();
    builder.Services.AddSingleton<WorkspaceService>();
    builder.Services.AddSingleton<FilesystemService>();

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
    await PipelineRunner.RunAsync(pipeline, token: cts.Token);
}
