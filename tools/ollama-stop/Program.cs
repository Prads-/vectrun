using System.Diagnostics;
using System.Text.Json;

// Support both CLI usage (args[0]) and pipeline usage (JSON via stdin)
string model;
if (args.Length > 0)
{
    model = args[0];
}
else
{
    var stdin = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(stdin))
    {
        Console.Error.WriteLine("Usage: ollama-stop <model>  OR  echo '{\"model\":\"...\"}' | ollama-stop");
        return 1;
    }
    var json = JsonSerializer.Deserialize<JsonElement>(stdin);
    if (!json.TryGetProperty("model", out var modelProp))
    {
        Console.Error.WriteLine("JSON input must contain a 'model' field.");
        return 1;
    }
    model = modelProp.GetString() ?? string.Empty;
}

if (string.IsNullOrWhiteSpace(model))
{
    Console.Error.WriteLine("Model name must not be empty.");
    return 1;
}

var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "ollama",
        Arguments = $"stop {model}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    }
};

process.Start();
var stdout = await process.StandardOutput.ReadToEndAsync();
var stderr = await process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();

if (process.ExitCode != 0)
{
    Console.Error.WriteLine(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    return process.ExitCode;
}

Console.WriteLine(string.IsNullOrWhiteSpace(stdout) ? "OK" : stdout.Trim());
return 0;
