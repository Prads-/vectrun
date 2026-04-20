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
dotnet build tools/ollama-stop/ollama-stop.csproj          -c Release
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

Generates images via a locally running **ComfyUI** instance. Supports single images, bulk generation, and sprite sheets with IPAdapter-based character consistency.

**Requires:** ComfyUI running locally (default `http://localhost:8188`).

### CLI

```bash
image-generator <file.json>
```

Or via stdin (pipe a JSON file in).

### Environment variables

| Variable             | Default                                        | Description                                           |
|----------------------|------------------------------------------------|-------------------------------------------------------|
| `COMFYUI_ENDPOINT`   | `http://localhost:8188`                        | Base URL of the ComfyUI instance                      |
| `COMFYUI_CHECKPOINT` | `v1-5-pruned-emaonly.safetensors`              | Default checkpoint filename                           |
| `IPADAPTER_MODEL`    | `ip-adapter_sdxl.bin`                         | IPAdapter model filename (sprite sheets only)         |
| `CLIP_VISION_MODEL`  | `CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors` | CLIP Vision model filename (sprite sheets only)       |

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

Generates a canonical character reference via text2img, then generates each cell independently via IPAdapter (character consistency) with a unique pose prompt per cell. All frames are resized and composited into a grid PNG using SkiaSharp.

**Requires:** `ComfyUI_IPAdapter_plus` custom node installed via ComfyUI Manager, plus:
- `models/ipadapter/ip-adapter_sdxl.bin`
- `models/clip_vision/CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors`

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
    "ipAdapterWeight": 0.7,
    "animations": [
      { "row": 0, "col": 0, "prompt": "standing idle, arms relaxed at sides" },
      { "row": 0, "col": 1, "prompt": "breathing in, chest slightly raised" },
      { "row": 1, "col": 0, "prompt": "walking, left foot forward, right arm forward" },
      { "row": 1, "col": 1, "prompt": "walking, right foot forward, left arm forward" }
    ]
  }]
}
```

Each `animations` entry is exactly one cell at `(row, col)` with a specific pose prompt. Cells not listed remain transparent. Frames are generated at `width × height` and resized to `frameWidth × frameHeight` when compositing. The output PNG is `columns × frameWidth` wide and `rows × frameHeight` tall.

**Sprite sheet fields:**

| Field             | Default | Description                                                              |
|-------------------|---------|--------------------------------------------------------------------------|
| `type`            | —       | Must be `"sprite_sheet"`                                                 |
| `characterPrompt` | —       | Appearance-only description used for the canonical reference sprite      |
| `animations`      | —       | Array of `{ row, col, prompt }` — one entry per cell                    |
| `frameWidth`      | `128`   | Width of each cell in the output grid (px)                               |
| `frameHeight`     | `128`   | Height of each cell in the output grid (px)                              |
| `columns`         | `6`     | Number of columns in the grid                                            |
| `rows`            | `5`     | Number of rows in the grid                                               |
| `ipAdapterWeight` | `0.7`   | IPAdapter influence strength (0.0–1.0)                                   |

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
- HTTP client timeout is 10 minutes to accommodate slow generations.
- After all images are done, `POST /free` (`unload_models: true, free_memory: true`) is called automatically to release VRAM.
- The `checkpoint` field is useful when you want different models for different asset categories (e.g. a pixel art LoRA for sprites, a different model for cinematic backgrounds).

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
