using System.Text;
using System.Text.Json;
using SkiaSharp;

internal static class ComfyUiClient
{
    // SDXL-class models can't usefully generate below ~512 px; anything tinier comes
    // out as noise. We always generate at ≥ this on the smaller side, then downscale
    // the saved PNG to whatever the caller asked for.
    private const int MinGenDimension = 512;
    private const int MaxGenDimension = 2048;
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(10);

    // If the caller asks for a size smaller than the model can reliably produce,
    // scale UP preserving aspect ratio until the shorter side hits the minimum,
    // round to the nearest multiple of 8, and cap the longer side at the maximum.
    // Returns the generation dims and whether a post-generation resize is needed.
    private static (int GenW, int GenH, bool NeedsResize) ResolveGenSize(int requestedW, int requestedH)
    {
        if (requestedW >= MinGenDimension && requestedH >= MinGenDimension)
            return (requestedW, requestedH, false);

        var scale = (double)MinGenDimension / Math.Min(requestedW, requestedH);
        var genW  = (int)Math.Round(requestedW * scale);
        var genH  = (int)Math.Round(requestedH * scale);

        genW = Math.Min(MaxGenDimension, ((genW + 7) / 8) * 8);
        genH = Math.Min(MaxGenDimension, ((genH + 7) / 8) * 8);

        return (genW, genH, true);
    }

    // ── LoRA / ClipSkip / Upscale helpers ─────────────────────────────────────
    //
    // Optional nodes are inserted into the workflow by key:
    //   "L1" LoraLoader         — when p.Lora is set
    //   "CS" CLIPSetLastLayer   — when p.ClipSkip > 0
    //   "U1" UpscaleModelLoader — when p.UpscaleBy > 0
    //   "U2" UltimateSDUpscale  — when p.UpscaleBy > 0
    //
    // Callers compute MODEL/CLIP refs with ModelClipRefs() and the final image
    // ref with FinalImageRef() BEFORE initializing the workflow dict, then call
    // AddLoraAndClipSkipNodes() and AddUpscaleNodes() AFTER to inject the
    // optional nodes. VAE always reads from the checkpoint directly.

    private static (object[] ModelRef, object[] ClipRef) ModelClipRefs(ImageParams p)
    {
        var hasLora     = !string.IsNullOrWhiteSpace(p.Lora);
        var hasClipSkip = p.ClipSkip > 0;

        var modelRef = hasLora ? new object[] { "L1", 0 } : new object[] { "1", 0 };
        var clipRef  = hasClipSkip ? new object[] { "CS", 0 }
                                   : hasLora ? new object[] { "L1", 1 } : new object[] { "1", 1 };
        return (modelRef, clipRef);
    }

    private static void AddLoraAndClipSkipNodes(Dictionary<string, object> workflow, ImageParams p)
    {
        var hasLora = !string.IsNullOrWhiteSpace(p.Lora);

        if (hasLora)
        {
            workflow["L1"] = new
            {
                class_type = "LoraLoader",
                inputs     = new
                {
                    model          = new object[] { "1", 0 },
                    clip           = new object[] { "1", 1 },
                    lora_name      = p.Lora,
                    strength_model = p.LoraStrength,
                    strength_clip  = p.LoraStrength
                }
            };
        }

        if (p.ClipSkip > 0)
        {
            workflow["CS"] = new
            {
                class_type = "CLIPSetLastLayer",
                inputs     = new
                {
                    clip               = hasLora ? new object[] { "L1", 1 } : new object[] { "1", 1 },
                    stop_at_clip_layer = -p.ClipSkip
                }
            };
        }
    }

    private static object[] FinalImageRef(ImageParams p) =>
        p.UpscaleBy > 0 ? new object[] { "U2", 0 } : new object[] { "6", 0 };

