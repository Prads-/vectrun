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
| **logic** | Runs an external process (`logicType: "process"`) or an embedded Lua script (`logicType: "script"`). Input via stdin, output from stdout or return value. A process fails if it exits with a non-zero code or writes anything to stderr. Set `processPathType` to `"relative"` (default, resolved relative to the pipeline folder) or `"absolute"`. |
| **wait** | Sleeps for `durationMs` milliseconds, then passes input through unchanged. |

### Logic node — `processInput` and `{INPUT}`

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

The editor is a drag-and-drop graph canvas backed by a left sidebar with five sections:

- **Nodes** — view and edit all pipeline nodes; set the pipeline name and start node
- **Models** — configure AI backends
- **Agents** — define agents and assign models, tools, and prompts
- **Tools** — view registered external tools
- **Run** — provide optional input and execute the pipeline

### Live output console

Clicking **Run** streams log entries in real time to a collapsible output panel at the bottom of the canvas. Each entry shows a timestamp, the node that produced it, and the event type:

| Event | When it fires |
|---|---|
| `started` | A node begins executing |
| `output` | A node completes and emits its result |
| `tool_call` | An agent node invokes a tool |
| `tool_result` | The tool returns a result |
| `retry` | A node failed and is being retried (includes attempt number and error) |
| `failed` | A node has exhausted all retries |
| `branch_failed` | A branch stopped due to an unrecoverable node failure |

Entries can be filtered by node using the dropdown in the panel header.

## Inter-agent messaging

Two built-in tools are available to every agent — no configuration needed:

- `my_message_queue(agentId)` — dequeues one message from an agent's FIFO queue; returns empty string if empty.
- `queue_message(agentId, message)` — enqueues a message onto an agent's queue.

These names are reserved and cannot be used for user-defined tools. The intended polling pattern is **WaitNode → AgentNode → BranchNode** (loop back if queue is empty).

## Bundled tools

The `tools/` directory contains ready-made executables you can reference in `tools.json`.

### web-scraper

Fetches the fully-rendered HTML of a URL (JavaScript executed) and writes it to stdout.

```
web-scraper <url>
```

Uses a headless Chromium browser (Playwright / Chromium) with bot-detection mitigations applied: a realistic Chrome user-agent, `AutomationControlled` disabled, and `navigator.webdriver` hidden from JavaScript. Navigation waits for the page `load` event (60 s timeout), then attempts an additional 25 s network-idle wait (best-effort — sites with persistent analytics beacons won't block on this).

### kv-store

A lightweight, disk-backed key-value store. Data is written to `data/<namespace>/<key-hash>` files next to the executable, so it persists across pipeline runs without any external service.

```
kv-store write  <namespace> <key> <value>   # upsert — creates or overwrites
kv-store update <namespace> <key> <value>   # overwrite (fails if key absent)
kv-store read   <namespace> <key>           # print value to stdout; empty string if not found (exit 0)
kv-store delete <namespace> <key>           # remove entry; no-op if absent (exit 0)
kv-store append <namespace> <key> <value>   # append to existing value with separator; create if absent
```

Namespaces keep agents isolated — each agent can read and write its own namespace without colliding with others. Keys are hashed (SHA-256) so any string is a valid key.

When called via stdin JSON, `append` accepts an optional `"separator"` field (defaults to `"\n\n---\n\n"`):

```json
{ "operation": "append", "namespace": "logs", "key": "run_1", "value": "new entry", "separator": "\n" }
```

`read` returns an empty string (exit 0) when the key does not exist, so callers can treat empty as not-found rather than handling a failure. `delete` is idempotent for the same reason.

### scaffold-claude

Reads project requirements from stdin, scaffolds a `CLAUDE.md` in a new project directory, then launches Claude Code non-interactively to build the project. Claude's output is streamed to stdout so it flows through the pipeline.

```
scaffold-claude <project-directory>
```

The requirements text is read from stdin (piped from a previous node). Claude Code must be installed and available in `PATH`. The project directory is created if it does not exist.

**Example pipeline use:** an agent node summarises or refines raw requirements → `scaffold-claude` scaffolds and builds the project → a downstream node reads the build summary.

### image-generator

Generates an image via a local [ComfyUI](https://github.com/comfyanonymous/ComfyUI) instance and saves it to disk. Input is a JSON object read from stdin.

| Field | Required | Default | Description |
|---|---|---|---|
| `prompt` | yes | — | Positive prompt describing the image. |
| `outputPath` | yes | — | Absolute path where the PNG will be saved. |
| `negativePrompt` | no | built-in quality defaults | Things to avoid in the image. |
| `width` | no | `1024` | Image width in pixels (must be a multiple of 8). |
| `height` | no | `1024` | Image height in pixels (must be a multiple of 8). |
| `steps` | no | `25` | Sampling steps. Use `6`–`8` for Lightning/Turbo models. |
| `cfg` | no | `7.0` | Classifier-free guidance scale. Use `1.5`–`2.0` for Lightning/Turbo models. |
| `seed` | no | random | Fixed seed for reproducibility. |
| `checkpoint` | no | `COMFYUI_CHECKPOINT` env var | Checkpoint filename as it appears in ComfyUI's `models/checkpoints/` folder. |
| `sampler` | no | `"euler"` | ComfyUI sampler name (e.g. `"dpmpp_sde"` for DPM++ SDE models). |
| `scheduler` | no | `"normal"` | ComfyUI scheduler name (e.g. `"karras"`). |

ComfyUI must be running and reachable at `http://localhost:8188` (override with the `COMFYUI_ENDPOINT` environment variable). No special ComfyUI extensions are required — only built-in nodes are used. After saving the image the tool automatically calls `POST /free` (`unload_models: true, free_memory: true`) to release VRAM; this is best-effort and does not affect the tool's exit code.

**Example — DreamShaper XL Lightning DPM++ SDE:**

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

Place checkpoint `.safetensors` files in ComfyUI's `models/checkpoints/` folder. SDXL-based checkpoints (native 1024×1024) are recommended for the default resolution; if using SD 1.5 checkpoints, set `width` and `height` to `512`.

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
