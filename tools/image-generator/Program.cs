using System.Text;
using System.Text.Json;

// ── Parse stdin ───────────────────────────────────────────────────────────────

var stdin = await Console.In.ReadToEndAsync();

if (string.IsNullOrWhiteSpace(stdin))
{
    Console.Error.WriteLine("Usage: echo '<json>' | image-generator");
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

if (json.TryGetProperty("images", out var imageArray) && imageArray.ValueKind == JsonValueKind.Array)
{
    // Bulk mode
    var defaults = json.TryGetProperty("defaults", out var def) ? def : default;
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

        if (string.IsNullOrWhiteSpace(p.Prompt))
        {
            Console.Error.WriteLine($"[{i + 1}/{items.Count}] Missing 'prompt' — skipping.");
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
        var ok = await GenerateImage(p, http, endpoint);
        if (!ok) failed++;
    }

    // Free VRAM once at the end
    await FreeVram(http, endpoint);

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

    if (string.IsNullOrWhiteSpace(p.Prompt))
    { Console.Error.WriteLine("Missing required field: 'prompt'"); return 1; }

    if (string.IsNullOrWhiteSpace(p.OutputPath))
    { Console.Error.WriteLine("Missing required field: 'outputPath'"); return 1; }

    var ok = await GenerateImage(p, http, endpoint);
    await FreeVram(http, endpoint);
    return ok ? 0 : 1;
}

// ── Generation ────────────────────────────────────────────────────────────────

static async Task<bool> GenerateImage(ImageParams p, HttpClient http, string endpoint)
{
    var clientId = Guid.NewGuid().ToString("N");

    var workflow = new Dictionary<string, object>
    {
        ["1"] = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = p.Checkpoint } },
        ["2"] = new { class_type = "CLIPTextEncode",         inputs = new { text = p.Prompt,         clip = new object[] { "1", 1 } } },
        ["3"] = new { class_type = "CLIPTextEncode",         inputs = new { text = p.NegativePrompt, clip = new object[] { "1", 1 } } },
        ["4"] = new { class_type = "EmptyLatentImage",       inputs = new { width = p.Width, height = p.Height, batch_size = 1 } },
        ["5"] = new
        {
            class_type = "KSampler",
            inputs     = new
            {
                seed           = p.Seed,
                steps          = p.Steps,
                cfg            = p.Cfg,
                sampler_name   = p.Sampler,
                scheduler      = p.Scheduler,
                denoise        = 1.0,
                model          = new object[] { "1", 0 },
                positive       = new object[] { "2", 0 },
                negative       = new object[] { "3", 0 },
                latent_image   = new object[] { "4", 0 }
            }
        },
        ["6"] = new { class_type = "VAEDecode",  inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
        ["7"] = new { class_type = "SaveImage",  inputs = new { filename_prefix = "vectrun", images = new object[] { "6", 0 } } }
    };

    // Queue
    var queueBody = JsonSerializer.Serialize(new { prompt = workflow, client_id = clientId });
    HttpResponseMessage queueResponse;
    try   { queueResponse = await http.PostAsync($"{endpoint}/prompt", new StringContent(queueBody, Encoding.UTF8, "application/json")); }
    catch (Exception ex) { Console.Error.WriteLine($"Could not reach ComfyUI at {endpoint}: {ex.Message}"); return false; }

    var queueText = await queueResponse.Content.ReadAsStringAsync();
    if (!queueResponse.IsSuccessStatusCode)
    { Console.Error.WriteLine($"ComfyUI queue error ({(int)queueResponse.StatusCode}): {queueText}"); return false; }

    var promptId = JsonSerializer.Deserialize<JsonElement>(queueText).GetProperty("prompt_id").GetString()!;
    Console.Error.WriteLine($"  Queued: {promptId}");

    // Poll
    JsonElement outputs = default;
    while (true)
    {
        await Task.Delay(2000);

        HttpResponseMessage histResponse;
        try   { histResponse = await http.GetAsync($"{endpoint}/history/{promptId}"); }
        catch (Exception ex) { Console.Error.WriteLine($"  Poll failed: {ex.Message}"); continue; }

        if (!histResponse.IsSuccessStatusCode) continue;

        var histJson = JsonSerializer.Deserialize<JsonElement>(await histResponse.Content.ReadAsStringAsync());
        if (!histJson.TryGetProperty(promptId, out var entry)) continue;

        if (entry.TryGetProperty("status", out var status) &&
            status.TryGetProperty("status_str", out var statusStr) &&
            statusStr.GetString() == "error")
        {
            Console.Error.WriteLine("  ComfyUI reported an error.");
            return false;
        }

        if (!entry.TryGetProperty("outputs", out outputs) || outputs.ValueKind == JsonValueKind.Undefined)
            continue;

        if (outputs.TryGetProperty("7", out var saveNode) &&
            saveNode.TryGetProperty("images", out var images) &&
            images.GetArrayLength() > 0)
            break;
    }

    // Download
    var imageInfo = outputs.GetProperty("7").GetProperty("images")[0];
    var filename  = imageInfo.GetProperty("filename").GetString()!;
    var subfolder = imageInfo.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
    var type      = imageInfo.TryGetProperty("type",      out var tp) ? tp.GetString() ?? "output" : "output";
    var viewUrl   = $"{endpoint}/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";

    byte[] imageBytes;
    try   { imageBytes = await http.GetByteArrayAsync(viewUrl); }
    catch (Exception ex) { Console.Error.WriteLine($"  Failed to download: {ex.Message}"); return false; }

    // Save
    var outputPath = Path.GetFullPath(p.OutputPath);
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllBytesAsync(outputPath, imageBytes);
    }
    catch (Exception ex) { Console.Error.WriteLine($"  Failed to save: {ex.Message}"); return false; }

    Console.WriteLine($"OK: {outputPath} ({imageBytes.Length} bytes)");
    return true;
}