    private static void AddUpscaleNodes(Dictionary<string, object> workflow, ImageParams p, object[] modelRef)
    {
        if (p.UpscaleBy <= 0) return;

        workflow["U1"] = new
        {
            class_type = "UpscaleModelLoader",
            inputs     = new { model_name = p.UpscaleModel }
        };

        workflow["U2"] = new
        {
            class_type = "UltimateSDUpscale",
            inputs     = new
            {
                batch_size          = 1,
                image               = new object[] { "6", 0 },
                model               = modelRef,
                positive            = new object[] { "2", 0 },
                negative            = new object[] { "3", 0 },
                vae                 = new object[] { "1", 2 },
                upscale_by          = p.UpscaleBy,
                seed                = p.Seed,
                steps               = p.Steps,
                cfg                 = p.Cfg,
                sampler_name        = p.Sampler,
                scheduler           = p.Scheduler,
                denoise             = p.UpscaleDenoise,
                upscale_model       = new object[] { "U1", 0 },
                mode_type           = "Linear",
                tile_width          = p.UpscaleTileWidth,
                tile_height         = p.UpscaleTileHeight,
                mask_blur           = p.UpscaleMaskBlur,
                tile_padding        = p.UpscalePadding,
                seam_fix_mode       = "None",
                seam_fix_denoise    = 1.0,
                seam_fix_width      = 64,
                seam_fix_mask_blur  = 8,
                seam_fix_padding    = 16,
                force_uniform_tiles = true,
                tiled_decode        = false
            }
        };
    }

    // ── Text-to-image ─────────────────────────────────────────────────────────

