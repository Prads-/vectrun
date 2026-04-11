import { useState } from 'react'

interface Props {
  isRunning: boolean
  onRun: (input: string) => void
  onStop: () => void
}

export function RunPanel({ isRunning, onRun, onStop }: Props) {
  const [input, setInput] = useState('')

  return (
    <div className="flex flex-col gap-3">
      <textarea
        value={input}
        onChange={(e) => setInput(e.target.value)}
        placeholder="Initial input (optional)"
        rows={3}
        disabled={isRunning}
        className="w-full resize-none rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 placeholder-slate-400 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100 disabled:bg-slate-50 disabled:text-slate-400"
      />

      {isRunning ? (
        <button
          onClick={onStop}
          className="rounded-lg bg-red-500 px-4 py-2 text-sm font-semibold text-white transition hover:bg-red-600"
        >
          Stop pipeline
        </button>
      ) : (
        <button
          onClick={() => onRun(input)}
          className="rounded-lg bg-blue-500 px-4 py-2 text-sm font-semibold text-white transition hover:bg-blue-600"
        >
          Run pipeline
        </button>
      )}
    </div>
  )
}
