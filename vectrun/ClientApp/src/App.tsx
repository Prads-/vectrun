import { useState } from 'react'
import type { Workspace } from './types/workspace'
import { loadWorkspace } from './api/workspace'
import { DirectoryInput } from './components/DirectoryInput'
import { ScaffoldDialog } from './components/ScaffoldDialog'
import { WorkspaceEditor } from './components/WorkspaceEditor'

type LoadStatus = 'idle' | 'loading' | 'loaded' | 'missing' | 'error'

export default function App() {
  const [directory, setDirectory] = useState('')
  const [workspace, setWorkspace] = useState<Workspace | null>(null)
  const [loadStatus, setLoadStatus] = useState<LoadStatus>('idle')
  const [loadError, setLoadError] = useState<string | null>(null)

  async function handleLoad() {
    if (!directory.trim()) return
    setLoadStatus('loading')
    setLoadError(null)
    setWorkspace(null)
    try {
      const result = await loadWorkspace(directory.trim())
      if ('missing' in result) {
        setLoadStatus('missing')
      } else {
        setWorkspace(result.workspace)
        setLoadStatus('loaded')
      }
    } catch (err) {
      setLoadError(String(err))
      setLoadStatus('error')
    }
  }

  function handleScaffold(w: Workspace) {
    setWorkspace(w)
    setLoadStatus('loaded')
  }

  function handleCancelScaffold() {
    setLoadStatus('idle')
  }

  function handleSaved(w: Workspace) {
    setWorkspace(w)
  }

  return (
    <div className="flex h-screen flex-col bg-slate-50 text-slate-900" style={{ fontFamily: "'DM Sans', system-ui, sans-serif" }}>
      {/* Header */}
      <header className="flex items-center gap-4 border-b border-slate-200 bg-white px-4 py-2.5 shadow-sm shrink-0 z-10">
        <div className="flex items-center gap-2 shrink-0">
          <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-slate-900 text-white text-xs font-bold tracking-tight">v</span>
          <span className="text-sm font-bold tracking-tight text-slate-900 select-none">vectrun</span>
        </div>
        <div className="w-px h-5 bg-slate-200 shrink-0" />
        <div className="flex flex-1 items-center gap-2">
          <DirectoryInput
            value={directory}
            onChange={setDirectory}
            onSubmit={handleLoad}
          />
          <button
            onClick={handleLoad}
            disabled={loadStatus === 'loading' || !directory.trim()}
            className="rounded-lg bg-slate-900 px-4 py-1.5 text-xs font-semibold text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50 shrink-0"
          >
            {loadStatus === 'loading' ? 'Loading…' : 'Load'}
          </button>
        </div>
        {workspace && (
          <span className="shrink-0 text-xs font-medium text-slate-400 font-mono">{workspace.pipeline.pipelineName}</span>
        )}
      </header>

      {/* Main — overflow-hidden + h-0 flex-1 forces browsers to compute a real pixel height
           that h-full children can reference, rather than collapsing to content size */}
      <div className="flex min-h-0 flex-1 overflow-hidden">
        {loadStatus === 'idle' && (
          <div className="flex flex-1 items-center justify-center">
            <div className="text-center">
              <p className="text-sm font-medium text-slate-400">Enter a pipeline directory to get started.</p>
            </div>
          </div>
        )}
        {loadStatus === 'loading' && (
          <div className="flex flex-1 items-center justify-center">
            <p className="text-sm text-slate-400">Loading…</p>
          </div>
        )}
        {loadStatus === 'error' && loadError && (
          <div className="flex flex-1 items-center justify-center p-8">
            <div className="rounded-xl bg-red-50 border border-red-100 px-6 py-4 text-sm text-red-700 shadow-sm max-w-md">
              {loadError}
            </div>
          </div>
        )}
        {loadStatus === 'loaded' && workspace && (
          <WorkspaceEditor
            workspace={workspace}
            directory={directory.trim()}
            onSaved={handleSaved}
          />
        )}
      </div>

      {/* Scaffold dialog */}
      {loadStatus === 'missing' && (
        <ScaffoldDialog
          directory={directory.trim()}
          onScaffold={handleScaffold}
          onCancel={handleCancelScaffold}
        />
      )}
    </div>
  )
}
