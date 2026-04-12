using System.Text;
using System.Text.Json;

// ── Parse stdin or file arg ───────────────────────────────────────────────────

var stdin = args.Length > 0
    ? (await File.ReadAllTextAsync(args[0])).Replace("\r", "")
    : (await Console.In.ReadToEndAsync()).Replace("\r", "");

if (string.IsNullOrWhiteSpace(stdin))
{
    Console.Error.WriteLine("Usage: image-generator <file.json>");
    Console.Error.WriteLine("       echo '<json>' | image-generator");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Single image:");
    Console.Error.WriteLine("  { \"prompt\": \"...\", \"outputPath\": \"...\", [negativePrompt, width, height, steps, cfg, seed, checkpoint, sampler, scheduler] }");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Bulk (sequential):");
    Console.Error.WriteLine("  { \"defaults\": { <common fields> }, \"images\": [ { \"prompt\": \"...\", \"outputPath\": \"...\" }, ... ] }");
    return 1;
}

JsonElement json;
try { json = JsonSerializer.Deserialize<JsonElement>(stdin); }
catch (JsonException ex) { Console.Error.WriteLine($"Invalid JSON: {ex.Message}"); return 1; }

var endpoint = Environment.GetEnvironmentVariable("COMFYUI_ENDPOINT") ?? "http://localhost:8188";
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

// ── Detect mode: bulk vs single ───────────────────────────────────────────────
try
{
    if (json.TryGetProperty("images", out var imageArray) && imageArray.ValueKind == JsonValueKind.Array)
    {
        // Bulk mode
        var defaults = json.TryGetProperty("defaults", out var def) ? def : json;
        var items = imageArray.EnumerateArray().ToList();

        if (items.Count == 0)
        {
            Console.Error.WriteLine("'images' array is empty.");
            return 1;
        }

        var failed = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var p = MergeParams(defaults, item);

            if (p.Type == "sprite_sheet" ? string.IsNullOrWhiteSpace(p.CharacterPrompt) : string.IsNullOrWhiteSpace(p.Prompt))
            {
                var field = p.Type == "sprite_sheet" ? "characterPrompt" : "prompt";
                Console.Error.WriteLine($"[{i + 1}/{items.Count}] Missing '{field}' — skipping.");
                failed++;
                continue;
            }
            if (string.IsNullOrWhiteSpace(p.OutputPath))
            {
                Console.Error.WriteLine($"[{i + 1}/{items.Count}] Missing 'outputPath' — skipping.");
                failed++;
                continue;
            }

            Console.Error.WriteLine($"[{i + 1}/{items.Count}] Generating: {p.OutputPath}");
            var ok = p.Type == "sprite_sheet"
                ? await SpriteSheetGenerator.Generate(p, http, endpoint)
                : await ComfyUiClient.GenerateTextToImg(p, http, endpoint);
            if (!ok) failed++;
        }

        if (failed > 0)
        {
            Console.Error.WriteLine($"{failed}/{items.Count} image(s) failed.");
            return 1;
        }

        Console.WriteLine($"OK: {items.Count} image(s) generated.");
        return 0;
    }
    else
    {
        // Single image mode (backward-compatible)
        var p = MergeParams(default, json);

        if (p.Type == "sprite_sheet" ? string.IsNullOrWhiteSpace(p.CharacterPrompt) : string.IsNullOrWhiteSpace(p.Prompt))
        { Console.Error.WriteLine($"Missing required field: '{(p.Type == "sprite_sheet" ? "characterPrompt" : "prompt")}'"); return 1; }

        if (string.IsNullOrWhiteSpace(p.OutputPath))
        { Console.Error.WriteLine("Missing required field: 'outputPath'"); return 1; }

        var ok = p.Type == "sprite_sheet"
            ? await SpriteSheetGenerator.Generate(p, http, endpoint)
            : await ComfyUiClient.GenerateTextToImg(p, http, endpoint);
        return ok ? 0 : 1;
    }
}
finally
{
    // Free VRAM once at the end
    await ComfyUiClient.FreeVram(http, endpoint);
}

// ── Param resolution ──────────────────────────────────────────────────────────

