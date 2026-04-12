using SkiaSharp;

internal static class SpriteSheetGenerator
{
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

            // Phase 2 — animation frames (img2img, same seed as canonical)
            var frames = new List<(int Row, int Col, string TempPath)>();

            foreach (var anim in p.Animations)
            {
                if (anim.Row >= p.Rows)
                {
                    Console.Error.WriteLine($"  [sprite_sheet] Warning: row {anim.Row} >= rows {p.Rows}, skipping '{anim.Prompt}'.");
                    continue;
                }

                for (var col = 0; col < anim.FrameCount; col++)
                {
                    if (col >= p.Columns)
                    {
                        Console.Error.WriteLine($"  [sprite_sheet] Warning: col {col} >= columns {p.Columns}, truncating '{anim.Prompt}'.");
                        break;
                    }

                    var framePath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
                    tempFiles.Add(framePath);

                    Console.Error.WriteLine($"  [sprite_sheet] Frame row={anim.Row} col={col}: {anim.Prompt}");

                    var frameParams = p with
                    {
                        Prompt     = $"{p.CharacterPrompt}, {anim.Prompt}",
                        OutputPath = framePath
                        // Width/Height intentionally not overridden — generate at full model resolution
                        // Seed intentionally not overridden — same seed as canonical for consistency
                    };

                    if (!await ComfyUiClient.GenerateImgToImg(frameParams, canonicalPath, http, endpoint))
                        return false;

                    frames.Add((anim.Row, col, framePath));
                }
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
