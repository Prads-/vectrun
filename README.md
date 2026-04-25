# vectrun

A file-driven graph execution engine for building and running AI agent pipelines. Define your workflow as a directed graph of nodes in JSON, then run it from the CLI or build it interactively in the web UI.

## How it works

A pipeline is a directed graph. Execution starts at a designated node, each node produces an output passed to the next, and the graph terminates when a node returns no successors. Multiple successors run as independent parallel branches. Cycles in the graph create loops — there is no dedicated loop construct.

```
Start → AgentNode → BranchNode → AgentNode → ...
                 ↘ AgentNode → ...
```

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org) (for the web UI)

### Run the web UI

```bash
dotnet run --project vectrun/vectrun.csproj
```

Then in a second terminal:

```bash
cd vectrun/ClientApp
npm ci
npm run dev
```

Open [http://localhost:5173](http://localhost:5173). Point it at a pipeline directory to load and edit it, or let it scaffold a new one for you.

For a step-by-step walkthrough of the editor, see the **[Hello World tutorial](tutorial.md)**.

### Run a pipeline from the CLI

```bash
dotnet run --project vectrun/vectrun.csproj -- /path/to/pipeline
```

Ctrl+C cancels gracefully.

## Pipeline directory structure

```
my-pipeline/
├── pipeline.json       # Graph definition
├── models.json         # AI backend configurations
├── tools.json          # External tool definitions
├── agents/             # One JSON file per agent
│   └── my-agent.json
└── tools/              # Tool executables
    └── my-tool
```

### pipeline.json

```json
{
  "pipelineName": "My Pipeline",
  "startNodeId": "step1",
  "nodes": [
    {
      "id": "step1",
      "type": "agent",
      "data": { "name": "Classify", "agentId": "my-agent", "nextNodeIds": ["step2"] }
    },
    {
      "id": "step2",
      "type": "wait",
      "data": { "name": "Cool-down", "durationMs": 1000, "nextNodeIds": [] }
    }
  ]
}
```

The optional `name` field on each node is a human-readable label shown in the UI and the live output console. The `id` remains the internal identifier used for routing.

### models.json

```json
[
  {
    "id": "llama3.2",
    "name": "Llama 3.2 (local)",
    "type": "ollama",
    "endpoint": "http://localhost:11434"
  },
  {
    "id": "claude-sonnet-4-6",
    "name": "Claude Sonnet",
    "type": "anthropic",
    "endpoint": "https://api.anthropic.com",
    "apiKey": "sk-ant-..."
  }
]
```

`id` is the model identifier sent to the AI backend (e.g. `llama3.2`, `claude-sonnet-4-6`, `gpt-4o`). `name` is a display label used only in the UI — it has no effect on which model is called.

Supported types: `anthropic`, `open_ai`, `ollama`, `vllm`, `llama.cpp`

### tools.json

```json
[
  {
    "name": "search",
    "description": "Search the web",
    "parameters": {
      "type": "object",
      "properties": { "query": { "type": "string" } },
      "required": ["query"]
    },
    "path": "search/bin/Release/net9.0/search.exe",
    "pathType": "relative"
  }
]
```

Input is passed via stdin, output is read from stdout.

| Field | Description |
|---|---|
| `path` | Path to the tool executable. |
| `pathType` | `"relative"` (default) — resolved as `<pipeline folder>/tools/<path>`. `"absolute"` — used as-is. |

Both `pathType` options are configurable in the web editor's **Tools** panel, which shows the resolved path pattern as a hint beneath the input.

### agents/my-agent.json

```json
{
  "agentName": "my-agent",
  "modelId": "claude-sonnet-4-6",
  "systemPrompt": "You are a helpful assistant.",
  "output": "plain_text",
  "prompt": "Summarise this: {PREVIOUS_AGENT_OUTPUT}",
  "toolIds": ["search"]
}
```

`modelId` must match the `id` of an entry in `models.json`. `{PREVIOUS_AGENT_OUTPUT}` is the only supported template variable in `prompt`. If `prompt` is omitted, the previous node's output is sent to the agent as-is.

## Node types

| Type | What it does |
|---|---|
| **agent** | Sends a request to an AI model. Runs a tool-calling loop until the model stops calling tools. |
| **branch** | Compares input to `expectedOutput` (optional). Routes to `trueNodeIds` on match, `falseNodeIds` on mismatch. If `expectedOutput` is omitted, always routes to `trueNodeIds`. |
| **logic** | Runs an external process (`logicType: "process"`) or an embedded Lua script (`logicType: "script"`). For processes: input is passed via stdin, output is read from stdout, and the node fails on non-zero exit; stderr is included in the failure message when the process fails. Set `processPathType` to `"relative"` (default, resolved relative to the pipeline folder) or `"absolute"`. For scripts: input is exposed as the Lua global `input`; every tool in `tools.json` is exposed as a Lua function; the script's return value becomes the node output. |
| **wait** | Sleeps for `durationMs` milliseconds, then passes input through unchanged. |

### Logic node — `processInput` and `{PREVIOUS_AGENT_OUTPUT}`

By default a logic node pipes the previous node's output directly to the process stdin. Use `processInput` to override this with a hardcoded payload:

```json
{
  "logicType": "process",
  "processPath": "tools/kv-store/kv-store.exe",
  "processInput": "{\"operation\":\"delete\",\"namespace\":\"my_ns\",\"key\":\"counter\"}"
}
```

To inject the previous node's output into a structured payload, use the `{PREVIOUS_AGENT_OUTPUT}` template variable inside `processInput` — the same placeholder used in agent prompts. The value is JSON-escaped before substitution, so it is safe to embed inside a JSON string field:

```json
{
  "processInput": "{\"operation\":\"append\",\"namespace\":\"my_ns\",\"key\":\"log\",\"value\":\"{PREVIOUS_AGENT_OUTPUT}\"}"
}
```

At runtime `{PREVIOUS_AGENT_OUTPUT}` is replaced with the previous node's output, properly escaped. This is the recommended pattern for reliably writing dynamic data (e.g. agent-generated summaries) to a tool without relying on the agent to make the tool call itself.

`{PREVIOUS_AGENT_OUTPUT}` is configurable in the **Node Properties** panel in the web editor — the process input hint documents the substitution inline.

### Logic node — Lua scripting

Set `logicType` to `"script"` and provide a `script` field containing Lua code. The previous node's output is available as the global `input`, and every entry in `tools.json` is exposed as a Lua function of the same name. The script's return value becomes the node output — return a string or `nil`.

```json
{
  "id": "init-run",
  "type": "logic",
  "data": {
    "logicType": "script",
    "script": "kv_store({ operation = 'delete_prefix', namespace = 'game_dev', key = '' })\nreturn input",
    "nextNodeIds": ["next"]
  }
}
```

Tool functions accept either a table (auto-converted to JSON — array-like tables become JSON arrays, hash-like tables become objects) or a string (passed through verbatim). The return value is the tool's stdout as a Lua string. Anything the tool writes to stderr flows into the live output console as a `tool_log` event, just like it does for agent-invoked tools.

Tool names that collide with Lua keywords or contain special characters should be renamed in `tools.json` — the global is created with the exact name string.

All node types support an optional `name` field for human-readable labelling.

## Retry policy

Agent and Logic nodes support an optional retry policy. When a node fails it is retried up to `retryCount` times before the branch stops.

```json
{
  "id": "call-api",
  "type": "agent",
  "data": {
    "agentId": "my-agent",
    "nextNodeIds": ["next"],
    "retry": {
      "retryCount": 3,
      "retryDelayMs": 1000,
      "delayType": "sliding"
    }
  }
}
```

| Field | Description |
|---|---|
| `retryCount` | Number of retries after the initial attempt. `0` or absent disables retries. |
| `retryDelayMs` | Base delay in milliseconds between attempts. |
| `delayType` | `"linear"` — same delay every time. `"sliding"` — delay doubles each attempt (1 s → 2 s → 4 s …). |

Retry policy is configured per-node in the web editor under the **Retry Policy** section of the node properties panel.

## Error handling

When a node exhausts all retries the branch it belongs to stops. Other parallel branches are unaffected and continue to completion. The pipeline itself only finishes once every branch has either completed or stopped.

## Web UI

The editor is a drag-and-drop graph canvas backed by a collapsible left sidebar. The sidebar icon bar has four section tabs — **Nodes**, **Models**, **Agents**, **Tools** — plus **Run** and **Save** buttons at the bottom. The content panel is resizable (drag the right edge) and its width persists across sessions.

- **Nodes** — view and edit all pipeline nodes; set the pipeline name and start node
- **Models** — configure AI backends
- **Agents** — define agents, assign models/tools/prompts, set output type and optional JSON schema; drag agents directly onto the canvas or use the **Add to Canvas** button
- **Tools** — view and edit registered external tools
- **Run** — provide optional input and execute the pipeline; includes a **Stop** button to cancel a running pipeline

The canvas toolbar includes a **Layout** button that auto-positions nodes using a dagre-based directed layout. Edges use a floating style that routes around nodes, and node connection handles are hidden until the node is hovered or selected.

### Live output console

Clicking **Run** streams log entries in real time to a collapsible output panel at the bottom of the canvas. The panel height is resizable and persists across sessions. Each entry shows a timestamp, the node that produced it, and the event type:

| Event | When it fires |
|---|---|
| `started` | A node begins executing |
| `output` | A node completes and emits its result |
| `tool_call` | An agent node invokes a tool |
| `tool_result` | The tool returns a result |
| `tool_log` | A tool wrote a line to stderr (streamed live while the tool runs) |
| `retry` | A node failed and is being retried (includes attempt number and error) |
| `failed` | A node has exhausted all retries |
| `branch_failed` | A branch stopped due to an unrecoverable node failure |

Entries can be filtered by one or more nodes using the multi-select dropdown in the panel header.

## Inter-agent messaging

Two built-in tools are available to every agent — no configuration needed:

- `my_message_queue(agentId)` — dequeues one message from an agent's FIFO queue; returns empty string if empty.
- `queue_message(agentId, message)` — enqueues a message onto an agent's queue.

These names are reserved and cannot be used for user-defined tools. The intended polling pattern is **WaitNode → AgentNode → BranchNode** (loop back if queue is empty).

## Bundled tools

The `tools/` directory contains ready-made executables you can reference in `tools.json`.

### web-scraper

Fetches the fully-rendered HTML (or visible text) of a URL using a headless Chromium browser (Playwright). JavaScript executes before content is captured.

```bash
web-scraper <url> [--extract-text]
```

Pipeline stdin JSON:

```json
{ "url": "https://example.com", "extractText": true }
```

| Field | Default | Description |
|---|---|---|
| `extractText` | `false` | When `true`, returns visible page text with all HTML/CSS/JS stripped. When `false` (default), returns raw HTML. |

Uses bot-detection mitigations: realistic Chrome user-agent, `AutomationControlled` disabled, `navigator.webdriver` hidden. Navigation waits for the `load` event (30 s timeout), then attempts an additional 25 s network-idle wait (best-effort). For Google Search pages, structured title/URL/snippet tuples are extracted from the DOM instead of raw HTML to avoid obfuscated results.

### kv-store

A lightweight, disk-backed key-value store. Data is written to `data/<namespace>/<key-hash>` files next to the executable, so it persists across pipeline runs without any external service.

```
kv-store write         <namespace> <key>    <value>   # upsert — creates or overwrites
kv-store update        <namespace> <key>    <value>   # upsert (same as write)
kv-store read          <namespace> <key>              # print value to stdout; empty string if not found (exit 0)
kv-store delete        <namespace> <key>              # remove entry; no-op if absent (exit 0)
kv-store append        <namespace> <key>    <value>   # append to existing value with separator; create if absent
kv-store delete_prefix <namespace> <prefix>           # delete every key whose name starts with <prefix>; prints count deleted
```

`delete_prefix` is useful at the start of a pipeline run to wipe a whole slice of state (e.g. all keys under a `run_` prefix) without having to enumerate them. Pass an empty string as the prefix to wipe the whole namespace. It uses `.key` sidecar files written alongside each hashed value file to resolve the original key name.

Namespaces keep agents isolated — each agent can read and write its own namespace without colliding with others. Keys are hashed (SHA-256) so any string is a valid key.

When called via stdin JSON, `append` accepts an optional `"separator"` field (defaults to `"\n\n---\n\n"`):

```json
{ "operation": "append", "namespace": "logs", "key": "run_1", "value": "new entry", "separator": "\n" }
```

`read` returns an empty string (exit 0) when the key does not exist, so callers can treat empty as not-found rather than handling a failure. `delete` is idempotent for the same reason.

### scaffold-claude

Reads project requirements from stdin, scaffolds a `CLAUDE.md` in a new project directory, then launches Claude Code in a new visible console window so the user can watch the build. The pipeline waits for that console process to exit. The console stays open after Claude finishes until a key is pressed, then `scaffold-claude` writes a final `Project directory: <path>` line to stdout.

```
scaffold-claude <project-directory>
```

The requirements text is read from stdin (piped from a previous node). Claude Code must be installed and available in `PATH`. The project directory is created if it does not exist. In JSON pipeline mode, pass `projectDirectory`, `requirements`, and optional `model`.

**Example pipeline use:** an agent prepares requirements → `scaffold-claude` writes `CLAUDE.md` and opens Claude Code in a visible terminal → the pipeline receives the final project directory line after the terminal exits.

### image-generator

Generates an image, sprite sheet, composite sheet, or batch of images via a local [ComfyUI](https://github.com/comfyanonymous/ComfyUI) instance and saves each to disk. Input is a JSON object read from stdin.

ComfyUI must be running at `http://localhost:8188` (override with `COMFYUI_ENDPOINT`). Queued prompts time out after 10 minutes. After generation the tool calls `POST /free` to release VRAM (best-effort).

Some workflows need ComfyUI assets beyond the base install. Upscaling (`upscaleBy > 0`, including several presets) uses the `UltimateSDUpscale` custom node and an upscaler such as `RealESRGAN_x4plus.pth`. ControlNet and sprite-sheet modes require the configured ControlNet model in ComfyUI's ControlNet model folder; the current sprite-sheet path feeds the pose image directly to img2img + ControlNet and does not run a separate Canny preprocessing step.

**Game asset presets:**

```json
{
  "preset": "character_pixel",
  "prompt": "small knight with a blue cape",
  "outputPath": "C:/assets/knight.png",
  "seed": 42
}
```

Known presets are `character_pixel`, `character_cartoon`, `env_sprite_pixel`, `env_sprite_cartoon`, `background_pixel`, `background_cartoon`, `background_topdown_pixel`, `background_topdown_cartoon`, `ui_pixel`, and `ui_cartoon`. Presets own the model, sampler, dimensions, upscale, trim, and alpha settings; callers should normally provide only `preset`, `prompt`, `outputPath`, and optionally `seed` or `negativePrompt`. Env sprite and UI presets trim the white canvas and convert only edge-connected white background to alpha, preserving intentional interior white details.

**Single image:**

```json
{
  "prompt": "a knight in a forest, cinematic lighting",
  "outputPath": "C:/output/knight.png",
  "checkpoint": "dreamshaperXL_lightningDPMSDE.safetensors",
  "sampler": "dpmpp_sde",
  "scheduler": "karras",
  "steps": 6,
  "cfg": 2.0
}
```

| Field | Required | Default | Description |
|---|---|---|---|
| `prompt` | yes | — | Positive prompt. |
| `outputPath` | yes | — | Absolute path for the output PNG. |
| `negativePrompt` | no | built-in quality defaults | Things to avoid. |
| `width` | no | `1024` | Width in pixels (multiple of 8). |
| `height` | no | `1024` | Height in pixels (multiple of 8). |
| `steps` | no | `25` | Sampling steps. Use `6`–`8` for Lightning/Turbo models. |
| `cfg` | no | `7.0` | Guidance scale. Use `1.5`–`2.0` for Lightning/Turbo models. |
| `seed` | no | random | Fixed seed for reproducibility. |
| `checkpoint` | no | `COMFYUI_CHECKPOINT` env var | Checkpoint filename in `models/checkpoints/`. |
| `sampler` | no | `"euler"` | ComfyUI sampler name. |
| `scheduler` | no | `"normal"` | ComfyUI scheduler name. |
| `trimBackground` | no | `false` | Crops connected white/transparent border pixels around the subject. |
| `alphaFromWhite` | no | `false` | Converts only edge-connected near-white background to transparent alpha. |

**Bulk image generation:**

Generate multiple images in one call. Shared settings go in `defaults`; per-image fields go in `images[]`. Per-image values override defaults.

```json
{
  "defaults": {
    "checkpoint": "dreamshaperXL_lightningDPMSDE.safetensors",
    "sampler": "dpmpp_sde",
    "scheduler": "karras",
    "steps": 6,
    "cfg": 2.0
  },
  "images": [
    { "prompt": "knight in armour, pixel art",  "outputPath": "C:/assets/knight.png",  "width": 512,  "height": 512 },
    { "prompt": "forest level background",      "outputPath": "C:/assets/bg.png",      "width": 1344, "height": 768 },
    { "prompt": "rogue in shadow, game sprite", "outputPath": "C:/assets/rogue.png",   "width": 512,  "height": 512, "seed": 42 }
  ]
}
```

Images are generated sequentially. Each completed image prints `OK: <path> (<bytes> bytes)` to stdout. VRAM is freed once at the end of the batch.

Place checkpoint `.safetensors` files in ComfyUI's `models/checkpoints/` folder. SDXL-based checkpoints (native 1024×1024) are recommended for the default resolution; if using SD 1.5 checkpoints, set `width` and `height` to `512`.

### file-manager

Performs file system operations: create directories, write text or binary files, read text files, and list directory contents. Intermediate directories are created automatically on write operations.

```json
{ "operation": "write_text", "path": "C:/output/notes.txt", "content": "hello world" }
```

| Field | Required | Description |
|---|---|---|
| `operation` | yes | See operations table below. |
| `path` | yes | Absolute path to the file or directory. |
| `content` | `write_text` only | UTF-8 text content to write. |
| `contentBase64` | `write_binary` only | Base64-encoded binary content to write. |

| Operation | Description |
|---|---|
| `create_directory` | Creates the directory and any missing parents. |
| `write_text` | Writes a UTF-8 text file, creating parent directories as needed. |
| `write_binary` | Writes a binary file from a base64 string. |
| `read_text` | Reads and returns the contents of a text file. |
| `list_directory` | Returns a JSON array of `{name, type, size}` entries. |

Output: `OK: <path>` on successful write/create; file contents on read; JSON array on list.

### ollama-stop

Stops a running Ollama model, releasing it from VRAM. Useful between pipeline stages that use different models to avoid out-of-memory failures.

```json
{ "model": "llama3" }
```

Requires `ollama` to be installed and available in `PATH`. Wraps `ollama stop <model>`.

## State management

The runtime owns no persistent storage beyond the in-memory message queues. All other state — shared or agent-scoped — is handled via external tools. The bundled `kv-store` tool covers simple key-value needs; swap it for SQLite, Redis, a vector DB, or plain files as your pipeline demands.

## Development

```bash
# Run all tests
dotnet test vectrun.tests/

# Production build (compiles frontend into wwwroot)
dotnet build -c Release vectrun.sln
```

The Vite dev server proxies `/api` requests to `http://localhost:50125` (the ASP.NET backend).

## Design philosophy

The pipeline provides structure and execution — not guardrails. It does not control model behaviour, sanitise inputs, or prevent infinite loops. Prompt engineering, model selection, and safe graph design are the pipeline builder's responsibility.
