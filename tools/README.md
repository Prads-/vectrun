# Tools

Standalone .NET 9 executables used as tools within vectrun pipelines. Each tool reads its arguments as a JSON object from **stdin** when invoked by the pipeline, and also accepts traditional command-line arguments for direct CLI use.

## Building

Build all tools from the repo root:

```bash
dotnet build tools/web-scraper/web-scraper.csproj         -c Release
dotnet build tools/kv-store/kv-store.csproj               -c Release
dotnet build tools/scaffold-claude/scaffold-claude.csproj  -c Release
dotnet build tools/file-manager/file-manager.csproj        -c Release
dotnet build tools/image-generator/image-generator.csproj  -c Release
dotnet build tools/music-generator/music-generator.csproj  -c Release
dotnet build tools/ollama-stop/ollama-stop.csproj          -c Release
dotnet build tools/svg-rasterize/svg-rasterize.csproj      -c Release
```

Binaries are written to each tool's `bin/Release/net9.0/` directory.

---

## web-scraper

Fetches the full HTML content of a web page using a headless Chromium browser (Playwright). Waits for the page `load` event, then attempts a further 25-second network-idle wait (best-effort) so JavaScript-rendered content is included without hanging on analytics-heavy sites.

### CLI

```bash
web-scraper <url> [--extract-text]
```

### Pipeline (stdin JSON)

```json
{ "url": "https://store.steampowered.com/charts/topselling/", "extractText": true }
```

| Field         | Required | Default | Description                                                   |
|---------------|----------|---------|---------------------------------------------------------------|
| `url`         | Yes      | —       | The page URL to fetch                                         |
| `extractText` | No       | `false` | When `true`, returns visible text only (strips HTML/CSS/JS)   |

### Output

When `extractText` is omitted or `false`: the full raw HTML of the page.

When `extractText` is `true`: the visible rendered text of the page body, with all HTML tags, `<script>`, and `<style>` blocks removed. Equivalent to `document.body.innerText` in the browser.

### Notes

- Chromium is installed automatically as a post-build step.
- Timeout is 30 seconds per page.
- Bare URLs without a scheme are normalised to `https://`.

---

## kv-store

A simple file-backed key-value store. Data is persisted in a `data/` directory next to the executable, organised by namespace, with keys stored as SHA-256-hashed filenames. Safe for concurrent reads; writes are exclusive.

### CLI

```bash
kv-store <read|write|update|delete|delete_prefix|append> <namespace> <key> [value]
```

### Pipeline (stdin JSON)

```json
{ "operation": "write", "namespace": "game_dev", "key": "research_report", "value": "..." }
```

| Field       | Required                  | Description                                                                 |
|-------------|---------------------------|-----------------------------------------------------------------------------|
| `operation` | Yes                       | `read`, `write`, `update`, `delete`, `delete_prefix`, or `append`           |
| `namespace` | Yes                       | Groups related keys (e.g. `game_dev`)                                       |
| `key`       | Yes                       | Arbitrary string key (or prefix, for `delete_prefix`)                       |
| `value`     | write/update/append only  | The value to store                                                          |
| `separator` | append only (optional)    | String inserted between existing and new value (default `"\n\n---\n\n"`)    |

### Operations

| Operation       | Behaviour                                                                                          |
|-----------------|----------------------------------------------------------------------------------------------------|
| `write`         | Creates or overwrites a key (upsert).                                                              |
| `read`          | Returns the stored value. Returns empty string (exit 0) if key does not exist.                     |
| `update`        | Creates or overwrites a key (upsert). Identical to `write`.                                        |
| `delete`        | Removes the key. Succeeds silently if the key does not exist.                                      |
| `append`        | Appends `value` to the existing entry with a separator between them. Creates the key if absent.    |
| `delete_prefix` | Deletes every key in the namespace whose name starts with the given prefix. Prints count deleted.  |

### Output

`OK` on success for write/update/append/delete. The number of deleted entries (plain integer) for `delete_prefix`. The stored value on read.

