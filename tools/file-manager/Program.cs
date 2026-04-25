using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var stdin = await Console.In.ReadToEndAsync();

if (string.IsNullOrWhiteSpace(stdin))
{
    Console.Error.WriteLine("Usage: echo '<json>' | file-manager");
    Console.Error.WriteLine("Operations: create_directory, write_text, write_binary, read_text, list_directory, exists");
    return 1;
}

JsonElement json;
try
{
    json = JsonSerializer.Deserialize<JsonElement>(stdin);
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"Invalid JSON: {ex.Message}");
    return 1;
}

if (!json.TryGetProperty("operation", out var opProp) || string.IsNullOrWhiteSpace(opProp.GetString()))
{
    Console.Error.WriteLine("Missing required field: 'operation'");
    return 1;
}

if (!json.TryGetProperty("path", out var pathProp) || string.IsNullOrWhiteSpace(pathProp.GetString()))
{
    Console.Error.WriteLine("Missing required field: 'path'");
    return 1;
}

var operation = opProp.GetString()!.ToLowerInvariant();
var path      = Path.GetFullPath(pathProp.GetString()!);

return operation switch
{
    "create_directory" => CreateDirectory(path),
    "write_text"       => WriteText(path, json),
    "write_binary"     => WriteBinary(path, json),
    "read_text"        => ReadText(path),
    "list_directory"   => ListDirectory(path),
    "exists"           => Exists(path),
    _                  => UnknownOperation(operation)
};

// ── Operations ────────────────────────────────────────────────────────────────

static int CreateDirectory(string path)
{
    try
    {
        Directory.CreateDirectory(path);
        Console.WriteLine($"OK: {path}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to create directory '{path}': {ex.Message}");
        return 1;
    }
}

static int WriteText(string path, JsonElement json)
{
    if (!json.TryGetProperty("content", out var contentProp))
    {
        Console.Error.WriteLine("write_text requires a 'content' field.");
        return 1;
    }

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contentProp.GetString() ?? "", Encoding.UTF8);
        Console.WriteLine($"OK: {path}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to write '{path}': {ex.Message}");
        return 1;
    }
}

static int WriteBinary(string path, JsonElement json)
{
    if (!json.TryGetProperty("contentBase64", out var b64Prop))
    {
        Console.Error.WriteLine("write_binary requires a 'contentBase64' field.");
        return 1;
    }

    try
    {
        var bytes = Convert.FromBase64String(b64Prop.GetString() ?? "");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"OK: {path} ({bytes.Length} bytes)");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to write binary '{path}': {ex.Message}");
        return 1;
    }
}

static int ReadText(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 1;
    }

    try
    {
        Console.Write(File.ReadAllText(path, Encoding.UTF8));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to read '{path}': {ex.Message}");
        return 1;
    }
}

static int ListDirectory(string path)
{
    if (!Directory.Exists(path))
    {
        Console.Error.WriteLine($"Directory not found: {path}");
        return 1;
    }

    try
    {
        var entries = Directory.GetFileSystemEntries(path)
            .OrderBy(e => e)
            .Select(e => new 
            Entry(
                Path.GetFileName(e),
                Directory.Exists(e) ? "directory" : "file",
                Directory.Exists(e) ? null : new FileInfo(e).Length));

        var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        Console.WriteLine(JsonSerializer.Serialize(entries, options));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to list '{path}': {ex.Message}");
        return 1;
    }
}

// Exit code 0 either way — non-existence is a valid answer, not an error.
// stdout is "yes" or "no" so callers (e.g. Lua scripts) can compare directly.
static int Exists(string path)
{
    var exists = File.Exists(path) || Directory.Exists(path);
    Console.WriteLine(exists ? "yes" : "no");
    return 0;
}

static int UnknownOperation(string operation)
{
    Console.Error.WriteLine($"Unknown operation '{operation}'. Valid: create_directory, write_text, write_binary, read_text, list_directory, exists");
    return 1;
}

// ── Models ────────────────────────────────────────────────────────────────────

record Entry(string Name, string Type, long? Size);
