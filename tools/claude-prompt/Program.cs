using System.Diagnostics;
using System.Text.Json;

// Invokes `claude -p` non-interactively and returns the assistant's final response text.
//
// Pipeline mode (JSON stdin):
//   echo '{"prompt":"...","systemPrompt":"...","model":"claude-sonnet-4-6"}' | claude-prompt
//
// CLI mode (plain-text stdin):
//   echo "what is 2+2?" | claude-prompt

var stdin = await Console.In.ReadToEndAsync();
if (string.IsNullOrWhiteSpace(stdin))
{
    Console.Error.WriteLine("Usage: echo '{\"prompt\":\"...\",\"systemPrompt\":\"...\",\"model\":\"...\"}' | claude-prompt");
    Console.Error.WriteLine("  OR:  echo 'plain text prompt' | claude-prompt");
    return 1;
}

string prompt;
string? systemPrompt = null;
string? model = null;

var trimmed = stdin.TrimStart();
if (trimmed.StartsWith('{'))
{
    JsonElement json;
    try { json = JsonSerializer.Deserialize<JsonElement>(stdin); }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"Invalid JSON stdin: {ex.Message}");
        return 1;
    }

    if (!json.TryGetProperty("prompt", out var pEl) || string.IsNullOrWhiteSpace(pEl.GetString()))
    {
        Console.Error.WriteLine("JSON input must contain a non-empty 'prompt' field.");
        return 1;
    }
    prompt = pEl.GetString()!;

    if (json.TryGetProperty("systemPrompt", out var sEl))
    {
        var s = sEl.GetString();
        if (!string.IsNullOrWhiteSpace(s)) systemPrompt = s;
    }
    if (json.TryGetProperty("model", out var mEl))
    {
        var m = mEl.GetString();
        if (!string.IsNullOrWhiteSpace(m)) model = m.Trim();
    }
}
else
{
    prompt = stdin;
}

var psi = new ProcessStartInfo
{
    FileName                = "claude",
    RedirectStandardOutput  = true,
    RedirectStandardError   = true,
    UseShellExecute         = false,
};
psi.ArgumentList.Add("-p");
psi.ArgumentList.Add(prompt);
psi.ArgumentList.Add("--output-format");
psi.ArgumentList.Add("json");
psi.ArgumentList.Add("--dangerously-skip-permissions");
if (systemPrompt != null)
{
    psi.ArgumentList.Add("--append-system-prompt");
    psi.ArgumentList.Add(systemPrompt);
}
if (model != null)
{
    psi.ArgumentList.Add("--model");
    psi.ArgumentList.Add(model);
}

Process process;
try
{
    process = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start claude");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not launch claude: {ex.Message}");
    Console.Error.WriteLine("Make sure 'claude' is installed and on PATH.");
    return 1;
}

var stdoutTask = process.StandardOutput.ReadToEndAsync();
var stderrTask = process.StandardError.ReadToEndAsync();
await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

var claudeStdout = await stdoutTask;
var claudeStderr = await stderrTask;

if (process.ExitCode != 0)
{
    Console.Error.WriteLine($"claude exited with code {process.ExitCode}.");
    if (!string.IsNullOrWhiteSpace(claudeStderr)) Console.Error.WriteLine(claudeStderr.Trim());
    return process.ExitCode;
}

JsonElement outJson;
try { outJson = JsonSerializer.Deserialize<JsonElement>(claudeStdout); }
catch (JsonException ex)
{
    Console.Error.WriteLine($"Could not parse claude JSON output: {ex.Message}");
    Console.Error.WriteLine(claudeStdout);
    return 1;
}

if (outJson.TryGetProperty("is_error", out var errEl) && errEl.GetBoolean())
{
    Console.Error.WriteLine("claude returned an error response.");
    Console.Error.WriteLine(claudeStdout);
    return 1;
}

if (!outJson.TryGetProperty("result", out var resultEl))
{
    Console.Error.WriteLine("claude response missing 'result' field.");
    Console.Error.WriteLine(claudeStdout);
    return 1;
}

Console.Write(resultEl.GetString() ?? "");
return 0;
