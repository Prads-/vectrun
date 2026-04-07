import { useState } from 'react'
import type { Workspace } from '../types/workspace'
import { scaffoldWorkspace } from '../api/workspace'

interface Props {
  directory: string
  onScaffold: (workspace: Workspace) => void
  onCancel: () => void
}

export function ScaffoldDialog({ directory, onScaffold, onCancel }: Props) {
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleCreate() {
    setLoading(true)
    setError(null)
    try {
      const workspace = await scaffoldWorkspace(directory)
      onScaffold(workspace)
    } catch (err) {
      setError(String(err))
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-md rounded-2xl bg-white p-8 shadow-2xl">
        <h2 className="mb-2 text-lg font-bold text-slate-800">No workspace found</h2>
        <p className="mb-6 text-sm text-slate-500">
          This directory doesn&apos;t have a vectrun workspace. Create one?
        </p>
        <p className="mb-6 rounded-lg bg-slate-50 px-3 py-2 font-mono text-xs text-slate-600 break-all">
          {directory}
        </p>
        {error && (
          <div className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}
        <div className="flex justify-end gap-3">
          <button
            onClick={onCancel}
            disabled={loading}
            className="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={handleCreate}
            disabled={loading}
            className="rounded-lg bg-blue-500 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-600 disabled:opacity-50"
          >
            {loading ? 'Creating…' : 'Create workspace'}
          </button>
        </div>
      </div>
    </div>
  )
}
