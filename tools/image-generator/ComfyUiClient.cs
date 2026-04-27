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

    private static object[] PreBgRemovalImageRef(ImageParams p) =>
        p.UpscaleBy > 0 ? new object[] { "U2", 0 } : new object[] { "6", 0 };

    // FinalImageRef points at whatever node feeds SaveImage. If RemoveBackground
    // is enabled the BG1 node sits between the upscaler/VAE and SaveImage.
    private static object[] FinalImageRef(ImageParams p) =>
        p.RemoveBackground ? new object[] { "BG1", 0 } : PreBgRemovalImageRef(p);

    // Adds a ComfyUI-RMBG node that runs ML-based foreground segmentation on
    // whatever feeds it. Replaces the brittle EdgeBackgroundToAlpha flood-fill
    // for character/static-character presets — works regardless of bg colour or
    // body colour overlap. Requires the `1038lab/ComfyUI-RMBG` custom node and
    // the corresponding model weights (auto-downloaded on first use).
    private static void AddBgRemovalNode(Dictionary<string, object> workflow, ImageParams p)
    {
        if (!p.RemoveBackground) return;

        workflow["BG1"] = new
        {
            class_type = "RMBG",
            inputs     = new
            {
                image             = PreBgRemovalImageRef(p),
                model             = string.IsNullOrWhiteSpace(p.BgRemovalModel) ? "INSPYRENET" : p.BgRemovalModel,
                sensitivity       = 1.0,
                process_res       = 2048,
                mask_blur         = 0,
                mask_offset       = 0,
                invert_output     = false,
                refine_foreground = true,
                background        = "Alpha",
                background_color  = "#222222"
            }
        };
    }

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
        AddBgRemovalNode(workflow, p);

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
        AddBgRemovalNode(workflow, p);

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
        AddBgRemovalNode(workflow, p);

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
        AddBgRemovalNode(workflow, p);

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
            try   { imageBytes = EdgeBackgroundToAlpha(imageBytes, p.AlphaThreshold); }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to apply alpha: {ex.Message}"); return false; }
        }

        // ML segmentation (RemoveBackground via RMBG node) often leaves the body
        // interior partially or fully transparent — especially for "holographic"
        // or "glitchy" prompts where the model classifies the body as see-through.
        // HardenAlpha snaps interior pixels to opaque and fills enclosed holes
        // with the dominant body colour so the character stays visible against
        // any game background.
        if (p.RemoveBackground)
        {
            try   { imageBytes = HardenAlpha(imageBytes); }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to harden alpha: {ex.Message}"); return false; }
        }

        // Connected-components cleanup: drop floating opaque fragments left over
        // by ML segmentation (ghosts of dark BG that the matte didn't fully cull).
        // Runs after Harden so we operate on the final opaque/transparent classification.
        if (p.DespeckleAlpha > 0)
        {
            try   { imageBytes = DespeckleAlpha(imageBytes, p.DespeckleAlpha); }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to despeckle alpha: {ex.Message}"); return false; }
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

    // Snaps every interior body pixel to fully opaque while preserving silhouette
    // edge AA and true-background transparency. Used after ML segmentation
    // (RemoveBackground) where INSPYRENET / RMBG produce soft mattes — for
    // "translucent-style" prompts (holographic, glitchy, ghostly) the model
    // tells the segmenter the body is transparent, leaving alpha=0 throughout
    // the body interior. Composited over a non-flat game bg the character
    // would look see-through (or worse, render as a black blob if RGB at
    // alpha=0 is preserved as zero).
    //
    // Algorithm:
    //   1. Flood-fill alpha=0 from the image border → "true background" mask.
    //   2. Compute the median RGB across all alpha > 200 pixels — this is
    //      the dominant body colour, sampled from features the segmenter
    //      kept opaque (silhouette ring, eyes, accents).
    //   3. For each non-true-background pixel:
    //      - If any 4-neighbour is true-background → silhouette edge, keep alpha.
    //      - Else if alpha = 0 → enclosed hole, fill with bodyColour, alpha=255.
    //      - Else (alpha > 0) → snap to alpha=255 keeping its existing RGB
    //        (preserves intentional dark accents like eyes / shading).
    //
    // We use a single dominant body colour rather than per-pixel dilation
    // because dilation would pick up dark accent pixels (eyes, outlines)
    // and propagate them through the empty interior, painting the body black.
    public static byte[] HardenAlpha(byte[] pngBytes)
    {
        // Decode without premultiplication so RGB is preserved at alpha=0
        // pixels (default SKBitmap.Decode would zero them out).
        using var inData = SKData.CreateCopy(pngBytes);
        using var codec = SKCodec.Create(inData) ?? throw new InvalidOperationException("Could not create codec for alpha harden.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dst = new SKBitmap(info);
        var result = codec.GetPixels(info, dst.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            throw new InvalidOperationException($"PNG decode failed for alpha harden: {result}");

        var w = dst.Width;
        var h = dst.Height;
        var pixels = dst.Pixels;  // SKColor[w*h], indexed y*w + x

        // Step 1: flood-fill alpha=0 from the image border to identify true background.
        var outside = new bool[w * h];
        var stack = new Stack<int>(w * 4);
        void Seed(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            var i = y * w + x;
            if (outside[i]) return;
            if (pixels[i].Alpha != 0) return;
            outside[i] = true;
            stack.Push(i);
        }
        for (var x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (var y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            var x = i % w;
            var y = i / w;
            if (x > 0)     Seed(x - 1, y);
            if (x < w - 1) Seed(x + 1, y);
            if (y > 0)     Seed(x, y - 1);
            if (y < h - 1) Seed(x, y + 1);
        }

        // Step 2: compute median RGB across high-alpha pixels (the dominant body colour).
        var rs = new List<byte>();
        var gs = new List<byte>();
        var bs = new List<byte>();
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            if (c.Alpha < 200) continue;
            rs.Add(c.Red); gs.Add(c.Green); bs.Add(c.Blue);
        }

        SKColor bodyColour;
        if (rs.Count > 0)
        {
            rs.Sort(); gs.Sort(); bs.Sort();
            var mid = rs.Count / 2;
            bodyColour = new SKColor(rs[mid], gs[mid], bs[mid], 255);
        }
        else
        {
            // Fallback: no opaque source pixels at all. Use mid-grey.
            bodyColour = new SKColor(128, 128, 128, 255);
        }

        // Step 3: classify and snap.
        var hardened = 0;
        var edgesKept = 0;
        var holesFilled = 0;
        var changed = new SKColor[pixels.Length];
        Array.Copy(pixels, changed, pixels.Length);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (outside[i]) continue;  // true bg, alpha=0

                var isEdge =
                    (x > 0     && outside[i - 1]) ||
                    (x < w - 1 && outside[i + 1]) ||
                    (y > 0     && outside[i - w]) ||
                    (y < h - 1 && outside[i + w]);

                var c = pixels[i];
                if (isEdge)
                {
                    if (c.Alpha < 255) edgesKept++;
                    continue;  // preserve silhouette AA
                }

                if (c.Alpha == 0)
                {
                    changed[i] = bodyColour;
                    holesFilled++;
                }
                else if (c.Alpha < 255)
                {
                    changed[i] = new SKColor(c.Red, c.Green, c.Blue, 255);
                    hardened++;
                }
            }
        }

        dst.Pixels = changed;

        using var image = SKImage.FromBitmap(dst);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        Console.Error.WriteLine($"  Harden: bodyColour=({bodyColour.Red},{bodyColour.Green},{bodyColour.Blue}), {hardened} translucent → 255, {holesFilled} holes filled, {edgesKept} silhouette-edge AA preserved");
        return data.ToArray();
    }

    // Drops floating opaque fragments smaller than minArea pixels by setting
    // their alpha to 0. Used after HardenAlpha to clean up "ghost" BG remnants
    // that ML segmentation left near the silhouette — those fragments survive
    // Harden because Harden only fills holes INSIDE the silhouette, not noise
    // OUTSIDE it. For sprite sheets each character cell is one large component
    // (usually tens of thousands of pixels); noise blobs are typically a few
    // hundred to a few thousand pixels, so an absolute pixel threshold cleanly
    // distinguishes the two.
    public static byte[] DespeckleAlpha(byte[] pngBytes, int minArea)
    {
        if (minArea <= 0) return pngBytes;

        using var inData = SKData.CreateCopy(pngBytes);
        using var codec = SKCodec.Create(inData) ?? throw new InvalidOperationException("Could not create codec for despeckle.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dst = new SKBitmap(info);
        var result = codec.GetPixels(info, dst.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            throw new InvalidOperationException($"PNG decode failed for despeckle: {result}");

        var w = dst.Width;
        var h = dst.Height;
        var pixels = dst.Pixels;

        // Label connected components of opaque (alpha > 0) pixels using 4-connectivity.
        var labels = new int[w * h];          // 0 = unvisited, -1 = transparent, >=1 = component id
        var areas = new List<int> { 0 };       // areas[componentId]; index 0 unused
        var queue = new Queue<int>();
        var nextId = 1;

        for (var i = 0; i < pixels.Length; i++)
            if (pixels[i].Alpha == 0) labels[i] = -1;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var idx = y * w + x;
                if (labels[idx] != 0) continue; // -1 (bg) or already labelled

                var id = nextId++;
                areas.Add(0);
                labels[idx] = id;
                queue.Enqueue(idx);

                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    areas[id]++;
                    var px = p % w;
                    var py = p / w;
                    if (px > 0 && labels[p - 1] == 0)     { labels[p - 1] = id; queue.Enqueue(p - 1); }
                    if (px < w - 1 && labels[p + 1] == 0) { labels[p + 1] = id; queue.Enqueue(p + 1); }
                    if (py > 0 && labels[p - w] == 0)     { labels[p - w] = id; queue.Enqueue(p - w); }
                    if (py < h - 1 && labels[p + w] == 0) { labels[p + w] = id; queue.Enqueue(p + w); }
                }
            }
        }

        // Drop any component with area < minArea by setting its pixels' alpha to 0.
        var dropped = 0;
        var droppedPixels = 0;
        var changed = new SKColor[pixels.Length];
        Array.Copy(pixels, changed, pixels.Length);
        for (var i = 0; i < pixels.Length; i++)
        {
            var lab = labels[i];
            if (lab > 0 && areas[lab] < minArea)
            {
                var c = pixels[i];
                changed[i] = new SKColor(c.Red, c.Green, c.Blue, 0);
                droppedPixels++;
            }
        }
        for (var id = 1; id < areas.Count; id++)
            if (areas[id] < minArea) dropped++;

        dst.Pixels = changed;

        using var image = SKImage.FromBitmap(dst);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        Console.Error.WriteLine($"  Despeckle: {nextId - 1} components found, {dropped} dropped (<{minArea}px), {droppedPixels} pixels cleared");
        return data.ToArray();
    }

    // Removes the image background by:
    //   1. Auto-detecting the dominant edge color (mode of 16-step-binned edge pixels).
    //   2. Flood-filling from the image border, marking pixels within `tolerance`
    //      Manhattan distance per channel of that detected colour as transparent.
    // Works for ANY uniform-ish background colour (white, black, gray, anything).
    // Interior pixels of the same colour are preserved because the flood stops at
    // any pixel outside tolerance — typically the character outline.
    public static byte[] EdgeBackgroundToAlpha(byte[] pngBytes, int tolerance)
    {
        using var src = SKBitmap.Decode(pngBytes);
        if (src is null) throw new InvalidOperationException("Could not decode PNG for alpha conversion.");

        var w = src.Width;
        var h = src.Height;

        // ── Step 1: detect background colour from edge pixels ─────────────────
        // Bin each opaque edge pixel into 16-step buckets per channel (4096 bins
        // total). The most-populated bin's averaged colour is the background.
        var bins = new Dictionary<int, (int Count, long R, long G, long B)>();

        void AddSample(int x, int y)
        {
            var c = src.GetPixel(x, y);
            if (c.Alpha < 8) return;
            var key = ((c.Red >> 4) << 8) | ((c.Green >> 4) << 4) | (c.Blue >> 4);
            if (bins.TryGetValue(key, out var e))
                bins[key] = (e.Count + 1, e.R + c.Red, e.G + c.Green, e.B + c.Blue);
            else
                bins[key] = (1, c.Red, c.Green, c.Blue);
        }

        for (var x = 0; x < w; x++) { AddSample(x, 0); AddSample(x, h - 1); }
        for (var y = 1; y < h - 1; y++) { AddSample(0, y); AddSample(w - 1, y); }

        if (bins.Count == 0)
        {
            Console.Error.WriteLine("  Alpha: no opaque edge pixels to sample — skipping.");
            return pngBytes;
        }

        // Take up to 3 dominant edge-color bins, but only those with at least 15%
        // of the most-popular bin's count. SD outputs frequently have 2-3 distinct
        // bg shades (e.g. warm gray near sheet edges + lighter gray in cell interiors
        // + decorative grass band) that need separate flood seeds.
        //
        // Filter out near-white (R,G,B all ≥ 245) and near-black (R,G,B all ≤ 10)
        // candidates: characters frequently have white or black body parts and
        // flood-filling from those colours will eat the body via thin outline gaps.
        // If the actual bg IS pure white or pure black, the SD output is malformed
        // and we'd rather leave it untouched than corrupt the character.
        bool IsCharacterColour(int r, int g, int b) =>
            (r >= 245 && g >= 245 && b >= 245) || (r <= 10 && g <= 10 && b <= 10);

        var topCount = bins.Values.Max(b => b.Count);
        var dominants = bins.Values
            .Where(b => b.Count >= topCount * 15 / 100)
            .Where(b => !IsCharacterColour((int)(b.R / b.Count), (int)(b.G / b.Count), (int)(b.B / b.Count)))
            .OrderByDescending(b => b.Count)
            .Take(3)
            .Select(b => ((byte)(b.R / b.Count), (byte)(b.G / b.Count), (byte)(b.B / b.Count)))
            .ToList();

        if (dominants.Count == 0)
        {
            Console.Error.WriteLine("  Alpha: all dominant edge colours overlap with character (white/black) — skipping to avoid eating body.");
            return pngBytes;
        }
        var manhattanBudget = tolerance * 3;

        // ── Step 2: edge-connected flood fill against each detected bg colour ──
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dst = new SKBitmap(info);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(src, 0, 0);

        var visited = new bool[w, h];

        foreach (var (bgR, bgG, bgB) in dominants)
        {
            var queue = new Queue<(int X, int Y)>();

            bool IsBg(int x, int y)
            {
                var c = src.GetPixel(x, y);
                if (c.Alpha < 8) return true;
                var dr = Math.Abs(c.Red - bgR);
                var dg = Math.Abs(c.Green - bgG);
                var db = Math.Abs(c.Blue - bgB);
                return dr + dg + db <= manhattanBudget;
            }

            void TryEnqueue(int x, int y)
            {
                if (x < 0 || x >= w || y < 0 || y >= h) return;
                if (visited[x, y] || !IsBg(x, y)) return;
                visited[x, y] = true;
                queue.Enqueue((x, y));
            }

            for (var x = 0; x < w; x++) { TryEnqueue(x, 0); TryEnqueue(x, h - 1); }
            for (var y = 0; y < h; y++) { TryEnqueue(0, y); TryEnqueue(w - 1, y); }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                TryEnqueue(x - 1, y);
                TryEnqueue(x + 1, y);
                TryEnqueue(x, y - 1);
                TryEnqueue(x, y + 1);
            }
        }

        var cleared = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (visited[x, y])
                {
                    dst.SetPixel(x, y, SKColors.Transparent);
                    cleared++;
                }
            }
        }

        using var image = SKImage.FromBitmap(dst);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        var totalPixels = w * h;
        var bgList = string.Join(", ", dominants.Select(d => $"({d.Item1},{d.Item2},{d.Item3})"));
        Console.Error.WriteLine($"  Alpha: detected bgs=[{bgList}] tol={tolerance} → cleared {cleared}/{totalPixels} ({100 * cleared / totalPixels}%)");
        return data.ToArray();
    }

}
