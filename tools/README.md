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
web-scraper <url>
```

### Pipeline (stdin JSON)

```json
{ "url": "https://store.steampowered.com/charts/topselling/" }
```

### Output

The full HTML of the page written to stdout.

### Notes

- Chromium is installed automatically as a post-build step.
- Timeout is 30 seconds per page.
- Bare URLs without a scheme are normalised to `https://`.

---

## kv-store

A simple file-backed key-value store. Data is persisted in a `data/` directory next to the executable, organised by namespace, with keys stored as SHA-256-hashed filenames. Safe for concurrent reads; writes are exclusive.

### CLI

```bash
kv-store <read|write|update|delete> <namespace> <key> [value]
```

### Pipeline (stdin JSON)

```json
{ "operation": "write", "namespace": "game_dev", "key": "research_report", "value": "..." }
```

| Field       | Required         | Description                                         |
|-------------|------------------|-----------------------------------------------------|
| `operation` | Yes              | `read`, `write`, `update`, or `delete`              |
| `namespace` | Yes              | Groups related keys (e.g. `game_dev`)               |
| `key`       | Yes              | Arbitrary string key                                |
| `value`     | write/update only| The value to store                                  |

### Operations

| Operation | Behaviour                                                        |
|-----------|------------------------------------------------------------------|
| `write`   | Creates or overwrites a key (upsert).                            |
| `read`    | Returns the stored value. Returns empty string (exit 0) if key does not exist. |
| `update`  | Overwrites an existing key. Fails if key does not exist.         |
| `delete`  | Removes the key. Succeeds silently if the key does not exist.    |

### Output

`OK` on success for write/update/delete. The stored value on read.

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

Generates an image via a locally running **ComfyUI** instance and saves it to disk. Builds a standard txt2img workflow (KSampler → VAEDecode → SaveImage), queues it via the ComfyUI REST API, polls until complete, then downloads and saves the output PNG.

**Requires:** ComfyUI running locally (default `http://localhost:8188`).

### Environment variables

| Variable             | Default                              | Description                                           |
|----------------------|--------------------------------------|-------------------------------------------------------|
| `COMFYUI_ENDPOINT`   | `http://localhost:8188`              | Base URL of the ComfyUI instance                      |
| `COMFYUI_CHECKPOINT` | `v1-5-pruned-emaonly.safetensors`    | Default checkpoint filename. Must exist in ComfyUI's `models/checkpoints/` directory. |

### Pipeline (stdin JSON)

```json
{
  "prompt": "pixel art game character, knight in blue armour, full body, white background, game sprite",
  "outputPath": "C:/Games/my-game/assets/characters/knight.png",
  "negativePrompt": "background, shadow, blurry, low quality, deformed",
  "width": 512,
  "height": 512,
  "steps": 25,
  "cfg": 7,
  "seed": 42,
  "checkpoint": "dreamshaper_8.safetensors"
}
```

| Field            | Required | Default                           | Description                                                                                   |
|------------------|----------|-----------------------------------|-----------------------------------------------------------------------------------------------|
| `prompt`         | Yes      | —                                 | Positive prompt. Include art style, colours, subject, and composition.                        |
| `outputPath`     | Yes      | —                                 | Absolute path where the PNG will be saved. Parent directories are created automatically.       |
| `negativePrompt` | No       | Common quality/artefact terms     | Things to avoid. Tune per asset type (e.g. add `'background'` for sprites).                  |
| `width`          | No       | `1024`                            | Width in pixels. Must be a multiple of 8.                                                     |
| `height`         | No       | `1024`                            | Height in pixels. Must be a multiple of 8.                                                    |
| `steps`          | No       | `25`                              | Sampling steps. 20–30 is a good range.                                                        |
| `cfg`            | No       | `7.0`                             | Classifier-free guidance scale. 6–8 for balanced results.                                     |
| `seed`           | No       | Random                            | Fixed seed for reproducibility.                                                                |
| `checkpoint`     | No       | `COMFYUI_CHECKPOINT` env var      | Checkpoint filename in `models/checkpoints/`. Overrides the env var for this call only.       |

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
- After saving the image, `POST /free` (`unload_models: true, free_memory: true`) is called automatically to release VRAM. This is best-effort and does not affect the tool's exit code.
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