### Storage layout

For each key, two files are written into `data/<namespace>/`:

- `<sha256-of-key>` — the value file (filename is a safe hex hash of the key).
- `<sha256-of-key>.key` — a plain-text sidecar containing the original key string.

The sidecar is what makes `delete_prefix` possible — it lets the tool recover the original key from the hashed filename. The sidecar is written **before** the value file, so a mid-write crash leaves an orphan sidecar that the next `delete_prefix` call sweeps up automatically.

---

## scaffold-claude

Creates a project directory, writes a `CLAUDE.md` requirements file, and invokes Claude Code (`claude --dangerously-skip-permissions`) to implement the project from those requirements. Claude's output is streamed to stdout.

**Requires:** `claude` CLI installed and available in PATH.

### CLI

```bash
scaffold-claude <project-directory>
# Requirements are read as plain text from stdin
echo "Build a platformer game in Python" | scaffold-claude C:/Games/my-game
```

### Pipeline (stdin JSON)

```json
{
  "projectDirectory": "C:/Games/my-game",
  "requirements": "Build a 2D platformer in Python using Pygame. The player can run and jump..."
}
```

| Field              | Required | Description                                                  |
|--------------------|----------|--------------------------------------------------------------|
| `projectDirectory` | Yes      | Absolute path where the project will be created              |
| `requirements`     | Yes      | Full requirements text written into `CLAUDE.md`              |

### Output

Claude Code's stdout output, followed by the project directory path.

---

## file-manager

Performs file system operations: create directories, write text or binary files, read text files, and list directory contents. Intermediate directories are created automatically on write operations.

### Pipeline (stdin JSON)

```json
{ "operation": "write_text", "path": "C:/Games/my-game/assets/manifest.json", "content": "{...}" }
```

| Field           | Required              | Description                                         |
|-----------------|-----------------------|-----------------------------------------------------|
| `operation`     | Yes                   | See operations table below                          |
| `path`          | Yes                   | Absolute path to the file or directory              |
| `content`       | `write_text` only     | UTF-8 text content to write                         |
| `contentBase64` | `write_binary` only   | Base64-encoded binary content to write              |

### Operations

| Operation          | Description                                                       |
|--------------------|-------------------------------------------------------------------|
| `create_directory` | Creates the directory and any missing parents                     |
| `write_text`       | Writes a UTF-8 text file, creating parent directories as needed   |
| `write_binary`     | Writes a binary file from a base64 string                         |
| `read_text`        | Reads and returns the contents of a text file                     |
| `list_directory`   | Returns a JSON array of `{name, type, size}` entries              |

### Output

- `OK: <path>` on successful write or create.
- File contents on read.
- JSON array on list.

---

## image-generator

Generates images via a locally running **ComfyUI** instance. Supports single images, img2img, ControlNet, game-asset presets, composite sheets, and sprite sheets rendered from per-cell pose images.

**Requires:** ComfyUI running locally (default `http://localhost:8188`). Queued prompts time out after 10 minutes.

### CLI

```bash
image-generator <file.json>          # generate from JSON config
image-generator probe <png>          # print alpha histogram + sample opaque pixels
image-generator alpha <in> <out> [tolerance]  # flood-fill bg removal (no ComfyUI needed)
image-generator harden <in> <out>    # post-process an ML-segmented PNG: snap interior
                                     # body to opaque, fill holes with median body colour
```

The default `image-generator <file.json>` mode also accepts JSON via stdin.

### Game asset presets

```json
{
  "preset": "character_static_cartoon",
  "prompt": "creature, glowing crystal slime, teal and cyan, no face, amorphous",
  "outputPath": "C:/Games/my-game/assets/slime.png",
  "seed": 42
}
```

Known presets:

