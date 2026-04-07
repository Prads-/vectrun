import type { Workspace } from '../types/workspace'
import type { ModelConfig, ToolConfig, AgentConfig } from '../types/workspace'
import type { Pipeline } from '../types/pipeline'

export type { Workspace }

export async function loadWorkspace(directory: string): Promise<{ workspace: Workspace } | { missing: true }> {
  const res = await fetch(`/api/workspace?directory=${encodeURIComponent(directory)}`)
  if (res.status === 404) return { missing: true }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error((body as { error?: string }).error ?? `HTTP ${res.status}`)
  }
  const data = await res.json()
  return { workspace: data as Workspace }
}

export async function scaffoldWorkspace(directory: string): Promise<Workspace> {
  const res = await fetch('/api/workspace/scaffold', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ directory }),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error((body as { error?: string }).error ?? `HTTP ${res.status}`)
  }
  return res.json()
}

export async function saveWorkspace(
  directory: string,
  pipeline: Pipeline,
  models: ModelConfig[],
  tools: ToolConfig[],
  agents: AgentConfig[]
): Promise<void> {
  const res = await fetch('/api/workspace', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ directory, pipeline, models, tools, agents }),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error((body as { error?: string }).error ?? `HTTP ${res.status}`)
  }
}
