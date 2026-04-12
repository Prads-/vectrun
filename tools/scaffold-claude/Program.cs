using System.Diagnostics;
using System.Text.Json;

// Support two calling conventions:
//   CLI:      scaffold-claude <project-directory>   (requirements via stdin as plain text)
//   Pipeline: echo '{"projectDirectory":"...","requirements":"..."}' | scaffold-claude
string projectDir;
string requirements;

if (args.Length > 0)
{
    // CLI mode: project directory from args[0], requirements from stdin as plain text
    projectDir   = Path.GetFullPath(args[0]);
    requirements = await Console.In.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(requirements))
    {
        Console.Error.WriteLine("No requirements provided via stdin.");
        return 1;
    }
}
else
{
    // Pipeline mode: both fields from JSON stdin
    var stdin = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(stdin))
    {
        Console.Error.WriteLine("Usage: scaffold-claude <project-directory>  (requirements via stdin)");
        Console.Error.WriteLine("  OR:  echo '{\"projectDirectory\":\"...\",\"requirements\":\"...\"}' | scaffold-claude");
        return 1;
    }

    var json = JsonSerializer.Deserialize<JsonElement>(stdin);

    if (!json.TryGetProperty("projectDirectory", out var dirProp) || string.IsNullOrWhiteSpace(dirProp.GetString()))
    {
        Console.Error.WriteLine("JSON input must contain a non-empty 'projectDirectory' field.");
        return 1;
    }
    if (!json.TryGetProperty("requirements", out var reqProp) || string.IsNullOrWhiteSpace(reqProp.GetString()))
    {
        Console.Error.WriteLine("JSON input must contain a non-empty 'requirements' field.");
        return 1;
    }

    projectDir   = Path.GetFullPath(dirProp.GetString()!);
    requirements = reqProp.GetString()!;
}

var projectName = Path.GetFileName(projectDir);

// ── Scaffold ──────────────────────────────────────────────────────────────────

Directory.CreateDirectory(projectDir);

var claudeMd = BuildClaudeMd(projectName, requirements.Trim());
await File.WriteAllTextAsync(Path.Combine(projectDir, "CLAUDE.md"), claudeMd);

await Console.Error.WriteLineAsync($"Scaffolded project at: {projectDir}");

// ── Launch Claude Code ────────────────────────────────────────────────────────

const string prompt =
    "Read CLAUDE.md for the complete project requirements. " +
    "Then create a comprehensive TODO.md file in the project directory listing every task required to implement the project — " +
    "cover project setup, all features, all files to create, configuration, and anything else needed for a complete, runnable result. " +
    "Once the TODO.md is written, work through every item in it one by one, checking each off as you complete it. " +
    "Do not stop until every item is checked off. " +
    "When all items are done, print a short summary of what was built and where the entry point is.";

var psi = new ProcessStartInfo
{
    FileName               = "claude",
    WorkingDirectory       = projectDir,
    UseShellExecute        = false,
    RedirectStandardOutput = true,
    RedirectStandardError  = true,
};
psi.ArgumentList.Add("--dangerously-skip-permissions");
psi.ArgumentList.Add("--output-format");
psi.ArgumentList.Add("stream-json");
psi.ArgumentList.Add("-p");
psi.ArgumentList.Add(prompt);

await Console.Error.WriteLineAsync("Launching Claude Code...");

Process process;
try
{
    process = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start Claude Code process.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not launch Claude Code: {ex.Message}");
    Console.Error.WriteLine("Make sure 'claude' is installed and available in PATH.");
    Console.Error.WriteLine($"Project scaffolded at: {projectDir}");
    return 1;
}

using (process)
{
    // Drain stderr in background to prevent pipe deadlock
    var stderrTask = Task.Run(async () =>
    {
        string? line;
        while ((line = await process.StandardError.ReadLineAsync()) is not null)
            await Console.Error.WriteLineAsync($"[stderr] {line}");
    });

    // Read stream-json events from stdout and forward as human-readable lines to stderr.
    // Each line is a JSON object; we extract text content so the vectrun output panel
    // shows what Claude is doing in real-time rather than waiting until the end.
    string? line;
    while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
    {
        var readable = ParseStreamJsonLine(line);
        if (readable is not null)
            await Console.Error.WriteLineAsync(readable);
    }

    await stderrTask;
    await process.WaitForExitAsync();

    await Console.Error.WriteLineAsync($"Claude Code exited with code {process.ExitCode}.");

    // Only the final result goes to stdout — this becomes the tool result returned to the agent.
    Console.WriteLine($"Project directory: {projectDir}");

    return process.ExitCode;
}

// ── stream-json line parser ───────────────────────────────────────────────────

static string? ParseStreamJsonLine(string line)
{
    if (string.IsNullOrWhiteSpace(line)) return null;

    try
    {
        var doc = JsonSerializer.Deserialize<JsonElement>(line);

        if (!doc.TryGetProperty("type", out var typeProp)) return line;
        var type = typeProp.GetString();

        switch (type)
        {
            case "assistant":
            {
                // Extract text content blocks from the assistant message
                if (!doc.TryGetProperty("message", out var msg)) break;
                if (!msg.TryGetProperty("content", out var content)) break;

                var texts = new System.Text.StringBuilder();
                foreach (var block in content.EnumerateArray())
                {
                    if (!block.TryGetProperty("type", out var bt)) continue;
                    if (bt.GetString() == "text" && block.TryGetProperty("text", out var txt))
                        texts.AppendLine(txt.GetString());
                    else if (bt.GetString() == "tool_use" && block.TryGetProperty("name", out var toolName))
                        texts.AppendLine($"[tool] {toolName.GetString()}");
                }
                var result = texts.ToString().Trim();
                return result.Length > 0 ? result : null;
            }

            case "result":
            {
                if (doc.TryGetProperty("result", out var res))
                    return $"[done] {res.GetString()}";
                break;
            }

            case "system":
                // Suppress system/init noise
                return null;

            default:
                // Unknown event type — skip silently
                return null;
        }
    }
    catch
    {
        // Not valid JSON — write raw
        return line;
    }

    return null;
}

// ── CLAUDE.md builder ─────────────────────────────────────────────────────────

static string BuildClaudeMd(string projectName, string requirements) => $"""
    # {projectName}

    ## Requirements

    {requirements}

    ## Build Instructions

    - Implement every feature described in the requirements above
    - Create all project files, directories, and configuration needed to run the program
    - Choose technologies and frameworks appropriate for the requirements
    - Follow language-specific best practices and conventions
    - The project must be complete and functional — not a skeleton or placeholder
    - When implementation is complete, provide a brief summary of what was built,
      the tech stack chosen, and how to run the project
    """;
