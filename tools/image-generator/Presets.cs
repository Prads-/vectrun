// Six verified art-asset recipes for the game-dev pipeline. The game designer
// picks an art style ("pixel" or "cartoon"); downstream code selects the right
// preset by concatenating `{assetType}_{artStyle}` and sends a minimal request:
//   { "preset": "character_cartoon", "prompt": "<character description>", "outputPath": "..." }
// The preset hard-overrides all recipe params (checkpoint, LoRA, strength,
// sampler, steps, CFG, width, height, upscale, trim). Only `seed` and
// `negativePrompt` are user-overridable — `negativePrompt`, when provided,
// is appended to the preset's default negatives.

using System.Diagnostics.CodeAnalysis;

internal static class Presets
{
    private static readonly HashSet<string> Known =
    [
        "character_pixel",
        "character_cartoon",
        "character_static_pixel",
        "character_static_cartoon",
        "env_sprite_pixel",
        "env_sprite_cartoon",
        "background_pixel",
        "background_cartoon",
        "background_topdown_pixel",
        "background_topdown_cartoon",
        "ui_pixel",
        "ui_cartoon",
    ];

    public static bool IsKnown(string preset) => Known.Contains(preset);

    public static Task<bool> Run(ImageParams u, HttpClient http, string endpoint) => u.Preset switch
    {
        "character_pixel"            => CharacterPixel          (u, http, endpoint),
        "character_cartoon"          => CharacterCartoon        (u, http, endpoint),
        "character_static_pixel"     => CharacterStaticPixel    (u, http, endpoint),
        "character_static_cartoon"   => CharacterStaticCartoon  (u, http, endpoint),
        "env_sprite_pixel"           => EnvSpritePixel          (u, http, endpoint),
        "env_sprite_cartoon"         => EnvSpriteCartoon        (u, http, endpoint),
        "background_pixel"           => BackgroundPixel         (u, http, endpoint),
        "background_cartoon"         => BackgroundCartoon       (u, http, endpoint),
        "background_topdown_pixel"   => BackgroundTopdownPixel  (u, http, endpoint),
        "background_topdown_cartoon" => BackgroundTopdownCartoon(u, http, endpoint),
        "ui_pixel"                   => UiPixel                 (u, http, endpoint),
        "ui_cartoon"                 => UiCartoon               (u, http, endpoint),
        _ => throw new InvalidOperationException($"Unknown preset: {u.Preset}"),
    };

    // ── Recipe 1: character sprite sheet (pixel) ──────────────────────────────
    // Final output gets alpha-from-white so the developer can drop sheets straight
    // into a Canvas / Phaser texture without a runtime color-key step. Do NOT trim
    // background — the 4×4 grid layout must be preserved in pixel coordinates.
    private static Task<bool> CharacterPixel(ImageParams u, HttpClient http, string endpoint)
    {
        var p = BuildCharacterPixel(u, u.OutputPath) with
        {
            AlphaFromWhite = true,
            AlphaThreshold = 40,
        };
        return ComfyUiClient.GenerateTextToImg(p, http, endpoint);
    }

    // ── Recipe 1 → Recipe 4: character sprite sheet (cartoon, two-pass) ───────
    // Pass 1 stays opaque (it's an intermediate that pass 2 reads as input).
    // Pass 2 is the FINAL output, so alpha-from-white is applied there.
    private static async Task<bool> CharacterCartoon(ImageParams u, HttpClient http, string endpoint)
    {
        var temp = IntermediatePath(u.OutputPath);
        var pass1 = BuildCharacterPixel(u, temp);
        if (!await ComfyUiClient.GenerateTextToImg(pass1, http, endpoint)) return false;

        var pass2 = BuildCartoonRestyle(
            u, temp,
            scaffoldPrompt: "cartoon, cartoon style, flat colors, bold outlines, 2d, sprite sheet, multiple views, chibi, from side, looking away, from behind, back",
            scaffoldSuffix: "white background",
            defaultNegative: "pixel art, pixelated, painterly, oil painting, realistic, photorealistic, blurry, text, logo, watermark, ui, (picture frame:1.4), (image border:1.4), (matte border:1.3), framed image, frame around image, decorative border, outlined edges, panel border, sprite sheet border") with
        {
            AlphaFromWhite = true,
            AlphaThreshold = 40,
        };

        var ok = await ComfyUiClient.GenerateImgToImg(pass2, http, endpoint);
        TryDelete(temp);
        return ok;
    }