    internal static async Task<bool> GenerateTextToImg(ImageParams p, HttpClient http, string endpoint)
    {
        var (genW, genH, needsResize) = ResolveGenSize(p.Width, p.Height);
        var (modelRef, clipRef)       = ModelClipRefs(p);
        var finalImageRef             = FinalImageRef(p);

        var workflow = new Dictionary<string, object>
        {
            ["1"] = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = p.Checkpoint } },
            ["2"] = new { class_type = "CLIPTextEncode",         inputs = new { text = p.Prompt,         clip = clipRef } },
            ["3"] = new { class_type = "CLIPTextEncode",         inputs = new { text = p.NegativePrompt, clip = clipRef } },
            ["4"] = new { class_type = "EmptyLatentImage",       inputs = new { width = genW, height = genH, batch_size = 1 } },
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
                    model        = modelRef,
                    positive     = new object[] { "2", 0 },
                    negative     = new object[] { "3", 0 },
                    latent_image = new object[] { "4", 0 }
                }
            },
            ["6"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
            ["7"] = new { class_type = "SaveImage", inputs = new { filename_prefix = "vectrun", images = finalImageRef } }
        };
        AddLoraAndClipSkipNodes(workflow, p);
        AddUpscaleNodes(workflow, p, modelRef);

        return await QueueAndSave(workflow, p, http, endpoint, needsResize ? (p.Width, p.Height) : null);
    }

    // ── Image-to-image ────────────────────────────────────────────────────────

    internal static async Task<bool> GenerateImgToImg(ImageParams p, HttpClient http, string endpoint)
    {
        var uploadedName = await UploadImage(p.ReferenceImage, http, endpoint);
        if (uploadedName is null)
        {
            Console.Error.WriteLine("  Failed to upload reference image to ComfyUI.");
            return false;
        }

        var (modelRef, clipRef) = ModelClipRefs(p);
        var finalImageRef       = FinalImageRef(p);

        var workflow = new Dictionary<string, object>
        {
            ["1"]  = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = p.Checkpoint } },
            ["2"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.Prompt,         clip = clipRef } },
            ["3"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.NegativePrompt, clip = clipRef } },
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
                    model        = modelRef,
                    positive     = new object[] { "2", 0 },
                    negative     = new object[] { "3", 0 },
                    latent_image = new object[] { "4b", 0 }
                }
            },
            ["6"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
            ["7"] = new { class_type = "SaveImage", inputs = new { filename_prefix = "vectrun", images = finalImageRef } }
        };
        AddLoraAndClipSkipNodes(workflow, p);
        AddUpscaleNodes(workflow, p, modelRef);

        return await QueueAndSave(workflow, p, http, endpoint);
    }

    // ── img2img + ControlNet (combined, internal) ─────────────────────────────
    //
    // Used by SpriteSheetGenerator Phase 1: init image is the hero raster (colour
    // bleeds through via low denoise), ControlNet locks edges via the same raster.
    // Single reference image feeds both nodes.

    internal static async Task<bool> GenerateImgToImgWithControlNet(ImageParams p, HttpClient http, string endpoint)
    {
        var uploadedName = await UploadImage(p.ReferenceImage, http, endpoint);
        if (uploadedName is null)
        {
            Console.Error.WriteLine("  Failed to upload hero reference image to ComfyUI.");
            return false;
        }

        var (modelRef, clipRef) = ModelClipRefs(p);
        var finalImageRef       = FinalImageRef(p);

        var workflow = new Dictionary<string, object>
        {
            ["1"]  = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = p.Checkpoint } },
            ["2"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.Prompt,         clip = clipRef } },
            ["3"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.NegativePrompt, clip = clipRef } },
            ["4a"] = new { class_type = "LoadImage",              inputs = new { image = uploadedName } },
            ["4b"] = new { class_type = "VAEEncode",              inputs = new { pixels = new object[] { "4a", 0 }, vae = new object[] { "1", 2 } } },
            ["8"]  = new { class_type = "ControlNetLoader",       inputs = new { control_net_name = p.ControlNetModel } },
            ["10"] = new
            {
                class_type = "ControlNetApplyAdvanced",
                inputs     = new
                {
                    positive      = new object[] { "2", 0 },
                    negative      = new object[] { "3", 0 },
                    control_net   = new object[] { "8", 0 },
                    image         = new object[] { "4a", 0 },
                    strength      = p.ControlNetStrength,
                    start_percent = 0.0,
                    end_percent   = 1.0
                }
            },
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
                    denoise      = p.ImgToImgDenoise,
                    model        = modelRef,
                    positive     = new object[] { "10", 0 },
                    negative     = new object[] { "10", 1 },
                    latent_image = new object[] { "4b", 0 }
                }
            },
            ["6"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
            ["7"] = new { class_type = "SaveImage", inputs = new { filename_prefix = "vectrun", images = finalImageRef } }
        };
        AddLoraAndClipSkipNodes(workflow, p);
        AddUpscaleNodes(workflow, p, modelRef);

        return await QueueAndSave(workflow, p, http, endpoint);
    }

    // ── ControlNet (structure-preserving text-to-image) ───────────────────────
    //
    // Runs a text-to-image generation whose composition is locked to the edges
    // of a reference image. Typical use: rasterize a Claude-designed SVG and
    // feed it here so Stable Diffusion renders a detailed sprite that preserves
    // the exact pose, silhouette, and proportions of the SVG.

    internal static async Task<bool> GenerateWithControlNet(ImageParams p, HttpClient http, string endpoint)
    {
        var uploadedName = await UploadImage(p.ReferenceImage, http, endpoint);
        if (uploadedName is null)
        {
            Console.Error.WriteLine("  Failed to upload ControlNet reference image to ComfyUI.");
            return false;
        }

        var (genW, genH, needsResize) = ResolveGenSize(p.Width, p.Height);
        var (modelRef, clipRef)       = ModelClipRefs(p);
        var finalImageRef             = FinalImageRef(p);

        var workflow = new Dictionary<string, object>
        {
            ["1"]  = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = p.Checkpoint } },
            ["2"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.Prompt,         clip = clipRef } },
            ["3"]  = new { class_type = "CLIPTextEncode",         inputs = new { text = p.NegativePrompt, clip = clipRef } },
            ["4"]  = new { class_type = "EmptyLatentImage",       inputs = new { width = genW, height = genH, batch_size = 1 } },
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
                    denoise      = 1.0,
                    model        = modelRef,
                    positive     = new object[] { "10", 0 }, // conditioning from ControlNetApplyAdvanced
                    negative     = new object[] { "10", 1 },
                    latent_image = new object[] { "4",  0 }
                }
            },
            ["6"]  = new { class_type = "VAEDecode",              inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
            ["7"]  = new { class_type = "SaveImage",              inputs = new { filename_prefix = "vectrun", images = finalImageRef } },
            ["8"]  = new { class_type = "LoadImage",              inputs = new { image = uploadedName } },
            ["9"]  = new { class_type = "ControlNetLoader",       inputs = new { control_net_name = p.ControlNetModel } },
            ["10"] = new
            {
                class_type = "ControlNetApplyAdvanced",
                inputs     = new
                {
                    positive      = new object[] { "2", 0 },
                    negative      = new object[] { "3", 0 },
                    control_net   = new object[] { "9", 0 },
                    image         = new object[] { "8", 0 },
                    strength      = p.ControlNetStrength,
                    start_percent = 0.0,
                    end_percent   = 1.0
                }
            }
        };
        AddLoraAndClipSkipNodes(workflow, p);
        AddUpscaleNodes(workflow, p, modelRef);

        return await QueueAndSave(workflow, p, http, endpoint, needsResize ? (p.Width, p.Height) : null);
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

    private static async Task<bool> QueueAndSave(Dictionary<string, object> workflow, ImageParams p, HttpClient http, string endpoint, (int Width, int Height)? resizeTarget = null)
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
        var pollTimer = System.Diagnostics.Stopwatch.StartNew();
        while (pollTimer.Elapsed < PromptTimeout)
        {
            var remaining = PromptTimeout - pollTimer.Elapsed;
            var delay = remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);

            remaining = PromptTimeout - pollTimer.Elapsed;
            if (remaining <= TimeSpan.Zero) break;

            HttpResponseMessage histResponse;
            using var pollTimeout = new CancellationTokenSource(remaining);
            try   { histResponse = await http.GetAsync($"{endpoint}/history/{promptId}", pollTimeout.Token); }
            catch (OperationCanceledException) when (pollTimer.Elapsed >= PromptTimeout) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"  Poll failed: {ex.Message}"); continue; }

            if (!histResponse.IsSuccessStatusCode) continue;

            var histJson = JsonSerializer.Deserialize<JsonElement>(await histResponse.Content.ReadAsStringAsync());
            if (!histJson.TryGetProperty(promptId, out var entry)) continue;

            if (entry.TryGetProperty("status", out var status) &&
                status.TryGetProperty("status_str", out var statusStr) &&
                statusStr.GetString() == "error")
            {
                var messages = status.TryGetProperty("messages", out var msgs) ? msgs.ToString() : "(no details)";
                Console.Error.WriteLine($"  ComfyUI reported an error: {messages}");
                return false;
            }

            if (!entry.TryGetProperty("outputs", out outputs) || outputs.ValueKind == JsonValueKind.Undefined)
                continue;

            if (outputs.TryGetProperty("7", out var saveNode) &&
                saveNode.TryGetProperty("images", out var images) &&
                images.GetArrayLength() > 0)
                break;
        }

        if (pollTimer.Elapsed >= PromptTimeout)
        {
            Console.Error.WriteLine($"  Timed out waiting for ComfyUI prompt after {PromptTimeout.TotalMinutes:0} minutes: {promptId}");
            return false;
        }

        var imageInfo = outputs.GetProperty("7").GetProperty("images")[0];
        var filename  = imageInfo.GetProperty("filename").GetString()!;
        var subfolder = imageInfo.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
        var type      = imageInfo.TryGetProperty("type",      out var tp) ? tp.GetString() ?? "output" : "output";
        var viewUrl   = $"{endpoint}/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";

        byte[] imageBytes;
        try   { imageBytes = await http.GetByteArrayAsync(viewUrl); }
        catch (Exception ex) { Console.Error.WriteLine($"  Failed to download: {ex.Message}"); return false; }

        if (p.TrimBackground)
        {
            try   { imageBytes = TrimWhiteBorders(imageBytes, p.TrimPadding, p.TrimTolerance); }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to trim: {ex.Message}"); return false; }
        }

        if (p.AlphaFromWhite)
        {
            try   { imageBytes = WhiteToAlpha(imageBytes, p.AlphaThreshold); }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to apply alpha: {ex.Message}"); return false; }
        }

        if (resizeTarget is (int targetW, int targetH))
        {
            try   { imageBytes = ResizePng(imageBytes, targetW, targetH); }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to resize: {ex.Message}"); return false; }
        }

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

    private static byte[] ResizePng(byte[] pngBytes, int targetW, int targetH)
    {
        using var src = SKBitmap.Decode(pngBytes);
        if (src is null) throw new InvalidOperationException("Could not decode PNG for resize.");

        var info = new SKImageInfo(targetW, targetH, src.ColorType, src.AlphaType);
        using var dst = src.Resize(info, SKSamplingOptions.Default)
                        ?? throw new InvalidOperationException("SkiaSharp resize returned null.");
        using var image = SKImage.FromBitmap(dst);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // Crops white/transparent borders down to the subject's bounding box. A pixel
    // counts as background if it's nearly transparent OR all RGB channels are
    // within `tolerance` of 255. Interior whites are preserved — only the outer
    // frame is trimmed. Falls back to the original bytes if the whole image is
    // background.
    private static byte[] TrimWhiteBorders(byte[] pngBytes, int padding, int tolerance)
    {
        using var src = SKBitmap.Decode(pngBytes);
        if (src is null) throw new InvalidOperationException("Could not decode PNG for trim.");

        var w = src.Width;
        var h = src.Height;
        var whiteFloor = (byte)Math.Max(0, 255 - tolerance);

        int top = -1, bottom = -1, left = w, right = -1;

        for (var y = 0; y < h; y++)
        {
            var rowHasForeground = false;
            for (var x = 0; x < w; x++)
            {
                var c = src.GetPixel(x, y);
                var isBackground = c.Alpha < 8 ||
                                   (c.Red >= whiteFloor && c.Green >= whiteFloor && c.Blue >= whiteFloor);
                if (isBackground) continue;

                rowHasForeground = true;
                if (x < left)  left  = x;
                if (x > right) right = x;
            }
            if (!rowHasForeground) continue;
            if (top == -1) top = y;
            bottom = y;
        }

        if (top == -1)
        {
            Console.Error.WriteLine("  Trim: whole image is background, skipping.");
            return pngBytes;
        }

        top    = Math.Max(0,     top    - padding);
        bottom = Math.Min(h - 1, bottom + padding);
        left   = Math.Max(0,     left   - padding);
        right  = Math.Min(w - 1, right  + padding);

        var cropW = right - left + 1;
        var cropH = bottom - top + 1;

        using var cropped = new SKBitmap();
        if (!src.ExtractSubset(cropped, new SKRectI(left, top, right + 1, bottom + 1)))
            throw new InvalidOperationException("SkiaSharp ExtractSubset failed.");

        using var image = SKImage.FromBitmap(cropped);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        Console.Error.WriteLine($"  Trim: {w}x{h} → {cropW}x{cropH}");
        return data.ToArray();
    }

    // Converts only edge-connected near-white background pixels to transparent.
    // Interior whites are preserved so UI highlights and sprite details are not
    // punched out.
    private static byte[] WhiteToAlpha(byte[] pngBytes, int tolerance)
    {
        using var src = SKBitmap.Decode(pngBytes);
        if (src is null) throw new InvalidOperationException("Could not decode PNG for alpha conversion.");

        var w = src.Width;
        var h = src.Height;
        var whiteFloor = (byte)Math.Max(0, 255 - tolerance);

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dst = new SKBitmap(info);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(src, 0, 0);

        var connectedBackground = new bool[w, h];
        var queue = new Queue<(int X, int Y)>();

        bool IsBackground(int x, int y)
        {
            var c = src.GetPixel(x, y);
            return c.Alpha < 8 ||
                   (c.Red >= whiteFloor && c.Green >= whiteFloor && c.Blue >= whiteFloor);
        }

        void TryEnqueue(int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            if (connectedBackground[x, y] || !IsBackground(x, y)) return;

            connectedBackground[x, y] = true;
            queue.Enqueue((x, y));
        }

        for (var x = 0; x < w; x++)
        {
            TryEnqueue(x, 0);
            TryEnqueue(x, h - 1);
        }

        for (var y = 0; y < h; y++)
        {
            TryEnqueue(0, y);
            TryEnqueue(w - 1, y);
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            TryEnqueue(x - 1, y);
            TryEnqueue(x + 1, y);
            TryEnqueue(x, y - 1);
            TryEnqueue(x, y + 1);
        }

        var cleared = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (connectedBackground[x, y])
                {
                    dst.SetPixel(x, y, SKColors.Transparent);
                    cleared++;
                }
            }
        }

        using var image = SKImage.FromBitmap(dst);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        var totalPixels = w * h;
        Console.Error.WriteLine($"  Alpha: cleared {cleared}/{totalPixels} pixels ({100 * cleared / totalPixels}%)");
        return data.ToArray();
    }

}
