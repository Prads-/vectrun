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
    Console.Error.WriteLine("Preset (recommended for game assets):");
    Console.Error.WriteLine("  { \"preset\": \"character_pixel|character_cartoon|env_sprite_pixel|env_sprite_cartoon|background_pixel|background_cartoon|background_topdown_pixel|background_topdown_cartoon|ui_pixel|ui_cartoon\",");
    Console.Error.WriteLine("    \"prompt\": \"<designer description>\", \"outputPath\": \"...\", [seed, negativePrompt] }");
    Console.Error.WriteLine("  Preset hard-overrides checkpoint/lora/sampler/steps/cfg/etc; only seed and negativePrompt are user-overridable.");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Single image (text-to-img):");
    Console.Error.WriteLine("  { \"prompt\": \"...\", \"outputPath\": \"...\", [negativePrompt, width, height, steps, cfg, seed, checkpoint, lora, loraStrength, clipSkip, sampler, scheduler,");
    Console.Error.WriteLine("    upscaleBy, upscaleModel, upscaleDenoise, upscaleTileWidth, upscaleTileHeight, upscaleMaskBlur, upscalePadding,");
    Console.Error.WriteLine("    trimBackground, trimPadding, trimTolerance] }");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("ControlNet (structure-preserving generation from a reference image):");
    Console.Error.WriteLine("  { \"type\": \"controlnet\", \"prompt\": \"...\", \"outputPath\": \"...\", \"referenceImage\": \"path/to/structure.png\", [controlNetModel, controlNetStrength] }");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Img2Img: same shape, type=\"img2img\", plus referenceImage and imgToImgDenoise.");
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
            // Empty bulk request is a valid "nothing to do" — used by the verify_assets
            // regen path when every expected asset is already on disk.
            Console.WriteLine("OK: 0 image(s) generated (empty request).");
            return 0;
        }

        var failed = 0;
        for (var i = 0; i < items.Count; i++)
        {
            try
            {
                var item = items[i];

                // Composite type — stitches existing images into a grid, no generation needed
                if (item.TryGetProperty("type", out var typeCheck) && typeCheck.GetString() == "composite")
                {
                    if (!item.TryGetProperty("outputPath", out var opEl) || string.IsNullOrWhiteSpace(opEl.GetString()))
                    { Console.Error.WriteLine($"[{i + 1}/{items.Count}] Missing 'outputPath' — skipping."); failed++; continue; }

                    var inputPaths = item.TryGetProperty("inputPaths", out var ipEl)
                        ? ipEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                        : new List<string>();
                    int GetInt(string key, int fallback) =>
                        item.TryGetProperty(key, out var v) ? v.GetInt32() :
                        defaults.ValueKind != JsonValueKind.Undefined && defaults.TryGetProperty(key, out var dv) ? dv.GetInt32() : fallback;

                    var columns = GetInt("columns", 4);
                    var rows = GetInt("rows", 2);
                    var frameWidth = GetInt("frameWidth", 128);
                    var frameHeight = GetInt("frameHeight", 128);
                    var compositeErr = ValidateCompositeParams(inputPaths, columns, rows, frameWidth, frameHeight);
                    if (compositeErr != null)
                    {
                        Console.Error.WriteLine($"[{i + 1}/{items.Count}] Missing/invalid {compositeErr} - skipping.");
                        failed++;
                        continue;
                    }

                    Console.Error.WriteLine($"[{i + 1}/{items.Count}] Compositing: {opEl.GetString()}");
                    var ok = await SpriteSheetGenerator.ComposeFromFiles(
                        inputPaths, opEl.GetString()!,
                        columns, rows, frameWidth, frameHeight);
                    if (!ok) failed++;
                    continue;
                }

                var p = MergeParams(defaults, item);

                var err = ValidateParams(p);
                if (err != null)
                {
                    Console.Error.WriteLine($"[{i + 1}/{items.Count}] Missing/invalid {err} — skipping.");
                    failed++;
                    continue;
                }

                Console.Error.WriteLine($"[{i + 1}/{items.Count}] Generating: {p.OutputPath}");
                var ok2 = await Dispatch(p, http, endpoint);
                if (!ok2) failed++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
            {
                Console.Error.WriteLine($"[{i + 1}/{items.Count}] Invalid request: {ex.Message}");
                failed++;
            }
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
        ImageParams p;
        try { p = MergeParams(default, json); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            Console.Error.WriteLine($"Invalid request: {ex.Message}");
            return 1;
        }

        var err = ValidateParams(p);
        if (err != null) { Console.Error.WriteLine($"Missing/invalid required field: {err}"); return 1; }

        var ok = await Dispatch(p, http, endpoint);
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
        if (item.ValueKind != JsonValueKind.Undefined && item.TryGetProperty(key, out var iv)) return read(iv);
        if (defaults.ValueKind != JsonValueKind.Undefined && defaults.TryGetProperty(key, out var dv)) return read(dv);
        return fallback;
    }

    IReadOnlyList<SheetCell> ParseCells(JsonElement arr)
        => arr.EnumerateArray().Select(a => new SheetCell(
            Row: a.TryGetProperty("row", out var r) ? r.GetInt32() : 0,
            Col: a.TryGetProperty("col", out var c) ? c.GetInt32() : 0,
            Label: a.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "",
            PromptSuffix: a.TryGetProperty("promptSuffix", out var ps) ? ps.GetString() ?? "" :
                                a.TryGetProperty("prompt", out var ap) ? ap.GetString() ?? "" : "",
            PoseReferenceImage: a.TryGetProperty("poseReferenceImage", out var pri) ? pri.GetString() ?? "" : ""
        )).ToList();

    var cells = Get<IReadOnlyList<SheetCell>>("cells", [], ParseCells);
    if (cells.Count == 0)
        cells = Get<IReadOnlyList<SheetCell>>("animations", [], ParseCells);

    return new ImageParams(
        Prompt: Get("prompt", "", e => e.GetString() ?? ""),
        OutputPath: Get("outputPath", "", e => e.GetString() ?? ""),
        NegativePrompt: Get("negativePrompt", "", e => e.GetString() ?? ""),
        Width: Get("width", 1024, e => e.GetInt32()),
        Height: Get("height", 1024, e => e.GetInt32()),
        Steps: Get("steps", 25, e => e.GetInt32()),
        Cfg: Get("cfg", 7.0, e => e.GetDouble()),
        Seed: Get("seed", Random.Shared.NextInt64(0, long.MaxValue), e => e.GetInt64()),
        Checkpoint: Get("checkpoint", DefaultCheckpoint(), e => e.GetString() ?? DefaultCheckpoint()),
        Lora: Get("lora", "", e => e.GetString() ?? ""),
        LoraStrength: Get("loraStrength", 1.0, e => e.GetDouble()),
        ClipSkip: Get("clipSkip", 0, e => e.GetInt32()),
        Sampler: Get("sampler", "euler", e => e.GetString() ?? "euler"),
        Scheduler: Get("scheduler", "normal", e => e.GetString() ?? "normal"),
        Type: Get("type", "normal", e => e.GetString() ?? "normal"),
        CharacterPrompt: Get("characterPrompt", "", e => e.GetString() ?? ""),
        Cells: cells,
        FrameWidth: Get("frameWidth", 128, e => e.GetInt32()),
        FrameHeight: Get("frameHeight", 128, e => e.GetInt32()),
        Columns: Get("columns", 6, e => e.GetInt32()),
        Rows: Get("rows", 5, e => e.GetInt32()),
        ImgToImgDenoise: Get("imgToImgDenoise", 0.70, e => e.GetDouble()),
        ReferenceImage: Get("referenceImage", "", e => e.GetString() ?? ""),
        ControlNetModel: Get("controlNetModel", DefaultControlNetModel(), e => e.GetString() ?? DefaultControlNetModel()),
        ControlNetStrength: Get("controlNetStrength", 0.9, e => e.GetDouble()),
        HeroLabel: Get("heroLabel", "", e => e.GetString() ?? ""),
        FrameDirectory: Get("frameDirectory", "", e => e.GetString() ?? ""),
        MetadataPath: Get("metadataPath", "", e => e.GetString() ?? ""),
        UpscaleBy: Get("upscaleBy", 0.0, e => e.GetDouble()),
        UpscaleModel: Get("upscaleModel", "RealESRGAN_x4plus.pth", e => e.GetString() ?? "RealESRGAN_x4plus.pth"),
        UpscaleDenoise: Get("upscaleDenoise", 0.2, e => e.GetDouble()),
        UpscaleTileWidth: Get("upscaleTileWidth", 1024, e => e.GetInt32()),
        UpscaleTileHeight: Get("upscaleTileHeight", 1024, e => e.GetInt32()),
        UpscaleMaskBlur: Get("upscaleMaskBlur", 8, e => e.GetInt32()),
        UpscalePadding: Get("upscalePadding", 32, e => e.GetInt32()),
        TrimBackground: Get("trimBackground", false, e => e.GetBoolean()),
        TrimPadding: Get("trimPadding", 8, e => e.GetInt32()),
        TrimTolerance: Get("trimTolerance", 10, e => e.GetInt32()),
        AlphaFromWhite: Get("alphaFromWhite", false, e => e.GetBoolean()),
        AlphaThreshold: Get("alphaThreshold", 15, e => e.GetInt32()),
        Preset: Get("preset", "", e => e.GetString() ?? ""),
        Perspective: Get("perspective", "", e => e.GetString() ?? "")
    );
}

