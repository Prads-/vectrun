import { useState, useEffect, useRef, useCallback } from 'react'
import { browse } from '../api/browse'
import type { BrowseEntry } from '../api/browse'

interface Props {
  value: string
  onChange: (path: string) => void
  onSubmit: () => void
}

export function DirectoryInput({ value, onChange, onSubmit }: Props) {
  const [open, setOpen] = useState(false)
  const [current, setCurrent] = useState<string | null>(null)
  const [parent, setParent] = useState<string | null>(null)
  const [entries, setEntries] = useState<BrowseEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  const load = useCallback(async (path?: string) => {
    setLoading(true)
    setError(null)
    try {
      const res = await browse(path)
      setCurrent(res.current)
      setParent(res.parent)
      setEntries(res.entries)
    } catch (err) {
      setError(String(err))
    } finally {
      setLoading(false)
    }
  }, [])

  function openBrowser() {
    setOpen(true)
    load(value || undefined)
  }

  function navigate(path: string) {
    load(path)
  }

  function select(path: string) {
    onChange(path)
    setOpen(false)
  }

  // Close on outside click
  useEffect(() => {
    if (!open) return
    function handleClick(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [open])

  return (
    <div ref={containerRef} className="relative flex-1">
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onFocus={openBrowser}
        onKeyDown={(e) => {
          if (e.key === 'Enter') { setOpen(false); onSubmit() }
          if (e.key === 'Escape') setOpen(false)
        }}
        placeholder="Pipeline directory path…"
        className="w-full rounded-lg border border-slate-200 bg-slate-50 px-3 py-1.5 text-sm text-slate-700 placeholder-slate-400 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
      />

      {open && (
        <div className="absolute top-full left-0 right-0 z-50 mt-1 rounded-xl border border-slate-200 bg-white shadow-xl overflow-hidden">
          {/* Breadcrumb */}
          <div className="flex items-center gap-1 border-b border-slate-100 px-3 py-2 bg-slate-50">
            {loading && (
              <span className="text-xs text-slate-400">Loading…</span>
            )}
            {!loading && current && (
              <span className="text-xs font-mono text-slate-500 truncate">{current}</span>
            )}
            {!loading && current && (
              <button
                onClick={() => select(current)}
                className="ml-auto shrink-0 rounded-md bg-blue-500 px-3 py-1 text-xs font-semibold text-white hover:bg-blue-600"
              >
                Select
              </button>
            )}
          </div>

          {/* Error */}
          {error && (
            <div className="px-3 py-2 text-xs text-red-600">{error}</div>
          )}

          {/* Entries */}
          {!loading && (
            <ul className="max-h-64 overflow-y-auto py-1">
              {parent && (
                <li>
                  <button
                    onClick={() => navigate(parent)}
                    className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-500 hover:bg-slate-50"
                  >
                    <FolderIcon className="shrink-0 text-slate-400" />
                    <span className="font-mono">..</span>
                  </button>
                </li>
              )}
              {entries.length === 0 && !error && (
                <li className="px-3 py-2 text-xs text-slate-400">No subdirectories</li>
              )}
              {entries.map((entry) => (
                <li key={entry.path}>
                  <button
                    onClick={() => navigate(entry.path)}
                    className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-700 hover:bg-blue-50 hover:text-blue-700"
                  >
                    <FolderIcon className="shrink-0 text-amber-400" />
                    <span className="truncate">{entry.name}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}

function FolderIcon({ className }: { className?: string }) {
  return (
    <svg className={`h-4 w-4 ${className ?? ''}`} viewBox="0 0 20 20" fill="currentColor">
      <path d="M2 6a2 2 0 012-2h4l2 2h6a2 2 0 012 2v6a2 2 0 01-2 2H4a2 2 0 01-2-2V6z" />
    </svg>
  )
}