    // ── Recipe 2: isolated env sprite (pixel + trim) ──────────────────────────
    private static Task<bool> EnvSpritePixel(ImageParams u, HttpClient http, string endpoint) =>
        ComfyUiClient.GenerateTextToImg(BuildEnvSpritePixel(u, u.OutputPath), http, endpoint);

    // ── Recipe 2 → Recipe 4: isolated env sprite (cartoon, two-pass) ──────────
    private static async Task<bool> EnvSpriteCartoon(ImageParams u, HttpClient http, string endpoint)
    {
        var temp = IntermediatePath(u.OutputPath);
        var pass1 = BuildEnvSpritePixel(u, temp) with { AlphaFromWhite = false };
        if (!await ComfyUiClient.GenerateTextToImg(pass1, http, endpoint)) return false;

        var pass2 = BuildCartoonRestyle(
            u, temp,
            scaffoldPrompt: "cartoon, cartoon style, flat colors, bold outlines, 2d, simple background, white background, isolated, centered, solo, no humans, game asset",
            scaffoldSuffix: "",
            defaultNegative: "pixel art, pixelated, painterly, oil painting, realistic, photorealistic, blurry, text, logo, watermark, frame, border, ui, scene, background, room, interior, 1girl, 1boy, character, person, human, face");

        var pass2WithAlpha = pass2 with
        {
            TrimBackground = true,
            TrimPadding    = 8,
            TrimTolerance  = 40,
            AlphaFromWhite = true,
            AlphaThreshold = 40,
        };

        var ok = await ComfyUiClient.GenerateImgToImg(pass2WithAlpha, http, endpoint);
        TryDelete(temp);
        return ok;
    }

    // ── Static character/enemy single sprite (pixel) ──────────────────────────
    // Same character LoRA as the sheet preset, but no multi-view scaffolding.
    // Used for entities that don't need walk frames: stationary enemies (turret,
    // floating head, eye, fixed crystal), non-articulated forms with no clear
    // front/back/sides. Trims to subject + alpha-from-white so the dev gets a
    // single transparent PNG ready to drop in.
    private static Task<bool> CharacterStaticPixel(ImageParams u, HttpClient http, string endpoint) =>
        ComfyUiClient.GenerateTextToImg(BuildCharacterStaticPixel(u, u.OutputPath), http, endpoint);

    // ── Static character/enemy single sprite (cartoon, two-pass) ──────────────
    private static async Task<bool> CharacterStaticCartoon(ImageParams u, HttpClient http, string endpoint)
    {
        var temp = IntermediatePath(u.OutputPath);
        var pass1 = BuildCharacterStaticPixel(u, temp) with
        {
            TrimBackground = false,
            AlphaFromWhite = false,
        };
        if (!await ComfyUiClient.GenerateTextToImg(pass1, http, endpoint)) return false;

        var pass2 = BuildCartoonRestyle(
            u, temp,
            scaffoldPrompt: "cartoon, cartoon style, flat colors, bold outlines, 2d, isolated, centered, solo, single subject, full body, front view",
            scaffoldSuffix: "white background",
            defaultNegative: "pixel art, pixelated, painterly, oil painting, realistic, photorealistic, blurry, text, logo, watermark, ui, sprite sheet, multiple views, from side, looking away, from behind, back, walk cycle, (picture frame:1.4), (image border:1.4), (matte border:1.3), framed image, decorative border, panel border");

        var pass2WithAlpha = pass2 with
        {
            TrimBackground = true,
            TrimPadding    = 8,
            TrimTolerance  = 40,
            AlphaFromWhite = true,
            AlphaThreshold = 40,
        };

        var ok = await ComfyUiClient.GenerateImgToImg(pass2WithAlpha, http, endpoint);
        TryDelete(temp);
        return ok;
    }

