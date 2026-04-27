using System.Text.Json;

// ── Music generator: prompt → MP3 via ComfyUI ACE-Step v1.5 ─────────────────
//
// Reads JSON from stdin or a file path argument. Two shapes accepted:
//
//   Single track:
//   {
//     "prompt": "epic orchestral, strings, brass, hopeful",
//     "outputPath": "C:/Games/.../assets/audio/menu.mp3",
//     "duration": 30,                // optional, default 30 seconds
//     "lyrics": "",                  // optional vocals; empty = instrumental
//     "negativePrompt": "...",       // optional, default ""
//     "bpm": 120,                    // optional, default 120
//     "language": "en",              // optional, default "en"
//     "seed": 42,                    // optional, random if absent
//     "steps": 50,                   // optional, default 50
//     "cfg": 5.0,                    // optional, default 5.0
//     "quality": "V0"                // optional, V0 | 128k | 320k
//   }
//
//   Bulk:
//   {
//     "defaults": { ...same fields as single, except prompt/outputPath... },
//     "tracks":   [ { ...single track fields... }, ... ]
//   }
//
// Per-track fields override defaults.

if (args.Length >= 1 && args[0] is "help" or "--help" or "-h")
{
    PrintHelp();
    return 0;
}

string raw;
if (args.Length >= 1 && File.Exists(args[0]))
    raw = await File.ReadAllTextAsync(args[0]);
else
    raw = await Console.In.ReadToEndAsync();

if (string.IsNullOrWhiteSpace(raw))
{
    Console.Error.WriteLine("ERROR: no input JSON. Provide a JSON file path as argv[1] or pipe JSON to stdin.");
    return 2;
}

JsonElement root;
try { root = JsonSerializer.Deserialize<JsonElement>(raw); }
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: invalid JSON: {ex.Message}");
    return 2;
}

var endpoint = Environment.GetEnvironmentVariable("COMFYUI_ENDPOINT") ?? "http://localhost:8188";
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

// Decide single vs bulk by presence of `tracks` array.
List<TrackParams> tracks;
try
{
    tracks = ParseTracks(root);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 2;
}

if (tracks.Count == 0)
{
    Console.Error.WriteLine("ERROR: no tracks to generate.");
    return 2;
}

var anyFailed = false;
for (var i = 0; i < tracks.Count; i++)
{
    var t = tracks[i];
    Console.Error.WriteLine($"[{i + 1}/{tracks.Count}] Generating: {t.OutputPath}");
    var ok = await ComfyUiClient.GenerateAceStepMp3(t, http, endpoint);
    if (!ok) anyFailed = true;
}

// Free VRAM at the end of the batch (matches image-generator's behaviour).
try { await ComfyUiClient.FreeVram(http, endpoint); }
catch (Exception ex) { Console.Error.WriteLine($"  (warn) FreeVram: {ex.Message}"); }

if (anyFailed) return 1;
Console.WriteLine($"OK: {tracks.Count} track(s) generated.");
return 0;

// ── Helpers ─────────────────────────────────────────────────────────────────

static List<TrackParams> ParseTracks(JsonElement root)
{
    if (root.ValueKind != JsonValueKind.Object)
        throw new InvalidDataException("top-level JSON must be an object");

    if (root.TryGetProperty("tracks", out var arr))
    {
        if (arr.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("'tracks' must be an array");

        var defaults = root.TryGetProperty("defaults", out var d) ? d : default;

        var list = new List<TrackParams>();
        foreach (var entry in arr.EnumerateArray())
            list.Add(BuildTrack(entry, defaults));
        return list;
    }

    return new List<TrackParams> { BuildTrack(root, default) };
}

static TrackParams BuildTrack(JsonElement e, JsonElement defaults)
{
    string Str(string key, string fallback)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? fallback;
        if (defaults.ValueKind == JsonValueKind.Object && defaults.TryGetProperty(key, out var dv) && dv.ValueKind == JsonValueKind.String)
            return dv.GetString() ?? fallback;
        return fallback;
    }
    int Int(string key, int fallback)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        if (defaults.ValueKind == JsonValueKind.Object && defaults.TryGetProperty(key, out var dv) && dv.ValueKind == JsonValueKind.Number)
            return dv.GetInt32();
        return fallback;
    }
    double Dbl(string key, double fallback)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        if (defaults.ValueKind == JsonValueKind.Object && defaults.TryGetProperty(key, out var dv) && dv.ValueKind == JsonValueKind.Number)
            return dv.GetDouble();
        return fallback;
    }
    long? OptLong(string key)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt64();
        if (defaults.ValueKind == JsonValueKind.Object && defaults.TryGetProperty(key, out var dv) && dv.ValueKind == JsonValueKind.Number)
            return dv.GetInt64();
        return null;
    }

    var prompt     = Str("prompt", "");
    var outputPath = Str("outputPath", "");
    if (string.IsNullOrWhiteSpace(prompt))     throw new InvalidDataException("'prompt' is required");
    if (string.IsNullOrWhiteSpace(outputPath)) throw new InvalidDataException("'outputPath' is required");

    return new TrackParams(
        Prompt:         prompt,
        OutputPath:     outputPath,
        Duration:       Dbl("duration", 30.0),
        Lyrics:         Str("lyrics", ""),
        NegativePrompt: Str("negativePrompt", ""),
        Bpm:            Int("bpm", 120),
        Language:       Str("language", "en"),
        Seed:           OptLong("seed") ?? Random.Shared.NextInt64(0, long.MaxValue),
        Steps:          Int("steps", 8),
        Cfg:            Dbl("cfg", 1.0),
        Quality:        Str("quality", "V0")
    );
}

static void PrintHelp()
{
    Console.WriteLine(
@"music-generator — generate MP3 music via ComfyUI ACE-Step v1.5.

Usage:
  music-generator [path/to/input.json]
  cat input.json | music-generator
  music-generator --help

Input JSON (single track):
  {
    ""prompt"":     ""orchestral, strings, brass, hopeful, victory theme"",
    ""outputPath"": ""C:/Games/foo/assets/audio/menu.mp3"",
    ""duration"":   30,
    ""lyrics"":     """",
    ""bpm"":        120,
    ""seed"":       42,
    ""steps"":      8,
    ""cfg"":        1.0,
    ""quality"":    ""V0""
  }

Input JSON (bulk):
  {
    ""defaults"": { ""duration"": 30, ""steps"": 50 },
    ""tracks"":   [ { ""prompt"": ""..."", ""outputPath"": ""..."" }, ... ]
  }

Environment:
  COMFYUI_ENDPOINT  Base URL of ComfyUI (default http://localhost:8188).
");
}

internal record TrackParams(
    string Prompt,
    string OutputPath,
    double Duration,
    string Lyrics,
    string NegativePrompt,
    int    Bpm,
    string Language,
    long   Seed,
    int    Steps,
    double Cfg,
    string Quality
);
