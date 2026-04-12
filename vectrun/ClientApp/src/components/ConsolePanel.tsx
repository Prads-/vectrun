import { useEffect, useRef, useState, useMemo, useCallback } from 'react'
import type { LogEntry, LogEvent } from '../types/console'

interface Props {
  logs: LogEntry[]
  isRunning: boolean
  open: boolean
  onToggle: () => void
  onClear: () => void
}

// ── Styling helpers ────────────────────────────────────────────────────────

const NODE_TYPE_STYLE: Record<string, { dot: string; badge: string; check: string }> = {
  agent:  { dot: 'bg-blue-400',    badge: 'bg-blue-900/60 text-blue-300 border-blue-700',       check: 'accent-blue-400' },
  branch: { dot: 'bg-amber-400',   badge: 'bg-amber-900/60 text-amber-300 border-amber-700',     check: 'accent-amber-400' },
  logic:  { dot: 'bg-emerald-400', badge: 'bg-emerald-900/60 text-emerald-300 border-emerald-700', check: 'accent-emerald-400' },
  wait:   { dot: 'bg-violet-400',  badge: 'bg-violet-900/60 text-violet-300 border-violet-700',  check: 'accent-violet-400' },
  system: { dot: 'bg-slate-500',   badge: 'bg-slate-800 text-slate-400 border-slate-600',        check: 'accent-slate-400' },
}

const EVENT_STYLE: Record<LogEvent, { label: string; color: string }> = {
  started:     { label: 'started',     color: 'text-slate-400' },
  output:      { label: 'output',      color: 'text-sky-400' },
  tool_call:   { label: 'tool call',   color: 'text-amber-400' },
  tool_result: { label: 'tool result', color: 'text-emerald-400' },
  error:       { label: 'error',       color: 'text-red-400' },
}

function nodeStyle(type: string) {
  return NODE_TYPE_STYLE[type] ?? NODE_TYPE_STYLE.system
}

function formatTime(iso: string) {
  const d = new Date(iso)
  const hh = d.getHours().toString().padStart(2, '0')
  const mm = d.getMinutes().toString().padStart(2, '0')
  const ss = d.getSeconds().toString().padStart(2, '0')
  const ms = d.getMilliseconds().toString().padStart(3, '0')
  return `${hh}:${mm}:${ss}.${ms}`
}

// ── Markdown export ────────────────────────────────────────────────────────

