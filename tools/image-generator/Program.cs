using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// ── Parse stdin ───────────────────────────────────────────────────────────────

var stdin = await Console.In.ReadToEndAsync();

if (string.IsNullOrWhiteSpace(stdin))
{
    Console.Error.WriteLine("Usage: echo '<json>' | image-generator");
    Console.Error.WriteLine("Fields: prompt (required), outputPath (required), negativePrompt, width, height, steps, cfg, seed, checkpoint, sampler, scheduler");
    return 1;
}

JsonElement json;
try { json = JsonSerializer.Deserialize<JsonElement>(stdin); }
catch (JsonException ex) { Console.Error.WriteLine($"Invalid JSON: {ex.Message}"); return 1; }

if (!json.TryGetProperty("prompt", out var promptProp) || string.IsNullOrWhiteSpace(promptProp.GetString()))
{ Console.Error.WriteLine("Missing required field: 'prompt'"); return 1; }

if (!json.TryGetProperty("outputPath", out var outPathProp) || string.IsNullOrWhiteSpace(outPathProp.GetString()))
{ Console.Error.WriteLine("Missing required field: 'outputPath'"); return 1; }

var prompt         = promptProp.GetString()!;
var outputPath     = Path.GetFullPath(outPathProp.GetString()!);
var negativePrompt = json.TryGetProperty("negativePrompt",  out var negProp)  ? negProp.GetString()  ?? DefaultNegative()   : DefaultNegative();
var width          = json.TryGetProperty("width",           out var wProp)    ? wProp.GetInt32()                            : 1024;
var height         = json.TryGetProperty("height",          out var hProp)    ? hProp.GetInt32()                            : 1024;
var steps          = json.TryGetProperty("steps",           out var stProp)   ? stProp.GetInt32()                           : 25;
var cfg            = json.TryGetProperty("cfg",             out var cfgProp)  ? cfgProp.GetDouble()                         : 7.0;
var seed           = json.TryGetProperty("seed",            out var seedProp) ? seedProp.GetInt64()                         : Random.Shared.NextInt64(0, long.MaxValue);
var checkpoint     = json.TryGetProperty("checkpoint",      out var ckptProp)  ? ckptProp.GetString()  ?? DefaultCheckpoint()   : DefaultCheckpoint();
var sampler        = json.TryGetProperty("sampler",         out var sampProp)  ? sampProp.GetString()  ?? "euler"               : "euler";
var scheduler      = json.TryGetProperty("scheduler",       out var schedProp) ? schedProp.GetString() ?? "normal"              : "normal";
var endpoint       = Environment.GetEnvironmentVariable("COMFYUI_ENDPOINT") ?? "http://localhost:8188";
var clientId       = Guid.NewGuid().ToString("N");

// ── Build workflow ────────────────────────────────────────────────────────────

// Node IDs are stable strings used as references within the workflow graph.
// Layout: 1=CheckpointLoader, 2=PositivePrompt, 3=NegativePrompt,
//         4=EmptyLatentImage, 5=KSampler, 6=VAEDecode, 7=SaveImage

