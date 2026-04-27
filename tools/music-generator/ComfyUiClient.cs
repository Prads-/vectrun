using System.Text;
using System.Text.Json;

internal static class ComfyUiClient
{
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(10);

    // Hard-coded ACE-Step v1.5 model filenames. ACE-Step v1.5 requires TWO
    // text encoders loaded via DualCLIPLoader; using only one produces a cond
    // with pooled_output=None which crashes the sampler. Both files must live
    // under ComfyUI/models/text_encoders/.
    private const string AceUnetName  = "acestep_v1.5_turbo.safetensors";
    private const string AceClipName1 = "qwen_0.6b_ace15.safetensors";
    private const string AceClipName2 = "qwen_4b_ace15.safetensors";
    private const string AceVaeName   = "ace_1.5_vae.safetensors";

    internal static async Task<bool> GenerateAceStepMp3(TrackParams t, HttpClient http, string endpoint)
    {
        // Mirrors ComfyUI's canonical "Text to Audio (ACE-Step 1.5)" blueprint
        // (resources/ComfyUI/blueprints/Text to Audio (ACE-Step 1.5).json).
        // Two non-obvious nodes are mandatory:
        //   - ModelSamplingAuraFlow patches the UNet for ACE-Step's sampling
        //     format. Without it, KSampler fails inside conditioning processing
        //     with "'NoneType' object has no attribute 'shape'".
        //   - ConditioningZeroOut produces a well-shaped negative conditioning
        //     by zeroing the positive cond's tensors. Using a second
        //     TextEncodeAceStepAudio1.5 with empty tags ALSO triggers the
        //     same NoneType bug; ConditioningZeroOut sidesteps it.
        var workflow = new Dictionary<string, object>
        {
            ["1"] = new { class_type = "UNETLoader", inputs = new
            {
                unet_name    = AceUnetName,
                weight_dtype = "default"
            }},
            ["2"] = new { class_type = "ModelSamplingAuraFlow", inputs = new
            {
                model = new object[] { "1", 0 },
                shift = 3.0
            }},
            ["3"] = new { class_type = "DualCLIPLoader", inputs = new
            {
                clip_name1 = AceClipName1,
                clip_name2 = AceClipName2,
                type       = "ace"
            }},
            ["4"] = new { class_type = "VAELoader", inputs = new
            {
                vae_name = AceVaeName
            }},
            ["5"] = new { class_type = "EmptyAceStep1.5LatentAudio", inputs = new
            {
                seconds    = t.Duration,
                batch_size = 1
            }},
            ["6"] = new { class_type = "TextEncodeAceStepAudio1.5", inputs = new
            {
                clip                 = new object[] { "3", 0 },
                tags                 = t.Prompt,
                lyrics               = t.Lyrics,
                seed                 = t.Seed,
                bpm                  = t.Bpm,
                duration             = t.Duration,
                timesignature        = "4",
                language             = t.Language,
                keyscale             = "C major",
                generate_audio_codes = true,
                cfg_scale            = 2.0,
                temperature          = 0.85,
                top_p                = 0.9,
                top_k                = 0,
                min_p                = 0.0
            }},
            ["7"] = new { class_type = "ConditioningZeroOut", inputs = new
            {
                conditioning = new object[] { "6", 0 }
            }},
            ["8"] = new { class_type = "KSampler", inputs = new
            {
                model         = new object[] { "2", 0 },
                positive      = new object[] { "6", 0 },
                negative      = new object[] { "7", 0 },
                latent_image  = new object[] { "5", 0 },
                seed          = t.Seed,
                steps         = t.Steps,
                cfg           = t.Cfg,
                sampler_name  = "euler",
                scheduler     = "simple",
                denoise       = 1.0
            }},
            ["9"] = new { class_type = "VAEDecodeAudio", inputs = new
            {
                samples = new object[] { "8", 0 },
                vae     = new object[] { "4", 0 }
            }},
            ["10"] = new { class_type = "SaveAudioMP3", inputs = new
            {
                audio           = new object[] { "9", 0 },
                filename_prefix = "vectrun-music/track",
                quality         = t.Quality
            }}
        };

        return await QueueAndSave(workflow, t, http, endpoint);
    }

    // Output node id where SaveAudioMP3 lives (must match the workflow above).
    private const string SaveNodeId = "10";

    internal static async Task FreeVram(HttpClient http, string endpoint)
    {
        var body = JsonSerializer.Serialize(new { unload_models = true, free_memory = true });
        await http.PostAsync($"{endpoint}/free", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static async Task<bool> QueueAndSave(Dictionary<string, object> workflow, TrackParams t, HttpClient http, string endpoint)
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
            if (delay > TimeSpan.Zero) await Task.Delay(delay);

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

            // SaveAudioMP3 emits its results under outputs[SaveNodeId]. The exact
            // array key (audio / clips / mp3) varies between ComfyUI versions; probe
            // for any property whose first element has a 'filename' field.
            if (outputs.TryGetProperty(SaveNodeId, out var saveNode))
            {
                if (TryGetSavedFile(saveNode, out _))
                    break;
            }
        }

        if (pollTimer.Elapsed >= PromptTimeout)
        {
            Console.Error.WriteLine($"  Timed out waiting for ComfyUI prompt after {PromptTimeout.TotalMinutes:0} minutes: {promptId}");
            return false;
        }

        if (!outputs.TryGetProperty(SaveNodeId, out var node) || !TryGetSavedFile(node, out var saved))
        {
            Console.Error.WriteLine("  ComfyUI completed but produced no audio output.");
            return false;
        }

        var viewUrl = $"{endpoint}/view?filename={Uri.EscapeDataString(saved.Filename)}&subfolder={Uri.EscapeDataString(saved.Subfolder)}&type={Uri.EscapeDataString(saved.Type)}";

        byte[] audioBytes;
        try   { audioBytes = await http.GetByteArrayAsync(viewUrl); }
        catch (Exception ex) { Console.Error.WriteLine($"  Failed to download {viewUrl}: {ex.Message}"); return false; }

        var outputPath = Path.GetFullPath(t.OutputPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, audioBytes);
        }
        catch (Exception ex) { Console.Error.WriteLine($"  Failed to save: {ex.Message}"); return false; }

        Console.WriteLine($"OK: {outputPath} ({audioBytes.Length} bytes)");
        return true;
    }

    private record SavedFile(string Filename, string Subfolder, string Type);

    private static bool TryGetSavedFile(JsonElement saveNode, out SavedFile saved)
    {
        saved = new SavedFile("", "", "output");
        if (saveNode.ValueKind != JsonValueKind.Object) return false;

        foreach (var prop in saveNode.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            if (prop.Value.GetArrayLength() == 0) continue;
            var first = prop.Value[0];
            if (first.ValueKind != JsonValueKind.Object) continue;
            if (!first.TryGetProperty("filename", out var fn) || fn.ValueKind != JsonValueKind.String) continue;

            saved = new SavedFile(
                Filename:  fn.GetString() ?? "",
                Subfolder: first.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "",
                Type:      first.TryGetProperty("type",      out var tp) ? tp.GetString() ?? "output" : "output"
            );
            return !string.IsNullOrEmpty(saved.Filename);
        }
        return false;
    }
}
