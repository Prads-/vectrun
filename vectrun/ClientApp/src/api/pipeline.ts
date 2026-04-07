import type { Pipeline } from '../types/pipeline'
import type { LogEntry } from '../types/console'

export async function loadPipeline(directory: string): Promise<Pipeline> {
  const res = await fetch(`/api/pipeline?directory=${encodeURIComponent(directory)}`)
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error((body as { error?: string }).error ?? `HTTP ${res.status}`)
  }
  return res.json()
}

export class ConflictError extends Error {}

/**
 * Starts a pipeline run and yields log entries as they stream in via SSE.
 * The generator completes when the run finishes or the stream closes.
 */
export async function* streamRun(
  directory: string,
  input?: string
): AsyncGenerator<LogEntry> {
  const res = await fetch('/api/pipelines/run/stream', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ directory, input: input || null }),
  })

  if (res.status === 409) throw new ConflictError('A pipeline run is already in progress.')
  if (!res.ok) {
    const body = await res.text()
    throw new Error(`HTTP ${res.status}: ${body}`)
  }

  const reader = res.body!.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() ?? ''

      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const json = line.slice(6).trim()
          if (json) yield JSON.parse(json) as LogEntry
        }
      }
    }
  } finally {
    reader.releaseLock()
  }
}