function saveAsMarkdown(visibleLogs: LogEntry[], filterLabel: string) {
  const now = new Date()
  const dateStr = now.toISOString().replace('T', ' ').slice(0, 19) + ' UTC'

  const lines: string[] = [
    '# Pipeline Output Log',
    '',
    `**Generated:** ${dateStr}`,
    `**Filter:** ${filterLabel}`,
    `**Entries:** ${visibleLogs.length}`,
    '',
    '---',
    '',
  ]

  for (const entry of visibleLogs) {
    const label = entry.nodeName ?? entry.nodeId
    const time = formatTime(entry.timestamp)
    const eventLabel = EVENT_STYLE[entry.event]?.label ?? entry.event

    lines.push(`### ${time}  ·  ${label}  ·  ${entry.nodeType}`)
    lines.push('')
    lines.push(`**${eventLabel}**`)

    if (entry.message) {
      lines.push('')
      // JSON-looking content or short single-liners → code block
      // Prose (output / tool_result with plain text) → blockquote
      const isJson = entry.message.trimStart().startsWith('{') || entry.message.trimStart().startsWith('[')
      const isMultiline = entry.message.includes('\n')
      if (isJson || (!isMultiline && entry.message.length <= 120)) {
        lines.push('```')
        lines.push(entry.message)
        lines.push('```')
      } else {
        // Prose — use blockquote, one line at a time so markdown renders correctly
        for (const l of entry.message.split('\n')) {
          lines.push(`> ${l}`)
        }
      }
    }

    lines.push('')
    lines.push('---')
    lines.push('')
  }

  const content = lines.join('\n')
  const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `pipeline-log-${now.toISOString().slice(0, 19).replace(/:/g, '-')}.md`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

// ── Component ──────────────────────────────────────────────────────────────

export function ConsolePanel({ logs, isRunning, open, onToggle, onClear }: Props) {
  // Empty set = show all; non-empty = show only those node IDs
  const [includedNodeIds, setIncludedNodeIds] = useState<Set<string>>(new Set())
  const [filterOpen, setFilterOpen] = useState(false)
  const [autoScroll, setAutoScroll] = useState(true)
  const [panelHeight, setPanelHeight] = useState(() => {
    const saved = localStorage.getItem('vectrun_console_height')
    return saved ? Math.max(120, Math.min(800, parseInt(saved, 10))) : 260
  })
  const scrollRef = useRef<HTMLDivElement>(null)
  const filterRef = useRef<HTMLDivElement>(null)
  const dragStartY = useRef<number>(0)
  const dragStartHeight = useRef<number>(0)

  const handleResizeStart = useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    dragStartY.current = e.clientY
    dragStartHeight.current = panelHeight

    function onMove(ev: MouseEvent) {
      const delta = dragStartY.current - ev.clientY
      const next = Math.max(120, Math.min(800, dragStartHeight.current + delta))
      setPanelHeight(next)
    }
    function onUp() {
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
      document.body.style.cursor = ''
      document.body.style.userSelect = ''
    }

    document.addEventListener('mousemove', onMove)
    document.addEventListener('mouseup', onUp)
    document.body.style.cursor = 'ns-resize'
    document.body.style.userSelect = 'none'
  }, [panelHeight])

  // Persist height to localStorage
  useEffect(() => {
    localStorage.setItem('vectrun_console_height', String(panelHeight))
  }, [panelHeight])

  // Collect unique nodes that have appeared in logs
  const nodeOptions = useMemo(() => {
    const seen = new Map<string, { id: string; name: string | null; type: string }>()
    for (const l of logs) {
      if (l.nodeId !== 'pipeline' && !seen.has(l.nodeId)) {
        seen.set(l.nodeId, { id: l.nodeId, name: l.nodeName, type: l.nodeType })
      }
    }
    return [...seen.values()]
  }, [logs])

  // Reset filter when logs are cleared
  useEffect(() => {
    if (logs.length === 0) {
      setIncludedNodeIds(new Set())
      setFilterOpen(false)
    }
  }, [logs.length])

  // Close filter dropdown on outside click
  useEffect(() => {
    if (!filterOpen) return
    function handleClick(e: MouseEvent) {
      if (filterRef.current && !filterRef.current.contains(e.target as Node)) {
        setFilterOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [filterOpen])

  // Auto-scroll to bottom when new entries arrive
  useEffect(() => {
    if (autoScroll && open && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [logs, autoScroll, open])

  function handleScroll() {
    const el = scrollRef.current
    if (!el) return
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 8
    setAutoScroll(atBottom)
  }

  function toggleNode(id: string) {
    setIncludedNodeIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function selectAll() {
    setIncludedNodeIds(new Set())
  }

  function clearAll() {
    setIncludedNodeIds(new Set(nodeOptions.map(n => n.id)))
  }

  const isFiltered = includedNodeIds.size > 0
  const allSelected = !isFiltered
  const selectedCount = isFiltered
    ? includedNodeIds.size
    : nodeOptions.length

  const visibleLogs = !isFiltered
    ? logs
    : logs.filter(l => includedNodeIds.has(l.nodeId))

  const filterLabel = allSelected
    ? 'All nodes'
    : `${selectedCount} of ${nodeOptions.length} nodes`

  return (
    <div
      className="shrink-0 flex flex-col bg-slate-950"
      style={{ height: open ? panelHeight : 34 }}
    >
      {/* Resize handle — only shown when open */}
      {open && (
        <div
          onMouseDown={handleResizeStart}
          className="h-1 shrink-0 cursor-ns-resize bg-slate-800 hover:bg-sky-500/60 transition-colors"
          title="Drag to resize"
        />
      )}

      {/* Header bar */}
      <div className="flex h-[34px] shrink-0 items-center gap-3 px-3 border-t border-b border-slate-800">
        {/* Title + running indicator */}
        <button
          onClick={onToggle}
          className="flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-slate-400 hover:text-slate-200 transition select-none"
        >
          <TerminalIcon />
          Output
          {isRunning && (
            <span className="flex items-center gap-1 text-emerald-400">
              <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
              running
            </span>
          )}
          {!isRunning && logs.length > 0 && (
            <span className="rounded-full bg-slate-800 px-1.5 py-px text-[10px] text-slate-500 font-mono">
              {logs.length}
            </span>
          )}
        </button>

        <div className="flex-1" />

        {/* Multi-node filter */}
        {logs.length > 0 && open && nodeOptions.length > 0 && (
          <div ref={filterRef} className="relative">
            <button
              onClick={() => setFilterOpen(o => !o)}
              className={`flex items-center gap-1.5 rounded-md border px-2 py-0.5 text-xs transition ${
                isFiltered
                  ? 'border-sky-700 bg-sky-900/40 text-sky-300'
                  : 'border-slate-700 bg-slate-900 text-slate-300 hover:border-slate-500'
              }`}
            >
              <FilterIcon />
              {filterLabel}
              <ChevronUpIcon />
            </button>

            {filterOpen && (
              <div className="absolute bottom-full right-0 mb-1 w-56 rounded-lg border border-slate-700 bg-slate-900 shadow-xl z-50 overflow-hidden">
                {/* Select all / Clear header */}
                <div className="flex items-center justify-between px-3 py-2 border-b border-slate-800">
                  <span className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Filter nodes</span>
                  <div className="flex gap-2">
                    <button
                      onClick={selectAll}
                      className="text-[10px] text-slate-400 hover:text-slate-200 transition"
                    >
                      All
                    </button>
                    <span className="text-slate-700">·</span>
                    <button
                      onClick={clearAll}
                      className="text-[10px] text-slate-400 hover:text-slate-200 transition"
                    >
                      None
                    </button>
                  </div>
                </div>

                {/* Node list */}
                <div className="max-h-48 overflow-y-auto py-1">
                  {nodeOptions.map(n => {
                    const ns = nodeStyle(n.type)
                    const checked = !isFiltered || includedNodeIds.has(n.id)
                    return (
                      <label
                        key={n.id}
                        className="flex items-center gap-2.5 px-3 py-1.5 cursor-pointer hover:bg-slate-800 transition"
                      >
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={() => toggleNode(n.id)}
                          className={`rounded ${ns.check} shrink-0`}
                        />
                        <span className={`inline-flex items-center gap-1 rounded border px-1.5 py-px text-[10px] font-semibold shrink-0 ${ns.badge}`}>
                          <span className={`h-1.5 w-1.5 rounded-full ${ns.dot}`} />
                          {n.type}
                        </span>
                        <span className="text-xs text-slate-300 truncate">
                          {n.name ?? n.id}
                        </span>
                      </label>
                    )
                  })}
                </div>
              </div>
            )}
          </div>
        )}

        {/* Save as markdown */}
        {visibleLogs.length > 0 && open && (
          <button
            onClick={() => saveAsMarkdown(visibleLogs, filterLabel)}
            className="rounded-md p-0.5 text-slate-500 hover:bg-slate-800 hover:text-slate-300 transition"
            title="Save log as Markdown"
          >
            <SaveLogIcon />
          </button>
        )}

        {/* Clear */}
        {logs.length > 0 && (
          <button
            onClick={onClear}
            className="rounded-md px-2 py-0.5 text-xs text-slate-500 hover:bg-slate-800 hover:text-slate-300 transition"
            title="Clear output"
          >
            Clear
          </button>
        )}

        {/* Collapse / expand toggle */}
        <button
          onClick={onToggle}
          className="rounded-md p-0.5 text-slate-500 hover:bg-slate-800 hover:text-slate-300 transition"
          title={open ? 'Collapse' : 'Expand'}
        >
          {open ? <ChevronDownIcon /> : <ChevronUpSmallIcon />}
        </button>
      </div>

      {/* Log area */}
      {open && (
        <div
          ref={scrollRef}
          onScroll={handleScroll}
          className="flex-1 overflow-y-auto px-3 py-2 space-y-px"
          style={{ fontFamily: "'JetBrains Mono', 'Courier New', monospace" }}
        >
          {visibleLogs.length === 0 ? (
            <p className="text-xs text-slate-600 italic pt-2">
              {logs.length === 0
                ? 'No output yet. Run the pipeline to see logs here.'
                : 'No entries match the selected filter.'}
            </p>
          ) : (
            visibleLogs.map((entry, i) => (
              <LogRow key={i} entry={entry} />
            ))
          )}
        </div>
      )}
    </div>
  )
}

function LogRow({ entry }: { entry: LogEntry }) {
  const ns = nodeStyle(entry.nodeType)
  const ev = EVENT_STYLE[entry.event] ?? { label: entry.event, color: 'text-slate-400' }
  const label = entry.nodeName ?? entry.nodeId

  return (
    <div className="flex flex-col py-0.5 gap-0">
      <div className="flex items-baseline gap-2 text-xs leading-5">
        <span className="shrink-0 text-slate-600 tabular-nums">
          {formatTime(entry.timestamp)}
        </span>
        <span className={`shrink-0 inline-flex items-center gap-1 rounded border px-1.5 py-px text-[10px] font-semibold ${ns.badge}`}>
          <span className={`h-1.5 w-1.5 rounded-full ${ns.dot}`} />
          {label}
        </span>
        <span className={`shrink-0 text-[10px] font-semibold uppercase tracking-wide ${ev.color}`}>
          {ev.label}
        </span>
        {entry.message && !entry.message.includes('\n') && entry.message.length <= 120 && (
          <span className="text-slate-300 truncate">{entry.message}</span>
        )}
      </div>
      {entry.message && (entry.message.includes('\n') || entry.message.length > 120) && (
        <pre className="mt-0.5 ml-[calc(6.5rem+8px)] whitespace-pre-wrap break-all text-[11px] text-slate-300 leading-relaxed">
          {entry.message}
        </pre>
      )}
    </div>
  )
}

// ── Icons ──────────────────────────────────────────────────────────────────

function TerminalIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M2 5a2 2 0 012-2h12a2 2 0 012 2v10a2 2 0 01-2 2H4a2 2 0 01-2-2V5zm3.293 1.293a1 1 0 011.414 0l3 3a1 1 0 010 1.414l-3 3a1 1 0 01-1.414-1.414L7.586 10 5.293 7.707a1 1 0 010-1.414zM11 12a1 1 0 100 2h3a1 1 0 100-2h-3z" clipRule="evenodd" />
    </svg>
  )
}

function FilterIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M3 3a1 1 0 011-1h12a1 1 0 011 1v3a1 1 0 01-.293.707L13 10.414V17a1 1 0 01-.553.894l-4-2A1 1 0 017 15v-4.586L3.293 6.707A1 1 0 013 6V3z" clipRule="evenodd" />
    </svg>
  )
}

function ChevronUpIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M14.707 12.707a1 1 0 01-1.414 0L10 9.414l-3.293 3.293a1 1 0 01-1.414-1.414l4-4a1 1 0 011.414 0l4 4a1 1 0 010 1.414z" clipRule="evenodd" />
    </svg>
  )
}

function ChevronUpSmallIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M14.707 12.707a1 1 0 01-1.414 0L10 9.414l-3.293 3.293a1 1 0 01-1.414-1.414l4-4a1 1 0 011.414 0l4 4a1 1 0 010 1.414z" clipRule="evenodd" />
    </svg>
  )
}

function ChevronDownIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clipRule="evenodd" />
    </svg>
  )
}

function SaveLogIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 17V3" />
      <path d="M7 12l5 5 5-5" />
      <path d="M20 21H4" />
    </svg>
  )
}