static string DefaultCheckpoint() => Environment.GetEnvironmentVariable("COMFYUI_CHECKPOINT") ?? "v1-5-pruned-emaonly.safetensors";
static string DefaultControlNetModel() => Environment.GetEnvironmentVariable("CONTROLNET_MODEL") ?? "control-lora-canny-rank256.safetensors";

// Returns a human-readable error describing the first missing/invalid required field, or null when valid.
static string? ValidateParams(ImageParams p)
{
    if (!string.IsNullOrWhiteSpace(p.Preset))
    {
        if (!Presets.IsKnown(p.Preset)) return $"'preset' ('{p.Preset}') is not a known preset";
        if (string.IsNullOrWhiteSpace(p.Prompt)) return "'prompt'";
        if (string.IsNullOrWhiteSpace(p.OutputPath)) return "'outputPath'";
        return null;
    }

    var numericErr = ValidateCommonNumbers(p);
    if (numericErr != null) return numericErr;

    if (p.Type == "sprite_sheet")
    {
        if (string.IsNullOrWhiteSpace(p.CharacterPrompt)) return "'characterPrompt'";
        if (p.Cells.Count == 0) return "'cells' (empty)";
        foreach (var cell in p.Cells)
        {
            if (cell.Row < 0 || cell.Row >= p.Rows)
                return $"cell '{cell.Label}' row ({cell.Row}) outside sheet rows 0..{p.Rows - 1}";
            if (cell.Col < 0 || cell.Col >= p.Columns)
                return $"cell '{cell.Label}' col ({cell.Col}) outside sheet columns 0..{p.Columns - 1}";
            if (string.IsNullOrWhiteSpace(cell.PoseReferenceImage))
                return $"cell '{cell.Label}' poseReferenceImage";
            if (!string.IsNullOrEmpty(cell.PoseReferenceImage) && !File.Exists(cell.PoseReferenceImage))
                return $"cell '{cell.Label}' poseReferenceImage (file not found: {cell.PoseReferenceImage})";
        }
        if (!string.IsNullOrEmpty(p.HeroLabel) && !p.Cells.Any(c => c.Label == p.HeroLabel))
            return $"'heroLabel' ('{p.HeroLabel}') does not match any cell label";
    }
    else if (string.IsNullOrWhiteSpace(p.Prompt)) return "'prompt'";

    if (string.IsNullOrWhiteSpace(p.OutputPath)) return "'outputPath'";

    if (p.Type is "controlnet" or "img2img")
    {
        if (string.IsNullOrWhiteSpace(p.ReferenceImage)) return "'referenceImage'";
        if (!File.Exists(p.ReferenceImage)) return $"'referenceImage' (file not found: {p.ReferenceImage})";
    }
    return null;
}

