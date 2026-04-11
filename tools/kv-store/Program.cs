using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Support both CLI usage (args) and pipeline usage (JSON via stdin)
string operation, ns, key;
string? cliValue = null;
string separator = "\n\n---\n\n";

if (args.Length >= 3)
{
    operation = args[0].ToLowerInvariant();
    ns        = args[1];
    key       = args[2];
    cliValue  = args.Length >= 4 ? args[3] : null;
}
else
{
    var stdin = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(stdin))
    {
        Console.Error.WriteLine("Usage: kv-store <read|write|update|delete|append> <namespace> <key> [value]");
        Console.Error.WriteLine("  OR:  echo '{\"operation\":\"write\",\"namespace\":\"ns\",\"key\":\"k\",\"value\":\"v\"}' | kv-store");
        return 1;
    }
    var json = JsonSerializer.Deserialize<JsonElement>(stdin);
    operation = json.TryGetProperty("operation", out var op) ? op.GetString()?.ToLowerInvariant() ?? "" : "";
    ns        = json.TryGetProperty("namespace", out var nsProp) ? nsProp.GetString() ?? "" : "";
    key       = json.TryGetProperty("key", out var keyProp) ? keyProp.GetString() ?? "" : "";
    cliValue  = json.TryGetProperty("value", out var valProp) ? valProp.GetString() : null;
    separator = json.TryGetProperty("separator", out var sepProp) ? sepProp.GetString() ?? separator : separator;
}

if (string.IsNullOrWhiteSpace(operation) || string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("'operation', 'namespace', and 'key' are all required.");
    return 1;
}

var dataDir  = Path.Combine(AppContext.BaseDirectory, "data");
var nsDir    = Path.Combine(dataDir, SanitizeName(ns));
var keyFile  = Path.Combine(nsDir, HashKey(key));

return operation switch
{
    "read"   => Read(keyFile),
    "write"  => Write(nsDir, keyFile, cliValue),
    "update" => Update(keyFile, cliValue),
    "delete" => Delete(keyFile),
    "append" => Append(nsDir, keyFile, cliValue, separator),
    _        => UnknownOperation(operation)
};

// ── Operations ────────────────────────────────────────────────────────────────

static int Read(string keyFile)
{
    if (!File.Exists(keyFile))
    {
        // Return empty string with exit 0 — callers should treat empty as "not found"
        return 0;
    }

    try
    {
        using var fs = new FileStream(keyFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        Console.Write(sr.ReadToEnd());
        return 0;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error reading key: {ex.Message}");
        return 1;
    }
}

static int Write(string nsDir, string keyFile, string? value)
{
    if (value is null)
    {
        Console.Error.WriteLine("A value is required for the write operation.");
        return 1;
    }

    return PersistValue(nsDir, keyFile, value);
}

static int Update(string keyFile, string? value)
{
    if (value is null)
    {
        Console.Error.WriteLine("A value is required for the update operation.");
        return 1;
    }

    if (!File.Exists(keyFile))
    {
        Console.Error.WriteLine("Key not found. Use 'write' to create it.");
        return 1;
    }

    return PersistValue(Path.GetDirectoryName(keyFile)!, keyFile, value);
}

static int Delete(string keyFile)
{
    if (!File.Exists(keyFile))
    {
        Console.WriteLine("OK");
        return 0;
    }

    try
    {
        File.Delete(keyFile);
        Console.WriteLine("OK");
        return 0;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error deleting key: {ex.Message}");
        return 1;
    }
}

static int Append(string nsDir, string keyFile, string? value, string separator)
{
    if (value is null)
    {
        Console.Error.WriteLine("A value is required for the append operation.");
        return 1;
    }

    if (!File.Exists(keyFile))
        return PersistValue(nsDir, keyFile, value);

    try
    {
        string existing;
        using (var fs = new FileStream(keyFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sr = new StreamReader(fs, Encoding.UTF8))
            existing = sr.ReadToEnd();

        return PersistValue(nsDir, keyFile, existing + separator + value);
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error appending to key: {ex.Message}");
        return 1;
    }
}

static int UnknownOperation(string operation)
{
    Console.Error.WriteLine($"Unknown operation '{operation}'. Must be: read, write, update, delete, append.");
    return 1;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static int PersistValue(string nsDir, string keyFile, string value)
{
    try
    {
        Directory.CreateDirectory(nsDir);

        using var fs = new FileStream(keyFile, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sw = new StreamWriter(fs, Encoding.UTF8);
        sw.Write(value);

        Console.WriteLine("OK");
        return 0;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error writing key: {ex.Message}");
        return 1;
    }
}

// Strips characters that are invalid in directory names across all platforms.
static string SanitizeName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder(name.Length);
    foreach (var c in name)
        sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
    return sb.ToString();
}

// SHA-256 hex of the key — gives a safe, flat filename regardless of key content.
static string HashKey(string key)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}