| Preset                       | Use case                                                              |
|------------------------------|-----------------------------------------------------------------------|
| `character_pixel`            | Sprite-sheet character (humans, humanoids, animals — has walk cycle)  |
| `character_cartoon`          | Sprite-sheet character, cartoon style                                 |
| `character_static_pixel`     | Single-sprite character (turrets, floating heads — no walk cycle)     |
| `character_static_cartoon`   | Single-sprite character, cartoon style                                |
| `env_sprite_pixel`           | Props, items, hazards (chests, crates, etc.)                          |
| `env_sprite_cartoon`         | Props, items, hazards, cartoon style                                  |
| `background_pixel`           | Side-scrolling level background                                       |
| `background_cartoon`         | Side-scrolling level background, cartoon style                        |
| `background_topdown_pixel`   | Top-down level background                                             |
| `background_topdown_cartoon` | Top-down level background, cartoon style                              |
| `ui_pixel`                   | UI panels, icons, HUD elements                                        |
| `ui_cartoon`                 | UI panels, icons, HUD elements, cartoon style                         |

All four `character_*` presets use ML foreground segmentation (ComfyUI-RMBG node, `INSPYRENET` model by default) for background removal — works regardless of body / background colour overlap. `env_sprite_*` and `ui_*` presets use the simpler edge-connected flood-fill (`alphaFromWhite`). Presets own the checkpoint, LoRA, sampler, dimensions, upscale, trim, and alpha settings; callers should normally provide only `preset`, `prompt`, `outputPath`, and optionally `seed` or `negativePrompt`.

### Environment variables

| Variable             | Default                                        | Description                                           |
|----------------------|------------------------------------------------|-------------------------------------------------------|
| `COMFYUI_ENDPOINT`   | `http://localhost:8188`                        | Base URL of the ComfyUI instance                      |
| `COMFYUI_CHECKPOINT` | `v1-5-pruned-emaonly.safetensors`              | Default checkpoint filename                           |
| `CONTROLNET_MODEL`   | `control-lora-canny-rank256.safetensors`      | ControlNet model filename used by ControlNet and sprite-sheet modes |

Upscaling (`upscaleBy > 0`, including several presets) requires the `UltimateSDUpscale` custom node and an upscaler model such as `RealESRGAN_x4plus.pth`.

### Single image (stdin JSON)

```json
{
  "prompt": "pixel art game character, knight in blue armour, full body, white background, game sprite",
  "outputPath": "C:/Games/my-game/assets/characters/knight.png",
  "negativePrompt": "background, shadow, blurry, low quality, deformed",
  "width": 512,
  "height": 512,
  "steps": 6,
  "cfg": 2.0,
  "seed": 42,
  "checkpoint": "dreamshaperXL_lightning.safetensors",
  "sampler": "dpmpp_sde",
  "scheduler": "karras"
}
```

#### Background removal options

| Field              | Default        | Description                                                                        |
|--------------------|----------------|------------------------------------------------------------------------------------|
| `alphaFromWhite`   | `false`        | Edge-connected flood-fill that auto-detects the dominant edge colour and clears matching pixels. Works for solid backgrounds; preserves interior details of the same colour. |
| `alphaThreshold`   | `15`           | Manhattan-distance tolerance per channel for the flood-fill match.                 |
| `removeBackground` | `false`        | ML foreground segmentation via the ComfyUI-RMBG custom node — works regardless of bg / body colour overlap. Output is automatically passed through `HardenAlpha` to snap the body to fully opaque. |
| `bgRemovalModel`   | `"INSPYRENET"` | RMBG model. Other valid values: `"RMBG-2.0"`, `"BEN"`, `"BEN2"`. INSPYRENET is the default because RMBG-2.0 has compatibility issues with newer transformers versions. |

Use `alphaFromWhite` for solid-background prop / UI generations and `removeBackground` for characters where the body might overlap the bg colour. The two are independent and can run together (alpha first, then ML), though that is rarely needed.

The `removeBackground` path requires the `1038lab/ComfyUI-RMBG` custom node installed in your ComfyUI `custom_nodes/` directory, with `pip install -r requirements.txt`. Models auto-download on first use.

