import { useState } from 'react'
import type { ModelConfig } from '../../types/workspace'

interface Props {
  models: ModelConfig[]
  onChange: (models: ModelConfig[]) => void
  onSave: (models: ModelConfig[]) => void
}

const MODEL_TYPES: ModelConfig['type'][] = ['ollama', 'vllm', 'llama.cpp', 'open_ai', 'anthropic']

const emptyModel = (): ModelConfig => ({
  id: '',
  name: '',
  type: 'ollama',
  endpoint: '',
  apiKey: '',
})

export function ModelsPanel({ models, onChange, onSave }: Props) {
  const [editing, setEditing] = useState<ModelConfig | null>(null)
  const [isNew, setIsNew] = useState(false)
  const [form, setForm] = useState<ModelConfig>(emptyModel())

  function startNew() {
    setForm(emptyModel())
    setIsNew(true)
    setEditing(null)
  }

  function startEdit(model: ModelConfig) {
    setForm({ ...model })
    setEditing(model)
    setIsNew(false)
  }

  function cancelEdit() {
    setEditing(null)
    setIsNew(false)
  }

  function saveForm() {
    const updated = isNew
      ? [...models, form]
      : models.map(m => m.id === editing?.id ? form : m)
    onChange(updated)
    cancelEdit()
    onSave(updated)
  }

  function deleteModel(id: string) {
    const updated = models.filter(m => m.id !== id)
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
            All models
          </button>
        </div>
        <div className="flex-1 overflow-y-auto">
          <div className="flex flex-col gap-3 p-4">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              {isNew ? 'New model' : 'Edit model'}
            </p>
            <Field label="ID">
              <input
                value={form.id}
                onChange={e => setForm(f => ({ ...f, id: e.target.value }))}
                className={inputClass}
              />
            </Field>
            <Field label="Name">
              <input
                value={form.name}
                onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                className={inputClass}
              />
            </Field>
            <Field label="Type">
              <select
                value={form.type}
                onChange={e => setForm(f => ({ ...f, type: e.target.value as ModelConfig['type'] }))}
                className={inputClass}
              >
                {MODEL_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
              </select>
            </Field>
            <Field label="Endpoint">
              <input
                value={form.endpoint}
                onChange={e => setForm(f => ({ ...f, endpoint: e.target.value }))}
                className={inputClass}
              />
            </Field>
            <Field label="API key (optional)">
              <input
                value={form.apiKey ?? ''}
                onChange={e => setForm(f => ({ ...f, apiKey: e.target.value }))}
                className={inputClass}
              />
            </Field>
            <div className="flex gap-2 pt-1">
              <button
                onClick={saveForm}
                className="rounded-lg bg-blue-500 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-600"
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
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Models</p>
        <button
          onClick={startNew}
          className="rounded-lg bg-blue-500 px-3 py-1 text-xs font-semibold text-white hover:bg-blue-600"
        >
          + Add model
        </button>
      </div>
      <div className="flex-1 overflow-y-auto px-4 py-3">
        {models.length === 0 ? (
          <p className="text-xs text-slate-400 italic">No models configured.</p>
        ) : (
          <div className="flex flex-col gap-1">
            {models.map(m => (
              <div
                key={m.id}
                className="group flex items-center gap-2 rounded-lg border border-transparent px-2.5 py-2 hover:bg-slate-50 hover:border-slate-100 transition"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-slate-700 truncate">{m.name || m.id}</p>
                  <p className="text-xs text-slate-400 truncate font-mono">{m.id}</p>
                  <span className="inline-block mt-0.5 rounded px-1.5 py-0.5 text-[10px] font-semibold bg-slate-100 text-slate-500">{m.type}</span>
                </div>
                <div className="flex items-center gap-0.5 shrink-0 opacity-0 group-hover:opacity-100 transition">
                  <button
                    onClick={() => startEdit(m)}
                    className="rounded-md p-1.5 text-slate-400 hover:bg-white hover:text-slate-700 transition"
                    title="Edit model"
                  >
                    <EditIcon />
                  </button>
                  <button
                    onClick={() => deleteModel(m.id)}
                    className="rounded-md p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600 transition"
                    title="Delete model"
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