static string? ValidateCommonNumbers(ImageParams p)
{
    if (p.Width <= 0) return "'width' must be greater than 0";
    if (p.Height <= 0) return "'height' must be greater than 0";
    if (p.Width % 8 != 0) return "'width' must be a multiple of 8";
    if (p.Height % 8 != 0) return "'height' must be a multiple of 8";
    if (p.Steps <= 0) return "'steps' must be greater than 0";
    if (p.Cfg < 0) return "'cfg' must be 0 or greater";
    if (p.ClipSkip < 0) return "'clipSkip' must be 0 or greater";
    if (p.FrameWidth <= 0) return "'frameWidth' must be greater than 0";
    if (p.FrameHeight <= 0) return "'frameHeight' must be greater than 0";
    if (p.Columns <= 0) return "'columns' must be greater than 0";
    if (p.Rows <= 0) return "'rows' must be greater than 0";
    if (p.ImgToImgDenoise is < 0 or > 1) return "'imgToImgDenoise' must be between 0 and 1";
    if (p.ControlNetStrength < 0) return "'controlNetStrength' must be 0 or greater";
    if (p.UpscaleBy < 0) return "'upscaleBy' must be 0 or greater";
    if (p.UpscaleDenoise is < 0 or > 1) return "'upscaleDenoise' must be between 0 and 1";
    if (p.UpscaleTileWidth <= 0) return "'upscaleTileWidth' must be greater than 0";
    if (p.UpscaleTileHeight <= 0) return "'upscaleTileHeight' must be greater than 0";
    if (p.UpscaleMaskBlur < 0) return "'upscaleMaskBlur' must be 0 or greater";
    if (p.UpscalePadding < 0) return "'upscalePadding' must be 0 or greater";
    if (p.TrimPadding < 0) return "'trimPadding' must be 0 or greater";
    if (p.TrimTolerance is < 0 or > 255) return "'trimTolerance' must be between 0 and 255";
    if (p.AlphaThreshold is < 0 or > 255) return "'alphaThreshold' must be between 0 and 255";
    return null;
}