### Bulk image generation (stdin JSON)

Generate multiple images sequentially from a single call. Common fields go in `defaults` and are shared across all images; per-image fields go in each `images` entry. Per-image values override defaults when both are present.

```json
{
  "defaults": {
    "checkpoint": "dreamshaperXL_lightning.safetensors",
    "sampler": "dpmpp_sde",
    "scheduler": "karras",
    "steps": 6,
    "cfg": 2.0,
    "negativePrompt": "blurry, low quality, deformed"
  },
  "images": [
    { "prompt": "knight in armour, pixel art",  "outputPath": "C:/assets/knight.png",  "width": 512,  "height": 512  },
    { "prompt": "level background, forest",     "outputPath": "C:/assets/bg.png",      "width": 1344, "height": 768  },
    { "prompt": "rogue in shadow, game sprite", "outputPath": "C:/assets/rogue.png",   "width": 512,  "height": 512, "seed": 42 }
  ]
}
```

Images are generated one at a time. Each completed image prints `OK: <path> (<bytes> bytes)` to stdout. VRAM is freed once at the end of the whole batch, not between images.

**Fields — common (put in `defaults`):**

| Field            | Default                           | Description                                                             |
|------------------|-----------------------------------|-------------------------------------------------------------------------|
| `negativePrompt` | Common quality/artefact terms     | Things to avoid in all images.                                          |
| `steps`          | `25`                              | Sampling steps.                                                         |
| `cfg`            | `7.0`                             | Classifier-free guidance scale.                                         |
| `checkpoint`     | `COMFYUI_CHECKPOINT` env var      | Checkpoint filename in `models/checkpoints/`.                           |
| `sampler`        | `"euler"`                         | ComfyUI sampler name.                                                   |
| `scheduler`      | `"normal"`                        | ComfyUI scheduler name.                                                 |

**Fields — per-image (put in each `images[]` entry):**

| Field        | Required | Default | Description                                                              |
|--------------|----------|---------|--------------------------------------------------------------------------|
| `prompt`     | Yes      | —       | Positive prompt for this image.                                          |
| `outputPath` | Yes      | —       | Absolute path where the PNG will be saved.                               |
| `width`      | No       | `1024`  | Width in pixels. Must be a multiple of 8.                                |
| `height`     | No       | `1024`  | Height in pixels. Must be a multiple of 8.                               |
| `seed`       | No       | Random  | Fixed seed. If omitted, each image gets a fresh random seed.             |

### Sprite sheet generation

Generates each cell independently from a required `poseReferenceImage` via img2img + ControlNet, then resizes and composites frames into a grid PNG using SkiaSharp. The current path uses the pose image directly as ControlNet input; it does not run a separate Canny preprocessing step.

**Requires:** a ControlNet model in ComfyUI's ControlNet model folder matching `CONTROLNET_MODEL` or the request's `controlNetModel`.

```json
{
  "defaults": {
    "checkpoint": "dreamshaperXL_lightningDPMSDE.safetensors",
    "sampler": "dpmpp_sde",
    "scheduler": "karras",
    "steps": 6,
    "cfg": 2.0,
    "negativePrompt": "multiple characters, character sheet, background scenery, ugly, deformed, blurry"
  },
  "images": [{
    "type": "sprite_sheet",
    "outputPath": "assets/characters/hero_sheet.png",
    "width": 1024,
    "height": 1024,
    "characterPrompt": "solo character, young female adventurer, short red hair, green tunic, clean bold black outlines, flat colors, simple white background, full body, front view, centered",
    "frameWidth": 128,
    "frameHeight": 128,
    "columns": 4,
    "rows": 2,
    "metadataPath": "assets/characters/hero_sheet.metadata.json",
    "cells": [
      { "row": 0, "col": 0, "label": "idle_0", "promptSuffix": "standing idle, arms relaxed at sides", "poseReferenceImage": "tmp/idle_0.png" },
      { "row": 0, "col": 1, "label": "idle_1", "promptSuffix": "breathing in, chest slightly raised", "poseReferenceImage": "tmp/idle_1.png" },
      { "row": 1, "col": 0, "label": "walk_0", "promptSuffix": "walking, left foot forward, right arm forward", "poseReferenceImage": "tmp/walk_0.png" },
      { "row": 1, "col": 1, "label": "walk_1", "promptSuffix": "walking, right foot forward, left arm forward", "poseReferenceImage": "tmp/walk_1.png" }
    ]
  }]
}
```

