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
    "path": "search"
  }
]
```

Tool executables live in the `tools/` subdirectory. Input is passed via stdin, output is read from stdout.

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
| **branch** | Compares input to `expectedOutput`. Routes to `trueNodeIds` or `falseNodeIds`. |
| **logic** | Runs an external process (`logicType: "process"`) or an embedded Lua script (`logicType: "script"`). Input via stdin, output from stdout or return value. |
| **wait** | Sleeps for `durationMs` milliseconds, then passes input through unchanged. |

All node types support an optional `name` field for human-readable labelling.

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
| `error` | An error occurred |

Entries can be filtered by node using the dropdown in the panel header.

## Inter-agent messaging

Two built-in tools are available to every agent — no configuration needed:

- `my_message_queue(agentId)` — dequeues one message from an agent's FIFO queue; returns empty string if empty.
- `queue_message(agentId, message)` — enqueues a message onto an agent's queue.

These names are reserved and cannot be used for user-defined tools. The intended polling pattern is **WaitNode → AgentNode → BranchNode** (loop back if queue is empty).

## State management

The runtime owns no persistent storage beyond the in-memory message queues. All other state — shared or agent-scoped — is handled via external tools. Choose whatever backend fits your pipeline (SQLite, Redis, a vector DB, plain files).

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
