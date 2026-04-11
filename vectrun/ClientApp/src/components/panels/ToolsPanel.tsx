import { useState } from 'react'
import type { ToolConfig } from '../../types/workspace'

interface Props {
  tools: ToolConfig[]
  onChange: (tools: ToolConfig[]) => void
  onSave: (tools: ToolConfig[]) => void
}

const emptyTool = (): ToolConfig => ({
  name: '',
  description: '',
  parameters: {},
  path: '',
  pathType: 'relative',
})

export function ToolsPanel({ tools, onChange, onSave }: Props) {
  const [editing, setEditing] = useState<ToolConfig | null>(null)
  const [isNew, setIsNew] = useState(false)
  const [form, setForm] = useState<ToolConfig>(emptyTool())
  const [paramsText, setParamsText] = useState('{}')
  const [paramsError, setParamsError] = useState<string | null>(null)

  function startNew() {
    const t = emptyTool()
    setForm(t)
    setParamsText(JSON.stringify(t.parameters, null, 2))
    setParamsError(null)
    setIsNew(true)
    setEditing(null)
  }

  function startEdit(tool: ToolConfig) {
    setForm({ ...tool })
    setParamsText(JSON.stringify(tool.parameters, null, 2))
    setParamsError(null)
    setEditing(tool)
    setIsNew(false)
  }

  function cancelEdit() {
    setEditing(null)
    setIsNew(false)
    setParamsError(null)
  }

  function handleParamsChange(text: string) {
    setParamsText(text)
    try {
      const parsed = JSON.parse(text)
      setForm(f => ({ ...f, parameters: parsed as Record<string, unknown> }))
      setParamsError(null)
    } catch {
      setParamsError('Invalid JSON')
    }
  }

  function saveForm() {
    if (paramsError) return
    const updated = isNew
      ? [...tools, form]
      : tools.map(t => t.name === editing?.name ? form : t)
    onChange(updated)
    cancelEdit()
    onSave(updated)
  }

  function deleteTool(name: string) {
    const updated = tools.filter(t => t.name !== name)
    onChange(updated)
    onSave(updated)
  }

  // ── Edit / New form view ───────────────────────────────────────────────────
  if (isNew || editing !== null) {
    return (
      <div className="flex flex-col h-full">
        <div className="flex items-center gap-2 px-3 py-2.5 border-b border-slate-100 shrink-0">
          <button
            onClick={cancelEdit}
            className="flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-medium text-slate-500 hover:bg-slate-100 hover:text-slate-800 transition"
          >
            <ChevronLeftIcon />
            All tools
          </button>
        </div>
        <div className="flex-1 overflow-y-auto">
          <div className="flex flex-col gap-3 p-4">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              {isNew ? 'New tool' : 'Edit tool'}
            </p>
            <Field label="Name">
              <input
                value={form.name}
                onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                className={inputClass}
              />
            </Field>
            <Field label="Description">
              <input
                value={form.description}
                onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                className={inputClass}
              />
            </Field>
            <Field label="Path type">
              <div className="flex gap-3">
                {(['relative', 'absolute'] as const).map(pt => (
                  <label key={pt} className="flex items-center gap-1.5 cursor-pointer">
                    <input
                      type="radio"
                      name="pathType"
                      value={pt}
                      checked={form.pathType === pt}
                      onChange={() => setForm(f => ({ ...f, pathType: pt }))}
                    />
                    <span className="text-sm text-slate-700 capitalize">{pt}</span>
                  </label>
                ))}
              </div>
            </Field>
            <Field label={form.pathType === 'relative' ? 'Path (relative to tools/)' : 'Path (absolute)'}>
              <input
                value={form.path}
                onChange={e => setForm(f => ({ ...f, path: e.target.value }))}
                className={inputClass}
              />
              {form.pathType === 'relative' && (
                <span className="text-xs text-slate-400">
                  Resolved as: <span className="font-mono">&lt;pipeline folder&gt;/tools/&lt;path&gt;</span>
                </span>
              )}
            </Field>
            <Field label="Parameters (JSON schema)">
              <textarea
                value={paramsText}
                onChange={e => handleParamsChange(e.target.value)}
                rows={5}
                className={`${inputClass} resize-y font-mono`}
              />
              {paramsError && (
                <span className="text-xs text-red-600">{paramsError}</span>
              )}
            </Field>
            <div className="flex gap-2 pt-1">
              <button
                onClick={saveForm}
                disabled={!!paramsError}
                className="rounded-lg bg-emerald-500 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-600 disabled:opacity-50"
              >
                Save
              </button>
              <button
                onClick={cancelEdit}
                className="rounded-lg border border-slate-200 px-3 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-50"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      </div>
    )
  }

  // ── List view ──────────────────────────────────────────────────────────────
  return (
    <div className="flex flex-col h-full overflow-hidden">
      <div className="flex items-center justify-between px-4 pt-4 pb-3 border-b border-slate-100 shrink-0">
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Tools</p>
        <button
          onClick={startNew}
          className="rounded-lg bg-emerald-500 px-3 py-1 text-xs font-semibold text-white hover:bg-emerald-600"
        >
          + Add tool
        </button>
      </div>
      <div className="flex-1 overflow-y-auto px-4 py-3">
        {tools.length === 0 ? (
          <p className="text-xs text-slate-400 italic">No tools configured.</p>
        ) : (
          <div className="flex flex-col gap-1">
            {tools.map(t => (
              <div
                key={t.name}
                className="group flex items-center gap-2 rounded-lg border border-transparent px-2.5 py-2 hover:bg-slate-50 hover:border-slate-100 transition"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-slate-700 truncate">{t.name}</p>
                  <p className="text-xs text-slate-400 truncate">{t.description}</p>
                </div>
                <div className="flex items-center gap-0.5 shrink-0 opacity-0 group-hover:opacity-100 transition">
                  <button
                    onClick={() => startEdit(t)}
                    className="rounded-md p-1.5 text-slate-400 hover:bg-white hover:text-slate-700 transition"
                    title="Edit tool"
                  >
                    <EditIcon />
                  </button>
                  <button
                    onClick={() => deleteTool(t.name)}
                    className="rounded-md p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600 transition"
                    title="Delete tool"
                  >
                    <TrashIcon />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs font-medium text-slate-500">{label}</span>
      {children}
    </label>
  )
}

const inputClass = 'rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100 w-full'

function ChevronLeftIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M12.707 5.293a1 1 0 010 1.414L9.414 10l3.293 3.293a1 1 0 01-1.414 1.414l-4-4a1 1 0 010-1.414l4-4a1 1 0 011.414 0z" clipRule="evenodd" />
    </svg>
  )
}

function EditIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor">
      <path d="M13.586 3.586a2 2 0 112.828 2.828l-.793.793-2.828-2.828.793-.793zM11.379 5.793L3 14.172V17h2.828l8.38-8.379-2.83-2.828z" />
    </svg>
  )
}

function TrashIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M9 2a1 1 0 00-.894.553L7.382 4H4a1 1 0 000 2v10a2 2 0 002 2h8a2 2 0 002-2V6a1 1 0 100-2h-3.382l-.724-1.447A1 1 0 0011 2H9zM7 8a1 1 0 012 0v6a1 1 0 11-2 0V8zm5-1a1 1 0 00-1 1v6a1 1 0 102 0V8a1 1 0 00-1-1z" clipRule="evenodd" />
    </svg>
  )
}