Each `cells` entry is exactly one cell at `(row, col)` with a specific pose prompt suffix and pose reference image. `animations` is still accepted as a backwards-compatible alias for `cells`. Cells not listed remain transparent. Frames are generated from the pose image and resized to `frameWidth x frameHeight` when compositing. The output PNG is `columns x frameWidth` wide and `rows x frameHeight` tall.

**Sprite sheet fields:**

| Field             | Default | Description                                                              |
|-------------------|---------|--------------------------------------------------------------------------|
| `type`            | —       | Must be `"sprite_sheet"`                                                 |
| `characterPrompt` | -       | Appearance description combined with each cell prompt suffix             |
| `cells`           | -       | Array of `{ row, col, label, promptSuffix, poseReferenceImage }` entries |
| `frameWidth`      | `128`   | Width of each cell in the output grid (px)                               |
| `frameHeight`     | `128`   | Height of each cell in the output grid (px)                              |
| `columns`         | `6`     | Number of columns in the grid                                            |
| `rows`            | `5`     | Number of rows in the grid                                               |
| `heroLabel`       | `""`    | Optional cell label marked as the hero in metadata                       |
| `frameDirectory`  | output folder `frames` | Optional directory for generated frame PNGs                 |
| `metadataPath`    | `""`    | Optional JSON metadata output path                                       |

### ControlNet (structure-preserving generation)

Locks the generated image's composition (silhouette, pose, proportions) to a reference image, while letting SD render fully-detailed art on top. Typical use: rasterize a hand-authored SVG character pose and feed it here so SD renders a detailed sprite that matches the exact pose and layout.

**Requires:** a ControlNet model in `models/controlnet/` matching `CONTROLNET_MODEL` or the request's `controlNetModel`. Choose a model that matches the kind of reference image you are feeding; this tool does not preprocess the reference into Canny edges.

```json
{
  "type": "controlnet",
  "prompt": "armored skeleton warrior, dark fantasy, painterly game sprite, 4k",
  "outputPath": "assets/enemies/skeleton.png",
  "referenceImage": "tmp/skeleton-pose.png",
  "controlNetStrength": 0.9,
  "width": 768,
  "height": 768
}
```

**ControlNet fields:**

| Field                | Required | Default                | Description                                                                 |
|----------------------|----------|------------------------|-----------------------------------------------------------------------------|
| `type`               | Yes      | —                      | Must be `"controlnet"`.                                                     |
| `referenceImage`     | Yes      | —                      | Path to the structure reference (e.g. SVG rasterized to PNG).               |
| `controlNetModel`    | No       | `CONTROLNET_MODEL` env | ControlNet model filename in `models/controlnet/`.                          |
| `controlNetStrength` | No       | `0.9`                  | How strictly composition follows the reference (0.0 ignored – 1.0 strict). |

The same `referenceImage` field also drives `type: "img2img"` mode. ControlNet preserves structure; img2img rewrites the image at a chosen denoise strength.

### Recommended dimensions by asset type

| Asset type              | Width | Height | Notes                              |
|-------------------------|-------|--------|------------------------------------|
| Character sprite        | 512   | 512    | 512×1024 for tall characters       |
| Level background        | 1344  | 768    | Landscape                          |
| UI panel / HUD          | 512   | 512    |                                    |
| Icon (item/ability)     | 256   | 256    |                                    |
| Health / resource bar   | 512   | 128    |                                    |
| Title screen / menu bg  | 1344  | 768    | Landscape                          |

### Output