static async Task FreeVram(HttpClient http, string endpoint)
{
    try
    {
        var body = JsonSerializer.Serialize(new { unload_models = true, free_memory = true });
        await http.PostAsync($"{endpoint}/free", new StringContent(body, Encoding.UTF8, "application/json"));
    }
    catch { /* best-effort */ }
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

    return new ImageParams(
        Prompt:         Get("prompt",         "",                           e => e.GetString() ?? ""),
        OutputPath:     Get("outputPath",      "",                           e => e.GetString() ?? ""),
        NegativePrompt: Get("negativePrompt",  DefaultNegative(),            e => e.GetString() ?? DefaultNegative()),
        Width:          Get("width",           1024,                         e => e.GetInt32()),
        Height:         Get("height",          1024,                         e => e.GetInt32()),
        Steps:          Get("steps",           25,                           e => e.GetInt32()),
        Cfg:            Get("cfg",             7.0,                          e => e.GetDouble()),
        Seed:           Get("seed",            Random.Shared.NextInt64(0, long.MaxValue), e => e.GetInt64()),
        Checkpoint:     Get("checkpoint",      DefaultCheckpoint(),          e => e.GetString() ?? DefaultCheckpoint()),
        Sampler:        Get("sampler",         "euler",                      e => e.GetString() ?? "euler"),
        Scheduler:      Get("scheduler",       "normal",                     e => e.GetString() ?? "normal")
    );
}

static string DefaultNegative()   => "blurry, low quality, distorted, watermark, text, signature, ugly, deformed";
static string DefaultCheckpoint() => Environment.GetEnvironmentVariable("COMFYUI_CHECKPOINT") ?? "v1-5-pruned-emaonly.safetensors";

// ── Types ─────────────────────────────────────────────────────────────────────

record ImageParams(
    string Prompt,
    string OutputPath,
    string NegativePrompt,
    int    Width,
    int    Height,
    int    Steps,
    double Cfg,
    long   Seed,
    string Checkpoint,
    string Sampler,
    string Scheduler
);
