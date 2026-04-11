export interface ModelConfig {
  id: string
  name: string
  type: 'ollama' | 'vllm' | 'llama.cpp' | 'open_ai' | 'anthropic'
  endpoint: string
  apiKey?: string
}

export interface ToolConfig {
  name: string
  description: string
  parameters: Record<string, unknown>
  path: string
  pathType: 'relative' | 'absolute'
}

export interface AgentConfig {
  agentName: string
  systemPrompt?: string
  modelId: string
  output: 'plain_text' | 'json'
  outputSchema?: unknown
  prompt?: string
  toolIds?: string[]
}

export interface Workspace {
  pipeline: import('./pipeline').Pipeline
  models: ModelConfig[]
  tools: ToolConfig[]
  agents: AgentConfig[]
}