    // ── Recipe 5: background (pixel hybrid 70:30, two-pass) ───────────────────
    private static async Task<bool> BackgroundPixel(ImageParams u, HttpClient http, string endpoint)
    {
        var temp = IntermediatePath(u.OutputPath);
        var pass1 = BuildBackgroundPixelPass1(u, temp);
        if (!await ComfyUiClient.GenerateTextToImg(pass1, http, endpoint)) return false;

        var pass2 = BuildBackgroundPixelPass2(u, temp);
        var ok = await ComfyUiClient.GenerateImgToImg(pass2, http, endpoint);
        TryDelete(temp);
        return ok;
    }

    // ── Recipe 3: background (cartoon, single-pass) ───────────────────────────
    private static Task<bool> BackgroundCartoon(ImageParams u, HttpClient http, string endpoint) =>
        ComfyUiClient.GenerateTextToImg(BuildBackgroundCartoon(u, u.OutputPath), http, endpoint);

    // ── Recipe builders ───────────────────────────────────────────────────────

    private static ImageParams BuildCharacterPixel(ImageParams u, string outputPath) => u with
    {
        Prompt             = Wrap("pixel_character_sprite, pxlchrctrsprt, sprite, sprite sheet, sprite art, pixel, (pixel art:1.5), retro game, vibrant colors, pixelated, multiple views, concept art, (chibi:1.5), from side, looking away, from behind, back", u.Prompt, "white background"),
        NegativePrompt     = AppendUserNeg("(picture frame:1.4), (image border:1.4), (matte border:1.3), framed image, frame around image, decorative border, outlined edges, panel border, sprite sheet border", u.NegativePrompt),
        OutputPath         = outputPath,
        Checkpoint         = "prefect_illustrious_xl_v3.fp16.safetensors",
        Lora               = "pixel_character_sprite_illustrious.safetensors",
        LoraStrength       = 0.7,
        ClipSkip           = 2,
        Sampler            = "euler_ancestral",
        Scheduler          = "normal",
        Steps              = 100,
        Cfg                = 5.0,
        Width              = 1024,
        Height             = 1024,
        UpscaleBy          = 2.0,
        UpscaleModel       = "RealESRGAN_x4plus.pth",
        UpscaleDenoise     = 0.15,
        UpscaleTileWidth   = 1024,
        UpscaleTileHeight  = 1024,
        UpscaleMaskBlur    = 8,
        UpscalePadding     = 32,
        TrimBackground     = false,
        Type               = "",
    };

    private static ImageParams BuildCharacterStaticPixel(ImageParams u, string outputPath) => u with
    {
        Prompt             = Wrap("pixel_character_sprite, pxlchrctrsprt, sprite, sprite art, pixel, (pixel art:1.5), retro game, vibrant colors, pixelated, isolated, centered, solo, single subject, full body, front view", u.Prompt, "(pure white background:1.4), flat white background, no shadow, no environment"),
        NegativePrompt     = AppendUserNeg("sprite sheet, multiple views, grid, tiled, from side, looking away, from behind, back, walk cycle, scene, background, room, interior, floor, shadow, vignette, gradient, lighting, (picture frame:1.4), (image border:1.4), (matte border:1.3), framed image, frame around image, decorative border, outlined edges, panel border, sprite sheet border", u.NegativePrompt),
        OutputPath         = outputPath,
        Checkpoint         = "prefect_illustrious_xl_v3.fp16.safetensors",
        Lora               = "pixel_character_sprite_illustrious.safetensors",
        LoraStrength       = 0.7,
        ClipSkip           = 2,
        Sampler            = "euler_ancestral",
        Scheduler          = "normal",
        Steps              = 100,
        Cfg                = 5.0,
        Width              = 1024,
        Height             = 1024,
        UpscaleBy          = 2.0,
        UpscaleModel       = "RealESRGAN_x4plus.pth",
        UpscaleDenoise     = 0.15,
        UpscaleTileWidth   = 1024,
        UpscaleTileHeight  = 1024,
        UpscaleMaskBlur    = 8,
        UpscalePadding     = 32,
        TrimBackground     = true,
        TrimPadding        = 8,
        TrimTolerance      = 40,
        AlphaFromWhite     = true,
        AlphaThreshold     = 40,
        Type               = "",
    };

