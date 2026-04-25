using SkiaSharp;

internal static class SpriteSheetGenerator
{
    private static string Slugify(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "cell";
        var sb = new System.Text.StringBuilder(label.Length);
        foreach (var ch in label.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        var s = sb.ToString().Trim('-');
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Length == 0 ? "cell" : s;
    }

    internal static async Task<bool> ComposeFromFiles(
        IReadOnlyList<string> inputPaths, string outputPath, int columns, int rows, int frameWidth, int frameHeight)
    {
        using var bitmap = new SKBitmap(columns * frameWidth, rows * frameHeight);
        using var canvas  = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        for (var i = 0; i < inputPaths.Count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            if (row >= rows) break;

            using var src = SKBitmap.Decode(inputPaths[i]);
            if (src is null) { Console.Error.WriteLine($"  Warning: could not decode {inputPaths[i]}, skipping."); continue; }
            using var resized = src.Resize(new SKImageInfo(frameWidth, frameHeight), SKSamplingOptions.Default);
            canvas.DrawBitmap(resized, col * frameWidth, row * frameHeight);
        }

        using var skImage = SKImage.FromBitmap(bitmap);
        using var data    = skImage.Encode(SKEncodedImageFormat.Png, 100);

        var absPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
        await File.WriteAllBytesAsync(absPath, data.ToArray());

        Console.WriteLine($"OK: {absPath} ({data.Size} bytes, {inputPaths.Count} frames)");
        return true;
    }

    internal static async Task<bool> Generate(ImageParams p, HttpClient http, string endpoint)
    {
        if (p.Cells.Count == 0)
        {
            Console.Error.WriteLine("  [sprite_sheet] 'cells' is empty.");
            return false;
        }

        var frameDir = string.IsNullOrEmpty(p.FrameDirectory)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p.OutputPath))!, "frames")
            : Path.GetFullPath(p.FrameDirectory);
        Directory.CreateDirectory(frameDir);

        SheetCell? heroCell = null;
        if (!string.IsNullOrEmpty(p.HeroLabel))
            heroCell = p.Cells.FirstOrDefault(c => c.Label == p.HeroLabel);

        var rendered = new List<RenderedCell>();
        var cellIndex = 0;

        foreach (var cell in p.Cells)
        {
            cellIndex++;
            if (cell.Row >= p.Rows || cell.Col >= p.Columns)
            {
                Console.Error.WriteLine($"  [sprite_sheet] Cell '{cell.Label}' ({cell.Row},{cell.Col}) out of bounds ({p.Rows}x{p.Columns}), skipping.");
                continue;
            }

            if (string.IsNullOrEmpty(cell.PoseReferenceImage))
            {
                Console.Error.WriteLine($"  [sprite_sheet] Cell '{cell.Label}' missing 'poseReferenceImage'; cannot render.");
                return false;
            }

            var framePath = Path.Combine(frameDir, $"{Slugify(cell.Label)}.png");
            Console.Error.WriteLine($"  [sprite_sheet] [{cellIndex}/{p.Cells.Count}] rendering '{cell.Label}' ({cell.Row},{cell.Col}) via img2img+ControlNet -> {framePath}");

            var cellParams = p with
            {
                Prompt         = $"{p.CharacterPrompt}, {cell.PromptSuffix}",
                OutputPath     = framePath,
                ReferenceImage = cell.PoseReferenceImage,
            };

            if (!await ComfyUiClient.GenerateImgToImgWithControlNet(cellParams, http, endpoint))
                return false;

            rendered.Add(new RenderedCell(cell, framePath));
        }

        return await CompositeAndWriteMetadata(p, frameDir, heroCell, rendered);
    }

    private record RenderedCell(SheetCell Cell, string FramePath);

    private static async Task<bool> CompositeAndWriteMetadata(
        ImageParams p, string frameDir,
        SheetCell? heroCell, IReadOnlyList<RenderedCell> rendered)
    {
        var sheetWidth  = p.Columns * p.FrameWidth;
        var sheetHeight = p.Rows    * p.FrameHeight;

        Console.Error.WriteLine($"  [sprite_sheet] compositing {rendered.Count} frames into {sheetWidth}x{sheetHeight} sheet.");

        using var bitmap = new SKBitmap(sheetWidth, sheetHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        foreach (var rc in rendered)
        {
            using var src = SKBitmap.Decode(rc.FramePath);
            if (src is null)
            {
                Console.Error.WriteLine($"  [sprite_sheet] Could not decode frame '{rc.Cell.Label}' at {rc.FramePath}, skipping.");
                continue;
            }
            using var resized = src.Resize(new SKImageInfo(p.FrameWidth, p.FrameHeight), SKSamplingOptions.Default);
            canvas.DrawBitmap(resized, rc.Cell.Col * p.FrameWidth, rc.Cell.Row * p.FrameHeight);
        }

        using var skImage = SKImage.FromBitmap(bitmap);
        using var data    = skImage.Encode(SKEncodedImageFormat.Png, 100);

        var sheetPath = Path.GetFullPath(p.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(sheetPath)!);
        await File.WriteAllBytesAsync(sheetPath, data.ToArray());

        Console.Error.WriteLine($"  [sprite_sheet] Wrote sheet: {sheetPath} ({data.Size} bytes)");

        // ── Phase 3b — metadata.json ──
        if (!string.IsNullOrEmpty(p.MetadataPath))
        {
            var metaPath = Path.GetFullPath(p.MetadataPath);
            Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);

            var metaCells = rendered.Select(rc => new
            {
                label        = rc.Cell.Label,
                row          = rc.Cell.Row,
                col          = rc.Cell.Col,
                promptSuffix = rc.Cell.PromptSuffix,
                pngPath      = rc.FramePath,
                isHero       = heroCell is not null && rc.Cell.Label == heroCell.Label
            }).ToArray();

            var metadata = new
            {
                character    = p.CharacterPrompt,
                sdPrompt     = p.Prompt,
                sdNegative   = p.NegativePrompt,
                seed         = p.Seed,
                heroLabel    = heroCell?.Label ?? "",
                sheetPngPath = sheetPath,
                columns      = p.Columns,
                rows         = p.Rows,
                frameWidth   = p.FrameWidth,
                frameHeight  = p.FrameHeight,
                cells        = metaCells
            };

            await File.WriteAllTextAsync(metaPath,
                System.Text.Json.JsonSerializer.Serialize(metadata,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            Console.Error.WriteLine($"  [sprite_sheet] Wrote metadata: {metaPath}");
        }

        Console.WriteLine($"OK: {sheetPath} ({data.Size} bytes, {rendered.Count} frames)");
        return true;
    }
}