// Merges defaults + per-image overrides. Per-image fields take precedence.
// Common fields (negativePrompt, steps, cfg, checkpoint, sampler, scheduler)
// belong in defaults. Per-image fields (prompt, outputPath, seed, width, height) belong in images[].
static ImageParams MergeParams(JsonElement defaults, JsonElement item)
{
    T Get<T>(string key, T fallback, Func<JsonElement, T> read)
    {
        if (item.ValueKind     != JsonValueKind.Undefined && item.TryGetProperty(key,     out var iv)) return read(iv);
        if (defaults.ValueKind != JsonValueKind.Undefined && defaults.TryGetProperty(key, out var dv)) return read(dv);
        return fallback;
    }

    var animations = Get<IReadOnlyList<AnimationCell>>("animations", [],
        e => e.EnumerateArray().Select(a => new AnimationCell(
            Row:    a.TryGetProperty("row",    out var r)  ? r.GetInt32()         : 0,
            Col:    a.TryGetProperty("col",    out var c)  ? c.GetInt32()         : 0,
            Prompt: a.TryGetProperty("prompt", out var ap) ? ap.GetString() ?? "" : ""
        )).ToList());

    return new ImageParams(
        Prompt:          Get("prompt",          "",                                        e => e.GetString() ?? ""),
        OutputPath:      Get("outputPath",       "",                                        e => e.GetString() ?? ""),
        NegativePrompt:  Get("negativePrompt",   DefaultNegative(),                         e => e.GetString() ?? DefaultNegative()),
        Width:           Get("width",            1024,                                      e => e.GetInt32()),
        Height:          Get("height",           1024,                                      e => e.GetInt32()),
        Steps:           Get("steps",            25,                                        e => e.GetInt32()),
        Cfg:             Get("cfg",              7.0,                                       e => e.GetDouble()),
        Seed:            Get("seed",             Random.Shared.NextInt64(0, long.MaxValue), e => e.GetInt64()),
        Checkpoint:      Get("checkpoint",       DefaultCheckpoint(),                       e => e.GetString() ?? DefaultCheckpoint()),
        Sampler:         Get("sampler",          "euler",                                   e => e.GetString() ?? "euler"),
        Scheduler:       Get("scheduler",        "normal",                                  e => e.GetString() ?? "normal"),
        Type:            Get("type",             "normal",                                  e => e.GetString() ?? "normal"),
        CharacterPrompt: Get("characterPrompt",  "",                                        e => e.GetString() ?? ""),
        Animations:      animations,
        FrameWidth:      Get("frameWidth",       128,                                       e => e.GetInt32()),
        FrameHeight:     Get("frameHeight",      128,                                       e => e.GetInt32()),
        Columns:         Get("columns",          6,                                         e => e.GetInt32()),
        Rows:            Get("rows",             5,                                         e => e.GetInt32()),
        ImgToImgDenoise:  Get("imgToImgDenoise",  0.70,                                      e => e.GetDouble()),
        IpAdapterWeight:  Get("ipAdapterWeight",  0.7,                                       e => e.GetDouble())
    );
}

static string DefaultNegative()   => "blurry, low quality, distorted, watermark, text, signature, ugly, deformed";
static string DefaultCheckpoint() => Environment.GetEnvironmentVariable("COMFYUI_CHECKPOINT") ?? "v1-5-pruned-emaonly.safetensors";

// ── Types ─────────────────────────────────────────────────────────────────────

record AnimationCell(int Row, int Col, string Prompt);

record ImageParams(
    string                      Prompt,
    string                      OutputPath,
    string                      NegativePrompt,
    int                         Width,
    int                         Height,
    int                         Steps,
    double                      Cfg,
    long                        Seed,
    string                      Checkpoint,
    string                      Sampler,
    string                      Scheduler,
    string                      Type,
    string                      CharacterPrompt,
    IReadOnlyList<AnimationCell> Animations,
    int                         FrameWidth,
    int                         FrameHeight,
    int                         Columns,
    int                         Rows,
    double                      ImgToImgDenoise,
    double                      IpAdapterWeight
);
