using System.Text;
using System.Text.Json;
using SkiaSharp;
using Svg.Skia;

// ── Parse stdin or file arg ───────────────────────────────────────────────────

var stdin = args.Length > 0
    ? (await File.ReadAllTextAsync(args[0])).Replace("\r", "")
    : (await Console.In.ReadToEndAsync()).Replace("\r", "");

if (string.IsNullOrWhiteSpace(stdin))
{
    Console.Error.WriteLine("Usage: svg-rasterize <file.json>");
    Console.Error.WriteLine("       echo '<json>' | svg-rasterize");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Single (inline SVG or file path):");
    Console.Error.WriteLine("  { \"svg\": \"<svg>...</svg>\", \"outputPath\": \"out.png\", [width, height, background] }");
    Console.Error.WriteLine("  { \"svgPath\": \"path/to/in.svg\", \"outputPath\": \"out.png\", [width, height, background] }");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Bulk (sequential):");
    Console.Error.WriteLine("  { \"defaults\": { <common fields> }, \"images\": [ { \"svgPath\": \"...\", \"outputPath\": \"...\" }, ... ] }");
    return 1;
}

JsonElement json;
try { json = JsonSerializer.Deserialize<JsonElement>(stdin); }
catch (JsonException ex) { Console.Error.WriteLine($"Invalid JSON: {ex.Message}"); return 1; }

// ── Detect mode: bulk vs single ───────────────────────────────────────────────

if (json.TryGetProperty("images", out var imageArray) && imageArray.ValueKind == JsonValueKind.Array)
{
    var defaults = json.TryGetProperty("defaults", out var def) ? def : json;
    var items    = imageArray.EnumerateArray().ToList();

    if (items.Count == 0) { Console.Error.WriteLine("'images' array is empty."); return 1; }

    var failed = 0;
    for (var i = 0; i < items.Count; i++)
    {
        var p   = ParseParams(defaults, items[i]);
        var err = ValidateParams(p);
        if (err != null)
        {
            Console.Error.WriteLine($"[{i + 1}/{items.Count}] Missing/invalid {err} — skipping.");
            failed++;
            continue;
        }

        Console.Error.WriteLine($"[{i + 1}/{items.Count}] Rasterizing: {p.OutputPath}");
        if (!Rasterize(p)) failed++;
    }

    if (failed > 0) { Console.Error.WriteLine($"{failed}/{items.Count} file(s) failed."); return 1; }
    Console.WriteLine($"OK: {items.Count} file(s) rasterized.");
    return 0;
}
else
{
    var p   = ParseParams(default, json);
    var err = ValidateParams(p);
    if (err != null) { Console.Error.WriteLine($"Missing/invalid required field: {err}"); return 1; }

    return Rasterize(p) ? 0 : 1;
}

// ── Param resolution ──────────────────────────────────────────────────────────

static RasterizeParams ParseParams(JsonElement defaults, JsonElement item)
{
    T Get<T>(string key, T fallback, Func<JsonElement, T> read)
    {
        if (item.ValueKind     != JsonValueKind.Undefined && item.TryGetProperty(key,     out var iv)) return read(iv);
        if (defaults.ValueKind != JsonValueKind.Undefined && defaults.TryGetProperty(key, out var dv)) return read(dv);
        return fallback;
    }

    int? GetNullableInt(string key)
    {
        if (item.ValueKind     != JsonValueKind.Undefined && item.TryGetProperty(key,     out var iv) && iv.ValueKind == JsonValueKind.Number) return iv.GetInt32();
        if (defaults.ValueKind != JsonValueKind.Undefined && defaults.TryGetProperty(key, out var dv) && dv.ValueKind == JsonValueKind.Number) return dv.GetInt32();
        return null;
    }

    return new RasterizeParams(
        Svg:        Get("svg",        "", e => e.GetString() ?? ""),
        SvgPath:    Get("svgPath",    "", e => e.GetString() ?? ""),
        OutputPath: Get("outputPath", "", e => e.GetString() ?? ""),
        Width:      GetNullableInt("width"),
        Height:     GetNullableInt("height"),
        Background: Get<string?>("background", null, e => e.ValueKind == JsonValueKind.Null ? null : e.GetString())
    );
}