    private static ImageParams BuildEnvSpritePixel(ImageParams u, string outputPath) => u with
    {
        Prompt             = Wrap("", u.Prompt, "(pure white background:1.4), flat white background, simple background, isolated, centered, solo, no humans, no floor, no ground, no shadow, no vignette, no environment"),
        NegativePrompt     = AppendUserNeg("sprite sheet, multiple views, grid, tiled, scene, background, room, interior, floor, wooden floor, ground, shadow, vignette, gradient, lighting, sunlight, indoor, outdoor, 1girl, 1boy, character, person, human, face", u.NegativePrompt),
        OutputPath         = outputPath,
        Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
        Lora               = "pixel-art-xl-v1.1.safetensors",
        LoraStrength       = 1.5,
        ClipSkip           = 0,
        Sampler            = "dpmpp_sde",
        Scheduler          = "karras",
        Steps              = 8,
        Cfg                = 2.0,
        Width              = 1024,
        Height             = 1024,
        UpscaleBy          = 2.0,
        UpscaleModel       = "RealESRGAN_x4plus.pth",
        UpscaleDenoise     = 0.15,
        UpscaleTileWidth   = 1024,
        UpscaleTileHeight  = 1024,
        UpscaleMaskBlur    = 8,
        UpscalePadding     = 32,
        TrimBackground     = true,
        TrimPadding        = 8,
        TrimTolerance      = 40,
        AlphaFromWhite     = true,
        AlphaThreshold     = 40,
        Type               = "",
    };

    // Recipe 5 pass 1: fully-pixelated widescreen forest-style background.
    private static ImageParams BuildBackgroundPixelPass1(ImageParams u, string outputPath) => u with
    {
        Prompt             = Wrap("", u.Prompt, "cartoon, cartoon style, flat colors, bold outlines, 2d, game background"),
        NegativePrompt     = AppendUserNeg("painterly, oil painting, realistic, photorealistic, detailed brush strokes, soft gradient, character, person, human, face, 1girl, 1boy, text, logo, watermark, frame, border, ui", u.NegativePrompt),
        OutputPath         = outputPath,
        Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
        Lora               = "pixel-art-xl-v1.1.safetensors",
        LoraStrength       = 1.5,
        ClipSkip           = 0,
        Sampler            = "dpmpp_sde",
        Scheduler          = "karras",
        Steps              = 8,
        Cfg                = 2.0,
        Width              = 1344,
        Height             = 768,
        UpscaleBy          = 0,
        TrimBackground     = false,
        Type               = "",
    };

