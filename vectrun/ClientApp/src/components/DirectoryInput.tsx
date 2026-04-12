import { useState, useEffect, useRef, useCallback } from 'react'
import { browse } from '../api/browse'
import type { BrowseEntry } from '../api/browse'
import { getRecents, addRecent, removeRecent } from '../api/recents'

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
  const [recents, setRecents] = useState<string[]>([])
  const containerRef = useRef<HTMLDivElement>(null)

  // Load recents from the server on mount
  useEffect(() => {
    getRecents().then(setRecents)
  }, [])

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

  async function select(path: string) {
    onChange(path)
    setRecents(await addRecent(path))
    setOpen(false)
  }

  async function handleSubmit() {
    if (value.trim()) setRecents(await addRecent(value.trim()))
    setOpen(false)
    onSubmit()
  }

  async function handleRemoveRecent(e: React.MouseEvent, path: string) {
    e.stopPropagation()
    setRecents(await removeRecent(path))
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
          if (e.key === 'Enter') handleSubmit()
          if (e.key === 'Escape') setOpen(false)
        }}
        placeholder="Pipeline directory path…"
        className="w-full rounded-lg border border-slate-200 bg-slate-50 px-3 py-1.5 text-sm text-slate-700 placeholder-slate-400 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
      />

      {open && (
        <div className="absolute top-full left-0 right-0 z-50 mt-1 rounded-xl border border-slate-200 bg-white shadow-xl overflow-hidden">

          {/* Recent paths */}
          {recents.length > 0 && (
            <div className="border-b border-slate-100">
              <p className="px-3 pt-2 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Recent</p>
              <ul className="py-1">
                {recents.map(path => (
                  <li key={path}>
                    <button
                      onClick={() => select(path)}
                      className="group flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-700 hover:bg-blue-50 hover:text-blue-700"
                    >
                      <ClockIcon className="shrink-0 text-slate-400 group-hover:text-blue-400" />
                      <span className="truncate font-mono text-xs">{path}</span>
                      <span
                        role="button"
                        onClick={(e) => handleRemoveRecent(e, path)}
                        className="ml-auto shrink-0 rounded p-0.5 text-slate-300 hover:bg-slate-100 hover:text-slate-500"
                        title="Remove from recents"
                      >
                        <XIcon />
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Breadcrumb */}
          <div className="flex items-center gap-1 border-b border-slate-100 px-3 py-2 bg-slate-50">
            {loading && <span className="text-xs text-slate-400">Loading…</span>}
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
          {error && <div className="px-3 py-2 text-xs text-red-600">{error}</div>}

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

function ClockIcon({ className }: { className?: string }) {
  return (
    <svg className={`h-4 w-4 ${className ?? ''}`} viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm1-12a1 1 0 10-2 0v4a1 1 0 00.293.707l2.828 2.829a1 1 0 101.415-1.415L11 9.586V6z" clipRule="evenodd" />
    </svg>
  )
}

function XIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clipRule="evenodd" />
    </svg>
  )
}
