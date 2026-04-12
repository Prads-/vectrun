export async function getRecents(): Promise<string[]> {
  const res = await fetch('/api/recents')
  if (!res.ok) return []
  return res.json()
}

export async function addRecent(path: string): Promise<string[]> {
  const res = await fetch('/api/recents', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  })
  if (!res.ok) return []
  return res.json()
}

export async function removeRecent(path: string): Promise<string[]> {
  const res = await fetch('/api/recents', {
    method: 'DELETE',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  })
  if (!res.ok) return []
  return res.json()
}
