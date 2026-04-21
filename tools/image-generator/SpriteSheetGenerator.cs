using SkiaSharp;

internal static class SpriteSheetGenerator
{
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
        var tempFiles = new List<string>();

        try
        {
            // Phase 1 — canonical sprite (text2img)
            var canonicalPath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
            tempFiles.Add(canonicalPath);

            Console.Error.WriteLine($"  [sprite_sheet] Generating canonical sprite...");

            var canonicalParams = p with
            {
                Prompt     = p.CharacterPrompt,
                OutputPath = canonicalPath
                // Width/Height intentionally not overridden — generate at full model resolution
            };

            if (!await ComfyUiClient.GenerateTextToImg(canonicalParams, http, endpoint))
                return false;

            // Phase 2 — one generation per cell, each with its own pose prompt
            var frames = new List<(int Row, int Col, string TempPath)>();

            foreach (var cell in p.Animations)
            {
                if (cell.Row >= p.Rows || cell.Col >= p.Columns)
                {
                    Console.Error.WriteLine($"  [sprite_sheet] Warning: cell ({cell.Row},{cell.Col}) out of bounds ({p.Rows}x{p.Columns}), skipping.");
                    continue;
                }

                var framePath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
                tempFiles.Add(framePath);

                Console.Error.WriteLine($"  [sprite_sheet] Cell ({cell.Row},{cell.Col}): {cell.Prompt}");

                var frameParams = p with
                {
                    Prompt         = $"{p.CharacterPrompt}, {cell.Prompt}",
                    OutputPath     = framePath,
                    Seed           = p.Seed + frames.Count,  // vary seed per cell for pose diversity
                    ReferenceImage = canonicalPath
                };

                if (!await ComfyUiClient.GenerateWithIPAdapter(frameParams, http, endpoint))
                    return false;

                frames.Add((cell.Row, cell.Col, framePath));
            }

            // Phase 3 — composite with SkiaSharp
            Console.Error.WriteLine($"  [sprite_sheet] Compositing {frames.Count} frames...");

            var sheetWidth  = p.Columns * p.FrameWidth;
            var sheetHeight = p.Rows    * p.FrameHeight;

            using var bitmap = new SKBitmap(sheetWidth, sheetHeight);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            foreach (var (row, col, framePath) in frames)
            {
                using var frameBitmap = SKBitmap.Decode(framePath);
                if (frameBitmap is null)
                {
                    Console.Error.WriteLine($"  [sprite_sheet] Warning: could not decode frame ({row},{col}), skipping.");
                    continue;
                }
                using var resized = frameBitmap.Resize(new SKImageInfo(p.FrameWidth, p.FrameHeight), SKSamplingOptions.Default);
                canvas.DrawBitmap(resized, col * p.FrameWidth, row * p.FrameHeight);
            }

            using var skImage = SKImage.FromBitmap(bitmap);
            using var data    = skImage.Encode(SKEncodedImageFormat.Png, 100);

            var outputPath = Path.GetFullPath(p.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, data.ToArray());

            Console.WriteLine($"OK: {outputPath} ({data.Size} bytes, {frames.Count} frames)");
            return true;
        }
        finally
        {
            foreach (var temp in tempFiles)
                try { File.Delete(temp); } catch { /* best-effort */ }
        }
    }
}