var workflow = new Dictionary<string, object>
{
    ["1"] = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = checkpoint } },
    ["2"] = new { class_type = "CLIPTextEncode",         inputs = new { text = prompt,         clip = new object[] { "1", 1 } } },
    ["3"] = new { class_type = "CLIPTextEncode",         inputs = new { text = negativePrompt, clip = new object[] { "1", 1 } } },
    ["4"] = new { class_type = "EmptyLatentImage",       inputs = new { width, height, batch_size = 1 } },
    ["5"] = new
    {
        class_type = "KSampler",
        inputs     = new
        {
            seed,
            steps,
            cfg,
            sampler_name   = sampler,
            scheduler      = scheduler,
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

// ── Queue prompt ──────────────────────────────────────────────────────────────

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

var queueBody    = JsonSerializer.Serialize(new { prompt = workflow, client_id = clientId });
var queueRequest = new StringContent(queueBody, Encoding.UTF8, "application/json");

HttpResponseMessage queueResponse;
try   { queueResponse = await http.PostAsync($"{endpoint}/prompt", queueRequest); }
catch (Exception ex) { Console.Error.WriteLine($"Could not reach ComfyUI at {endpoint}: {ex.Message}"); return 1; }

var queueBody2 = await queueResponse.Content.ReadAsStringAsync();
if (!queueResponse.IsSuccessStatusCode)
{ Console.Error.WriteLine($"ComfyUI queue error ({(int)queueResponse.StatusCode}): {queueBody2}"); return 1; }

var queueJson  = JsonSerializer.Deserialize<JsonElement>(queueBody2);
var promptId   = queueJson.GetProperty("prompt_id").GetString()!;
Console.Error.WriteLine($"Queued: {promptId}");

// ── Poll history until complete ───────────────────────────────────────────────

JsonElement outputs = default;
while (true)
{
    await Task.Delay(2000);

    HttpResponseMessage histResponse;
    try   { histResponse = await http.GetAsync($"{endpoint}/history/{promptId}"); }
    catch (Exception ex) { Console.Error.WriteLine($"History poll failed: {ex.Message}"); continue; }

    if (!histResponse.IsSuccessStatusCode) continue;

    var histJson = JsonSerializer.Deserialize<JsonElement>(await histResponse.Content.ReadAsStringAsync());

    if (!histJson.TryGetProperty(promptId, out var entry)) continue;

    // Check for error status
    if (entry.TryGetProperty("status", out var status) &&
        status.TryGetProperty("status_str", out var statusStr) &&
        statusStr.GetString() == "error")
    {
        Console.Error.WriteLine("ComfyUI reported an error generating the image.");
        return 1;
    }

    if (!entry.TryGetProperty("outputs", out outputs) || outputs.ValueKind == JsonValueKind.Undefined)
        continue;

    // Check if our SaveImage node (id "7") has produced output
    if (outputs.TryGetProperty("7", out var saveNode) &&
        saveNode.TryGetProperty("images", out var images) &&
        images.GetArrayLength() > 0)
        break;
}

// ── Download image ────────────────────────────────────────────────────────────

var imageInfo  = outputs.GetProperty("7").GetProperty("images")[0];
var filename   = imageInfo.GetProperty("filename").GetString()!;
var subfolder  = imageInfo.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
var type       = imageInfo.TryGetProperty("type",      out var tp) ? tp.GetString() ?? "output" : "output";

var viewUrl    = $"{endpoint}/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";

byte[] imageBytes;
try   { imageBytes = await http.GetByteArrayAsync(viewUrl); }
catch (Exception ex) { Console.Error.WriteLine($"Failed to download image: {ex.Message}"); return 1; }

// ── Save to outputPath ────────────────────────────────────────────────────────

try
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await File.WriteAllBytesAsync(outputPath, imageBytes);
}
catch (Exception ex) { Console.Error.WriteLine($"Failed to save to '{outputPath}': {ex.Message}"); return 1; }

Console.WriteLine($"OK: {outputPath} ({imageBytes.Length} bytes)");

// ── Free VRAM ─────────────────────────────────────────────────────────────────

try
{
    var freeBody = JsonSerializer.Serialize(new { unload_models = true, free_memory = true });
    await http.PostAsync($"{endpoint}/free", new StringContent(freeBody, Encoding.UTF8, "application/json"));
}
catch { /* best-effort — image is already saved */ }

return 0;

// ── Defaults ──────────────────────────────────────────────────────────────────

static string DefaultNegative()   => "blurry, low quality, distorted, watermark, text, signature, ugly, deformed";
static string DefaultCheckpoint() => Environment.GetEnvironmentVariable("COMFYUI_CHECKPOINT") ?? "v1-5-pruned-emaonly.safetensors";