static string? ValidateCompositeParams(
    IReadOnlyList<string> inputPaths, int columns, int rows, int frameWidth, int frameHeight)
{
    if (inputPaths.Count == 0) return "'inputPaths' (empty)";
    if (columns <= 0) return "'columns' must be greater than 0";
    if (rows <= 0) return "'rows' must be greater than 0";
    if (frameWidth <= 0) return "'frameWidth' must be greater than 0";
    if (frameHeight <= 0) return "'frameHeight' must be greater than 0";
    if (inputPaths.Count > columns * rows) return $"'inputPaths' count ({inputPaths.Count}) exceeds grid capacity ({columns * rows})";

    foreach (var path in inputPaths)
    {
        if (!File.Exists(path)) return $"'inputPaths' file not found: {path}";
    }

    return null;
}

static Task<bool> Dispatch(ImageParams p, HttpClient http, string endpoint)
{
    if (!string.IsNullOrWhiteSpace(p.Preset))
        return Presets.Run(p, http, endpoint);

    return p.Type switch
    {
        "sprite_sheet" => SpriteSheetGenerator.Generate(p, http, endpoint),
        "controlnet" => ComfyUiClient.GenerateWithControlNet(p, http, endpoint),
        "img2img" => ComfyUiClient.GenerateImgToImg(p, http, endpoint),
        _ => ComfyUiClient.GenerateTextToImg(p, http, endpoint),
    };
}

// ── Types ─────────────────────────────────────────────────────────────────────

record SheetCell(
    int Row,
    int Col,
    string Label,
    string PromptSuffix,
    string PoseReferenceImage
);

record ImageParams(
    string Prompt,
    string OutputPath,
    string NegativePrompt,
    int Width,
    int Height,
    int Steps,
    double Cfg,
    long Seed,
    string Checkpoint,
    string Lora,
    double LoraStrength,
    int ClipSkip,
    string Sampler,
    string Scheduler,
    string Type,
    string CharacterPrompt,
    IReadOnlyList<SheetCell> Cells,
    int FrameWidth,
    int FrameHeight,
    int Columns,
    int Rows,
    double ImgToImgDenoise,
    string ReferenceImage,
    string ControlNetModel,
    double ControlNetStrength,
    string HeroLabel,
    string FrameDirectory,
    string MetadataPath,
    double UpscaleBy,
    string UpscaleModel,
    double UpscaleDenoise,
    int UpscaleTileWidth,
    int UpscaleTileHeight,
    int UpscaleMaskBlur,
    int UpscalePadding,
    bool TrimBackground,
    int TrimPadding,
    int TrimTolerance,
    bool AlphaFromWhite,
    int AlphaThreshold,
    string Preset,
    string Perspective
);
