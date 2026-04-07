import { useEffect, useRef, useState, useMemo } from 'react'
import type { LogEntry, LogEvent } from '../types/console'

interface Props {
  logs: LogEntry[]
  isRunning: boolean
  open: boolean
  onToggle: () => void
  onClear: () => void
}

// ── Styling helpers ────────────────────────────────────────────────────────

const NODE_TYPE_STYLE: Record<string, { dot: string; badge: string }> = {
  agent:  { dot: 'bg-blue-400',    badge: 'bg-blue-900/60 text-blue-300 border-blue-700' },
  branch: { dot: 'bg-amber-400',   badge: 'bg-amber-900/60 text-amber-300 border-amber-700' },
  logic:  { dot: 'bg-emerald-400', badge: 'bg-emerald-900/60 text-emerald-300 border-emerald-700' },
  wait:   { dot: 'bg-violet-400',  badge: 'bg-violet-900/60 text-violet-300 border-violet-700' },
  system: { dot: 'bg-slate-500',   badge: 'bg-slate-800 text-slate-400 border-slate-600' },
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

// ── Component ──────────────────────────────────────────────────────────────

export function ConsolePanel({ logs, isRunning, open, onToggle, onClear }: Props) {
  const [filterNodeId, setFilterNodeId] = useState<string>('all')
  const [autoScroll, setAutoScroll] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)

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
    if (logs.length === 0) setFilterNodeId('all')
  }, [logs.length])

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

  const visibleLogs = filterNodeId === 'all'
    ? logs
    : logs.filter(l => l.nodeId === filterNodeId)

  return (
    <div
      className="shrink-0 flex flex-col border-t border-slate-700 bg-slate-950"
      style={{ height: open ? 260 : 34 }}
    >
      {/* Header bar */}
      <div className="flex h-[34px] shrink-0 items-center gap-3 px-3 border-b border-slate-800">
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

        {/* Spacer */}
        <div className="flex-1" />

        {/* Node filter */}
        {logs.length > 0 && open && (
          <select
            value={filterNodeId}
            onChange={e => setFilterNodeId(e.target.value)}
            className="rounded-md border border-slate-700 bg-slate-900 px-2 py-0.5 text-xs text-slate-300 focus:outline-none focus:border-slate-500 cursor-pointer"
          >
            <option value="all">All nodes</option>
            {nodeOptions.map(n => (
              <option key={n.id} value={n.id}>
                {n.name ? `${n.name} (${n.id})` : n.id}
              </option>
            ))}
          </select>
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
          {open ? <ChevronDownIcon /> : <ChevronUpIcon />}
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
        {/* Timestamp */}
        <span className="shrink-0 text-slate-600 tabular-nums">
          {formatTime(entry.timestamp)}
        </span>

        {/* Node badge */}
        <span className={`shrink-0 inline-flex items-center gap-1 rounded border px-1.5 py-px text-[10px] font-semibold ${ns.badge}`}>
          <span className={`h-1.5 w-1.5 rounded-full ${ns.dot}`} />
          {label}
        </span>

        {/* Event label */}
        <span className={`shrink-0 text-[10px] font-semibold uppercase tracking-wide ${ev.color}`}>
          {ev.label}
        </span>

        {/* Inline message for short single-line values */}
        {entry.message && !entry.message.includes('\n') && entry.message.length <= 120 && (
          <span className="text-slate-300 truncate">{entry.message}</span>
        )}
      </div>

      {/* Multi-line or long message on its own line */}
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

function ChevronUpIcon() {
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