    // Recipe 5 pass 2: img2img @ 0.4 denoise, no LoRA, to pull ~70:30 cartoon:pixel.
    private static ImageParams BuildBackgroundPixelPass2(ImageParams u, string referencePath) => u with
    {
        Prompt             = Wrap("", u.Prompt, "cartoon, cartoon style, flat colors, bold outlines, 2d, game background"),
        NegativePrompt     = AppendUserNeg("painterly, oil painting, realistic, photorealistic, detailed brush strokes, soft gradient, character, person, human, face, 1girl, 1boy, text, logo, watermark, frame, border, ui", u.NegativePrompt),
        OutputPath         = u.OutputPath,
        ReferenceImage     = referencePath,
        ImgToImgDenoise    = 0.45,
        Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
        Lora               = "",
        LoraStrength       = 0,
        ClipSkip           = 0,
        Sampler            = "dpmpp_sde",
        Scheduler          = "karras",
        Steps              = 8,
        Cfg                = 2.0,
        Width              = 1344,
        Height             = 768,
        UpscaleBy          = 0,
        TrimBackground     = false,
        Type               = "",
    };

    private static ImageParams BuildBackgroundCartoon(ImageParams u, string outputPath) => u with
    {
        Prompt             = Wrap("", u.Prompt, "cartoon, cartoon style, flat colors, bold outlines, 2d, game background"),
        NegativePrompt     = AppendUserNeg("painterly, oil painting, realistic, photorealistic, detailed brush strokes, soft gradient, character, person, human, face, 1girl, 1boy, text, logo, watermark, frame, border, ui", u.NegativePrompt),
        OutputPath         = outputPath,
        Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
        Lora               = "",
        LoraStrength       = 0,
        ClipSkip           = 0,
        Sampler            = "dpmpp_sde",
        Scheduler          = "karras",
        Steps              = 8,
        Cfg                = 2.0,
        Width              = 1344,
        Height             = 768,
        UpscaleBy          = 0,
        TrimBackground     = false,
        Type               = "",
    };

    // Cartoon restyle pass (Recipe 4): img2img @ 0.6 with no LoRA on DreamshaperXL.
    // Used by character_cartoon and env_sprite_cartoon to repaint a pixel source.
    private static ImageParams BuildCartoonRestyle(
        ImageParams u, string referencePath, string scaffoldPrompt, string scaffoldSuffix, string defaultNegative) => u with
    {
        Prompt          = Wrap(scaffoldPrompt, u.Prompt, scaffoldSuffix),
        NegativePrompt  = AppendUserNeg(defaultNegative, u.NegativePrompt),
        OutputPath      = u.OutputPath,
        ReferenceImage  = referencePath,
        ImgToImgDenoise = 0.6,
        Checkpoint      = "dreamshaperXL_lightningDPMSDE.safetensors",
        Lora            = "",
        LoraStrength    = 0,
        ClipSkip        = 0,
        Sampler         = "dpmpp_sde",
        Scheduler       = "karras",
        Steps           = 8,
        Cfg             = 2.0,
        UpscaleBy       = 0,
        TrimBackground  = false,
        Type            = "",
    };

    // ── Top-down background ───────────────────────────────────────────────────
    //
    // Single square image viewed from above: the level's ground surface with
    // terrain detail (grass + path, cobblestone, sand, etc.). Designer layers
    // env sprites on top in the engine.

    private static async Task<bool> BackgroundTopdownPixel(ImageParams u, HttpClient http, string endpoint)
    {
        var (scaffold, negative) = TopdownScaffold(u.Perspective);
        var temp = IntermediatePath(u.OutputPath);

        var pass1 = u with
        {
            Prompt             = Wrap("", u.Prompt, scaffold),
            NegativePrompt     = AppendUserNeg(negative, u.NegativePrompt),
            OutputPath         = temp,
            Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
            Lora               = "pixel-art-xl-v1.1.safetensors",
            LoraStrength       = 1.5,
            ClipSkip           = 0,
            Sampler            = "dpmpp_sde",
            Scheduler          = "karras",
            Steps              = 8,
            Cfg                = 2.0,
            Width              = 1024,
            Height             = 1024,
            UpscaleBy          = 0,
            TrimBackground     = false,
            Type               = "",
        };
        if (!await ComfyUiClient.GenerateTextToImg(pass1, http, endpoint)) return false;

        var pass2 = u with
        {
            Prompt             = Wrap("", u.Prompt, scaffold),
            NegativePrompt     = AppendUserNeg(negative, u.NegativePrompt),
            OutputPath         = u.OutputPath,
            ReferenceImage     = temp,
            ImgToImgDenoise    = 0.45,
            Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
            Lora               = "",
            LoraStrength       = 0,
            ClipSkip           = 0,
            Sampler            = "dpmpp_sde",
            Scheduler          = "karras",
            Steps              = 8,
            Cfg                = 2.0,
            Width              = 1024,
            Height             = 1024,
            UpscaleBy          = 0,
            TrimBackground     = false,
            Type               = "",
        };
        var ok = await ComfyUiClient.GenerateImgToImg(pass2, http, endpoint);
        TryDelete(temp);
        return ok;
    }

