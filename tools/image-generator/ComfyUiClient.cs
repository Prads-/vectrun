using System.Text;
using System.Text.Json;

internal static class ComfyUiClient
{
    // ── Text-to-image ─────────────────────────────────────────────────────────

    internal static async Task<bool> GenerateTextToImg(ImageParams p, HttpClient http, string endpoint)
    {
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
                    seed         = p.Seed,
                    steps        = p.Steps,
                    cfg          = p.Cfg,
                    sampler_name = p.Sampler,
                    scheduler    = p.Scheduler,
                    denoise      = 1.0,
                    model        = new object[] { "1", 0 },
                    positive     = new object[] { "2", 0 },
                    negative     = new object[] { "3", 0 },
                    latent_image = new object[] { "4", 0 }
                }
            },
            ["6"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
            ["7"] = new { class_type = "SaveImage", inputs = new { filename_prefix = "vectrun", images = new object[] { "6", 0 } } }
        };

        return await QueueAndSave(workflow, p, http, endpoint);
    }

    // ── Image-to-image ────────────────────────────────────────────────────────

    internal static async Task<bool> GenerateImgToImg(ImageParams p, string referenceImagePath, HttpClient http, string endpoint)
    {
        var uploadedName = await UploadImage(referenceImagePath, http, endpoint);
        if (uploadedName is null)
        {
            Console.Error.WriteLine("  Failed to upload reference image to ComfyUI.");
            return false;
        }

        var workflow = new Dictionary<string, object>
        {
            ["1"]  = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = p.Checkpoint } },
            ["2"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.Prompt,         clip = new object[] { "1", 1 } } },
            ["3"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.NegativePrompt, clip = new object[] { "1", 1 } } },
            ["4a"] = new { class_type = "LoadImage",              inputs = new { image = uploadedName } },
            ["4b"] = new { class_type = "VAEEncode",              inputs = new { pixels = new object[] { "4a", 0 }, vae = new object[] { "1", 2 } } },
            ["5"]  = new
            {
                class_type = "KSampler",
                inputs     = new
                {
                    seed         = p.Seed,
                    steps        = p.Steps,
                    cfg          = p.Cfg,
                    sampler_name = p.Sampler,
                    scheduler    = p.Scheduler,
                    denoise      = p.ImgToImgDenoise,
                    model        = new object[] { "1", 0 },
                    positive     = new object[] { "2", 0 },
                    negative     = new object[] { "3", 0 },
                    latent_image = new object[] { "4b", 0 }
                }
            },
            ["6"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
            ["7"] = new { class_type = "SaveImage", inputs = new { filename_prefix = "vectrun", images = new object[] { "6", 0 } } }
        };

        return await QueueAndSave(workflow, p, http, endpoint);
    }

    // ── Upload reference image ────────────────────────────────────────────────

    internal static async Task<string?> UploadImage(string imagePath, HttpClient http, string endpoint)
    {
        using var form   = new MultipartFormDataContent();
        var imageBytes   = await File.ReadAllBytesAsync(imagePath);
        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", Path.GetFileName(imagePath));

        HttpResponseMessage response;
        try   { response = await http.PostAsync($"{endpoint}/upload/image", form); }
        catch (Exception ex) { Console.Error.WriteLine($"  Upload failed: {ex.Message}"); return null; }

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"  Upload error ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
            return null;
        }

        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return json.TryGetProperty("name", out var name) ? name.GetString() : null;
    }

    // ── Free VRAM ─────────────────────────────────────────────────────────────

    internal static async Task FreeVram(HttpClient http, string endpoint)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { unload_models = true, free_memory = true });
            await http.PostAsync($"{endpoint}/free", new StringContent(body, Encoding.UTF8, "application/json"));
        }
        catch { /* best-effort */ }
    }

    // ── Shared: queue → poll → download → save ────────────────────────────────

    private static async Task<bool> QueueAndSave(Dictionary<string, object> workflow, ImageParams p, HttpClient http, string endpoint)
    {
        var clientId  = Guid.NewGuid().ToString("N");
        var queueBody = JsonSerializer.Serialize(new { prompt = workflow, client_id = clientId });

        HttpResponseMessage queueResponse;
        try   { queueResponse = await http.PostAsync($"{endpoint}/prompt", new StringContent(queueBody, Encoding.UTF8, "application/json")); }
        catch (Exception ex) { Console.Error.WriteLine($"Could not reach ComfyUI at {endpoint}: {ex.Message}"); return false; }

        var queueText = await queueResponse.Content.ReadAsStringAsync();
        if (!queueResponse.IsSuccessStatusCode)
        { Console.Error.WriteLine($"ComfyUI queue error ({(int)queueResponse.StatusCode}): {queueText}"); return false; }

        var promptId = JsonSerializer.Deserialize<JsonElement>(queueText).GetProperty("prompt_id").GetString()!;
        Console.Error.WriteLine($"  Queued: {promptId}");

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

        var imageInfo = outputs.GetProperty("7").GetProperty("images")[0];
        var filename  = imageInfo.GetProperty("filename").GetString()!;
        var subfolder = imageInfo.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
        var type      = imageInfo.TryGetProperty("type",      out var tp) ? tp.GetString() ?? "output" : "output";
        var viewUrl   = $"{endpoint}/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";

        byte[] imageBytes;
        try   { imageBytes = await http.GetByteArrayAsync(viewUrl); }
        catch (Exception ex) { Console.Error.WriteLine($"  Failed to download: {ex.Message}"); return false; }

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
}
