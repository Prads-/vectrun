using System.Diagnostics;
using System.Text.Json;

// Support two calling conventions:
//   CLI:      scaffold-claude <project-directory>   (requirements via stdin as plain text)
//   Pipeline: echo '{"projectDirectory":"...","requirements":"..."}' | scaffold-claude
string projectDir;
string requirements;
string? model = null;

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
    // Pipeline mode: both required fields + optional 'model' from JSON stdin
    var stdin = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(stdin))
    {
        Console.Error.WriteLine("Usage: scaffold-claude <project-directory>  (requirements via stdin)");
        Console.Error.WriteLine("  OR:  echo '{\"projectDirectory\":\"...\",\"requirements\":\"...\",\"model\":\"claude-sonnet-4-6\"}' | scaffold-claude");
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

    if (json.TryGetProperty("model", out var modelProp))
    {
        var m = modelProp.GetString();
        if (!string.IsNullOrWhiteSpace(m)) model = m.Trim();
    }
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

// Spawn Claude in a NEW visible console window so the user can watch its work live.
// scaffold-claude itself runs inside vectrun's pipeline with stdin/stdout piped, so it
// has no attached terminal. We use cmd.exe with UseShellExecute=true, which triggers
// ShellExecuteEx and allocates a fresh console for the child process.
//
// The inner command is `claude <args> & pause`. The `& pause` keeps the window open
// after Claude exits so the user can read the final output before closing it. When
// the user presses a key, cmd exits, WaitForExit returns here, and the pipeline
// continues with Claude's exit code propagated back.
var safePrompt = EscapeForCmdDoubleQuoted(prompt);
var modelFlag  = string.IsNullOrWhiteSpace(model)
    ? ""
    : $"--model \\\"{EscapeForCmdDoubleQuoted(model!)}\\\" ";
var cmdArgs    = $"/C \"claude {modelFlag}--dangerously-skip-permissions -p \\\"{safePrompt}\\\" & pause\"";

await Console.Error.WriteLineAsync(
    string.IsNullOrWhiteSpace(model)
        ? "Model: (claude default)"
        : $"Model: {model}");

var psi = new ProcessStartInfo
{
    FileName         = "cmd.exe",
    Arguments        = cmdArgs,
    UseShellExecute  = true,
    WorkingDirectory = projectDir,
};

await Console.Error.WriteLineAsync("Launching Claude Code in a new terminal window...");

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
    await process.WaitForExitAsync();

    await Console.Error.WriteLineAsync($"Claude Code exited with code {process.ExitCode}.");

    // Only the final result goes to stdout — this becomes the tool result returned to the agent.
    Console.WriteLine($"Project directory: {projectDir}");

    return process.ExitCode;
}

// ── Cmd argument escaping ─────────────────────────────────────────────────────

// Prepares a string for inclusion inside a double-quoted segment of a cmd.exe command line
// that will be passed on to a child process whose arg parser follows the standard CRT rules:
//   backslash before a quote escapes the quote; a lone backslash is preserved.
// We therefore escape only double-quotes (→ \") and runs of backslashes that immediately
// precede a double-quote (each backslash → \\). Other characters pass through untouched,
// since cmd does not reinterpret them inside a /C "..." wrapper.
static string EscapeForCmdDoubleQuoted(string input)
{
    var sb = new System.Text.StringBuilder(input.Length + 16);
    var backslashes = 0;

    foreach (var c in input)
    {
        if (c == '\\')
        {
            backslashes++;
            continue;
        }

        if (c == '"')
        {
            // Double the pending backslashes (so they stay literal in the child), then escape the quote.
            sb.Append('\\', backslashes * 2);
            sb.Append('\\').Append('"');
        }
        else
        {
            sb.Append('\\', backslashes);
            sb.Append(c);
        }

        backslashes = 0;
    }

    sb.Append('\\', backslashes);
    return sb.ToString();
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