`OK: <outputPath> (<bytes> bytes)` on success.

### Notes

- The tool polls ComfyUI every 2 seconds until the job completes.
- HTTP requests and queued prompt polling time out after 10 minutes.
- After all images are done, `POST /free` (`unload_models: true, free_memory: true`) is called automatically to release VRAM.
- The `checkpoint` field is useful when you want different models for different asset categories (e.g. a pixel art LoRA for sprites, a different model for cinematic backgrounds).
- `trimBackground` crops near-white or transparent borders; `alphaFromWhite` removes only edge-connected near-white background so interior white details are preserved.
- When `removeBackground` is set, the saved PNG is also run through `HardenAlpha`, which fills any partially-transparent body interior with the median body colour. Use the `harden` CLI directly if you need to repair an already-keyed PNG without regenerating.

---

## music-generator

Generates MP3 music via a locally running **ComfyUI** instance using the **ACE-Step v1.5** music model. Reads a prompt + duration, returns an MP3 saved to `outputPath`.

**Requires:** ComfyUI running locally with these model files in place:
- `models/unet/acestep_v1.5_turbo.safetensors`
- `models/text_encoders/qwen_0.6b_ace15.safetensors` *(both Qwen files required)*
- `models/text_encoders/qwen_4b_ace15.safetensors`   *(both Qwen files required)*
- `models/vae/ace_1.5_vae.safetensors`

ACE-Step v1.5 needs **both** Qwen encoders loaded via `DualCLIPLoader`; using only one produces conditioning with `pooled_output=None` which crashes the sampler.

ACE-Step nodes are built into ComfyUI core (no custom node needed). Queued prompts time out after 10 minutes.

### CLI

```bash
music-generator <file.json>      # generate from JSON config
cat file.json | music-generator  # or pipe via stdin
music-generator --help
```

### Single track (stdin JSON)

```json
{
  "prompt": "epic orchestral, strings, brass, hopeful, victory theme",
  "outputPath": "C:/Games/my-game/assets/audio/menu.mp3",
  "duration": 30,
  "lyrics": "",
  "negativePrompt": "low quality, distorted, noisy",
  "bpm": 120,
  "language": "en",
  "seed": 42,
  "steps": 8,
  "cfg": 1.0,
  "quality": "V0"
}
```

### Bulk (stdin JSON)

```json
{
  "defaults": { "duration": 30, "steps": 8, "cfg": 1.0 },
  "tracks": [
    { "prompt": "8-bit chiptune, upbeat, fast, victory",  "outputPath": "C:/.../menu.mp3" },
    { "prompt": "ambient pads, eerie, slow, low strings", "outputPath": "C:/.../boss.mp3" }
  ]
}
```

### Fields

| Field            | Required | Default | Description                                                          |
|------------------|----------|---------|----------------------------------------------------------------------|
| `prompt`         | Yes      | —       | Style / genre / instrument tags (passed to `tags` on the encoder)    |
| `outputPath`     | Yes      | —       | Absolute path where the MP3 is saved                                 |
| `duration`       | No       | `30`    | Track length in seconds                                              |
| `lyrics`         | No       | `""`    | Lyrics for vocals (omit for instrumental)                            |
| `negativePrompt` | No       | tuned   | Tags to avoid; defaults to a sensible quality-bias baseline          |
| `bpm`            | No       | `120`   | Target tempo                                                         |
| `language`       | No       | `"en"`  | Language tag (en/ja/zh/es/de/fr/pt/ru/it/...)                        |
| `seed`           | No       | random  | Fixed seed for reproducible generations                              |
| `steps`          | No       | `8`     | KSampler steps (turbo model — bump for non-turbo checkpoints)        |
| `cfg`            | No       | `1.0`   | KSampler CFG (turbo wants 1.0; full ACE-Step uses ~5.0)              |
| `quality`        | No       | `"V0"`  | MP3 encoding quality. One of `V0` (best VBR), `128k`, `320k`         |

### Environment variables