    private static Task<bool> BackgroundTopdownCartoon(ImageParams u, HttpClient http, string endpoint)
    {
        var (scaffold, negative) = TopdownScaffold(u.Perspective);

        var p = u with
        {
            Prompt             = Wrap("", u.Prompt, scaffold),
            NegativePrompt     = AppendUserNeg(negative, u.NegativePrompt),
            OutputPath         = u.OutputPath,
            Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
            Lora               = "",
            LoraStrength       = 0,
            ClipSkip           = 0,
            Sampler            = "dpmpp_sde",
            Scheduler          = "karras",
            Steps              = 8,
            Cfg                = 2.0,
            Width              = 1024,
            Height             = 1024,
            UpscaleBy          = 0,
            TrimBackground     = false,
            Type               = "",
        };
        return ComfyUiClient.GenerateTextToImg(p, http, endpoint);
    }

    // ── UI assets (icons, buttons, panels) ────────────────────────────────────
    //
    // Isolated UI element on a white canvas, trimmed to the subject's bounding
    // box, then alpha-from-white so the PNG has a transparent background ready
    // to composite over the game. Similar shape to env_sprite but with
    // UI-specific scaffolding and alpha conversion always on.

    private static Task<bool> UiPixel(ImageParams u, HttpClient http, string endpoint) =>
        ComfyUiClient.GenerateTextToImg(BuildUiPixel(u, u.OutputPath), http, endpoint);

    private static async Task<bool> UiCartoon(ImageParams u, HttpClient http, string endpoint)
    {
        var temp = IntermediatePath(u.OutputPath);
        var pass1 = BuildUiPixel(u, temp);
        if (!await ComfyUiClient.GenerateTextToImg(pass1, http, endpoint)) return false;

        var pass2 = BuildCartoonRestyle(
            u, temp,
            scaffoldPrompt: "cartoon, cartoon style, flat colors, bold outlines, 2d, UI element, game icon, clean vector style",
            scaffoldSuffix: "",
            defaultNegative: "pixel art, pixelated, painterly, oil painting, realistic, photorealistic, blurry, text, logo, watermark, frame, border, scene, background, room, 1girl, 1boy, character, person, human, face");

        var pass2WithAlpha = pass2 with
        {
            TrimBackground = true,
            TrimPadding    = 8,
            TrimTolerance  = 25,
            AlphaFromWhite = true,
            AlphaThreshold = 15,
        };

        var ok = await ComfyUiClient.GenerateImgToImg(pass2WithAlpha, http, endpoint);
        TryDelete(temp);
        return ok;
    }

