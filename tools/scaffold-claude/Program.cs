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

Console.Error.WriteLine($"Scaffolded project at: {projectDir}");

// ── Launch Claude Code ────────────────────────────────────────────────────────

const string prompt =
    "Read CLAUDE.md for the complete project requirements. " +
    "Implement the full project: create all necessary files, directories, and configuration. " +
    "Choose appropriate technologies based on the requirements. " +
    "When done, print a short summary of what was built and where the entry point is.";

var psi = new ProcessStartInfo
{
    FileName               = "claude",
    WorkingDirectory       = projectDir,
    UseShellExecute        = false,
    RedirectStandardOutput = true,
    RedirectStandardError  = false,
};
psi.ArgumentList.Add("--dangerously-skip-permissions");
psi.ArgumentList.Add("-p");
psi.ArgumentList.Add(prompt);

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
    // Stream claude's output to our stdout so it flows through the vectrun pipeline.
    string? line;
    while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
        Console.WriteLine(line);

    await process.WaitForExitAsync();

    Console.WriteLine();
    Console.WriteLine($"Project directory: {projectDir}");

    return process.ExitCode;
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