| Variable           | Default                  | Description                       |
|--------------------|--------------------------|-----------------------------------|
| `COMFYUI_ENDPOINT` | `http://localhost:8188`  | Base URL of the ComfyUI instance  |

### Output

`OK: <outputPath> (<bytes> bytes)` per track, then `OK: N track(s) generated.` at the end of a batch.

### Notes

- The tool polls ComfyUI every 2 seconds until the job completes.
- After all tracks are done, `POST /free` is called to release VRAM.
- The default sampler is `euler` with `simple` scheduler — proven combo for ACE-Step. If you need different behaviour, adjust `Program.cs` / `ComfyUiClient.cs` rather than exposing more knobs to callers.
- Only `tags` (style/genre) and `lyrics` (optional vocals) are passed to the text encoder; the encoder's other knobs (timesignature, keyscale, temperature, top_p) stay at their ComfyUI defaults.

---

## ollama-stop

Stops a running Ollama model, releasing it from VRAM. Wraps `ollama stop <model>`.

**Requires:** `ollama` installed and available in PATH.

### CLI

```bash
ollama-stop <model>
```

### Pipeline (stdin JSON)

```json
{ "model": "llama3" }
```

| Field   | Required | Description              |
|---------|----------|--------------------------|
| `model` | Yes      | Name of the model to stop |

### Output

`OK` on success (or ollama's own output if non-empty). stderr + non-zero exit on failure.

---

## svg-rasterize

Converts SVG into PNG using SkiaSharp via [`Svg.Skia`](https://github.com/wieslawsoltes/Svg.Skia). Preserves aspect ratio by default (fit-within, centered, transparent padding). Used as the bridge step between a Claude-authored SVG pose and `image-generator`'s ControlNet input.

### CLI

```bash
svg-rasterize <file.json>
```

Or via stdin (pipe a JSON file in).

### Single (stdin JSON)

Either `svg` (inline) or `svgPath` (file) is required — not both:

```json
{
  "svgPath": "tmp/skeleton-pose.svg",
  "outputPath": "tmp/skeleton-pose.png",
  "width": 768,
  "height": 768
}
```

```json
{
  "svg": "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><circle cx='50' cy='50' r='40' fill='#ff3366'/></svg>",
  "outputPath": "tmp/dot.png",
  "width": 256,
  "height": 256,
  "background": "#101018"
}
```

### Bulk (stdin JSON)

```json
{
  "defaults": { "width": 768, "height": 768 },
  "images": [
    { "svgPath": "poses/idle.svg",   "outputPath": "rast/idle.png"   },
    { "svgPath": "poses/strike.svg", "outputPath": "rast/strike.png" }
  ]
}
```

### Fields

| Field        | Required | Default       | Description                                                                                 |
|--------------|----------|---------------|---------------------------------------------------------------------------------------------|
| `svg`        | One of   | —             | Inline SVG markup as a string.                                                              |
| `svgPath`    | One of   | —             | Path to an SVG file on disk.                                                                |
| `outputPath` | Yes      | —             | Absolute path where the PNG will be saved.                                                  |
| `width`      | No       | intrinsic     | Output width in px. If only one dim is given, the other is derived from the SVG's aspect.  |
| `height`     | No       | intrinsic     | Output height in px.                                                                        |
| `background` | No       | `transparent` | Any SkiaSharp-parseable color (`"#ffffff"`, `"#101018"`, `"red"`, etc.).                   |

### Sizing behaviour

- **Both `width` and `height` given** → output is exactly that size. SVG is scaled to fit within while preserving aspect ratio and centered; any remaining area is transparent (or `background` if set). This is the shape ControlNet expects.
- **Only one given** → the other is derived from the SVG's intrinsic aspect ratio.
- **Neither given** → the SVG's intrinsic bounds are used.

### Output

`OK: <outputPath> (<bytes> bytes, <w>x<h>)` per rasterized image. Bulk mode additionally prints `OK: N file(s) rasterized.` at the end.