    private static ImageParams BuildUiPixel(ImageParams u, string outputPath) => u with
    {
        Prompt             = Wrap("", u.Prompt, "UI element, game icon, (pure white background:1.4), flat white background, isolated, centered, solo, no humans, no environment, clean silhouette, bold outlines, readable at small size"),
        NegativePrompt     = AppendUserNeg("sprite sheet, multiple views, grid, tiled, scene, background, room, interior, floor, shadow, vignette, gradient, lighting, character, person, human, 1girl, 1boy, face", u.NegativePrompt),
        OutputPath         = outputPath,
        Checkpoint         = "dreamshaperXL_lightningDPMSDE.safetensors",
        Lora               = "pixel-art-xl-v1.1.safetensors",
        LoraStrength       = 1.5,
        ClipSkip           = 0,
        Sampler            = "dpmpp_sde",
        Scheduler          = "karras",
        Steps              = 8,
        Cfg                = 2.0,
        Width              = 1024,
        Height             = 1024,
        UpscaleBy          = 2.0,
        UpscaleModel       = "RealESRGAN_x4plus.pth",
        UpscaleDenoise     = 0.15,
        UpscaleTileWidth   = 1024,
        UpscaleTileHeight  = 1024,
        UpscaleMaskBlur    = 8,
        UpscalePadding     = 32,
        TrimBackground     = true,
        TrimPadding        = 8,
        TrimTolerance      = 25,
        AlphaFromWhite     = true,
        AlphaThreshold     = 15,
        Type               = "",
    };

    // Perspective selects the camera height scaffolding for topdown backgrounds.
    // - "aerial"       → high camera, strategy-map feel (Age of Empires)
    // - "ground_level" → low camera, classic RPG feel (Zelda, Stardew, Pokemon)
    // Default is ground_level since that's the more common top-down game style.
    //
    // Scaffolding is aggressively minimalist — the goal is a seamless repeating
    // ground texture (grass, cobblestone, sand), NOT a scene. Any place-noun or
    // landmark makes SD draw a composed landscape; the negatives push hard
    // against "plaza/square/beach/clearing/landscape/scenery" and any solid
    // objects. Designers should prompt with material words only: "grass",
    // "cobblestone", "sand", "dirt path", NOT "grass field" or "beach".
    private static (string Scaffold, string Negative) TopdownScaffold(string perspective)
    {
        const string commonScaffold = "seamless repeating tileable texture, uniform ground material, minimal detail, flat surface, texture swatch, no landmarks, no focal point, no composition, cartoon, cartoon style, flat colors, 2d, game ground";
        const string commonNegative = "plaza, square, courtyard, beach, clearing, scene, landscape, scenery, landmark, building, structure, tree, bush, (large rocks:1.4), (boulders:1.4), (rock sprites:1.4), (rock formations:1.4), (distinct rocks:1.3), stone clumps, furniture, decoration objects, signage, path, trail, road, character, person, human, 1girl, 1boy, side view, horizon, sky, clouds, 3d objects, walls, raised elements, painterly, oil painting, realistic, photorealistic, detailed brush strokes, soft gradient, text, logo, watermark, frame, border, ui";

        return perspective == "aerial"
            ? ($"top-down texture, bird's eye view, flat material surface, {commonScaffold}",
               $"ground-level view, low angle, zoomed in, {commonNegative}")
            : ($"top-down texture, close-up ground material, JRPG floor texture, {commonScaffold}",
               $"bird's eye view, aerial view, satellite view, high altitude, zoomed out, wide area, {commonNegative}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Wrap(string prefix, string user, string suffix)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(prefix)) parts.Add(prefix);
        if (!string.IsNullOrWhiteSpace(user))   parts.Add(user);
        if (!string.IsNullOrWhiteSpace(suffix)) parts.Add(suffix);
        return string.Join(", ", parts);
    }

    private static string AppendUserNeg(string preset, string user)
    {
        if (string.IsNullOrWhiteSpace(preset)) return user ?? "";
        if (string.IsNullOrWhiteSpace(user))   return preset;
        return $"{preset}, {user}";
    }

    // Temp path for the pixel intermediate of a two-pass preset. We sit it next
    // to the final outputPath so the caller's directory stays self-contained.
    private static string IntermediatePath(string outputPath)
    {
        var dir  = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? "";
        var stem = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(dir, $".{stem}.pass1.png");
    }

    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup.")]
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
