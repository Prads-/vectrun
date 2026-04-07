export interface BrowseEntry {
  name: string
  path: string
}

export interface BrowseResponse {
  current: string
  parent: string | null
  entries: BrowseEntry[]
}

export async function browse(path?: string): Promise<BrowseResponse> {
  const url = path ? `/api/browse?path=${encodeURIComponent(path)}` : '/api/browse'
  const res = await fetch(url)
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error((body as { error?: string }).error ?? `HTTP ${res.status}`)
  }
  return res.json()
}