static string? ValidateParams(RasterizeParams p)
{
    if (string.IsNullOrWhiteSpace(p.Svg) && string.IsNullOrWhiteSpace(p.SvgPath))
        return "'svg' or 'svgPath' (one is required)";
    if (!string.IsNullOrWhiteSpace(p.SvgPath) && !File.Exists(p.SvgPath))
        return $"'svgPath' (file not found: {p.SvgPath})";
    if (string.IsNullOrWhiteSpace(p.OutputPath))
        return "'outputPath'";
    if (p.Width.HasValue  && p.Width.Value  <= 0) return "'width' (must be positive)";
    if (p.Height.HasValue && p.Height.Value <= 0) return "'height' (must be positive)";
    return null;
}

// ── Rasterize one ─────────────────────────────────────────────────────────────

static bool Rasterize(RasterizeParams p)
{
    using var svg = new SKSvg();

    try
    {
        if (!string.IsNullOrWhiteSpace(p.Svg))
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(p.Svg));
            svg.Load(ms);
        }
        else
        {
            svg.Load(p.SvgPath);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Failed to load SVG: {ex.Message}");
        return false;
    }

    var picture = svg.Picture;
    if (picture is null)
    {
        Console.Error.WriteLine("  SVG parsed but produced no renderable picture.");
        return false;
    }

    var bounds = picture.CullRect;
    if (bounds.Width <= 0 || bounds.Height <= 0)
    {
        Console.Error.WriteLine($"  SVG has zero-sized bounds ({bounds.Width}x{bounds.Height}).");
        return false;
    }

    // Resolve output dimensions. Given:
    //   both → exact size, content fit-within preserving aspect ratio, centered with transparent padding
    //   one  → preserve aspect ratio, derive the other
    //   none → use intrinsic SVG bounds
    int outW, outH;
    if (p.Width.HasValue && p.Height.HasValue)
    {
        outW = p.Width.Value;
        outH = p.Height.Value;
    }
    else if (p.Width.HasValue)
    {
        outW = p.Width.Value;
        outH = Math.Max(1, (int)Math.Round(bounds.Height * outW / bounds.Width));
    }
    else if (p.Height.HasValue)
    {
        outH = p.Height.Value;
        outW = Math.Max(1, (int)Math.Round(bounds.Width * outH / bounds.Height));
    }
    else
    {
        outW = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        outH = Math.Max(1, (int)Math.Ceiling(bounds.Height));
    }

    var bg = SKColors.Transparent;
    if (!string.IsNullOrWhiteSpace(p.Background) && !SKColor.TryParse(p.Background, out bg))
    {
        Console.Error.WriteLine($"  Invalid background color: {p.Background}");
        return false;
    }

    using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
    if (surface is null) { Console.Error.WriteLine("  Could not create drawing surface."); return false; }

    var canvas = surface.Canvas;
    canvas.Clear(bg);

    var scale   = Math.Min(outW / bounds.Width, outH / bounds.Height);
    var scaledW = bounds.Width  * scale;
    var scaledH = bounds.Height * scale;
    var tx      = (outW - scaledW) / 2 - bounds.Left * scale;
    var ty      = (outH - scaledH) / 2 - bounds.Top  * scale;
    canvas.Translate(tx, ty);
    canvas.Scale(scale);
    canvas.DrawPicture(picture);
    canvas.Flush();

    byte[] pngBytes;
    try
    {
        using var image = surface.Snapshot();
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        pngBytes = data.ToArray();
    }
    catch (Exception ex) { Console.Error.WriteLine($"  Failed to encode PNG: {ex.Message}"); return false; }

    var outputPath = Path.GetFullPath(p.OutputPath);
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, pngBytes);
    }
    catch (Exception ex) { Console.Error.WriteLine($"  Failed to save: {ex.Message}"); return false; }

    Console.WriteLine($"OK: {outputPath} ({pngBytes.Length} bytes, {outW}x{outH})");
    return true;
}

// ── Types ─────────────────────────────────────────────────────────────────────

record RasterizeParams(
    string  Svg,
    string  SvgPath,
    string  OutputPath,
    int?    Width,
    int?    Height,
    string? Background
);
