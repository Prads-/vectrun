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
  const [selected, setSelected] = useState<ModelConfig | null>(null)
  const [isNew, setIsNew] = useState(false)
  const [form, setForm] = useState<ModelConfig>(emptyModel())

  function startNew() {
    setForm(emptyModel())
    setIsNew(true)
    setSelected(null)
  }

  function startEdit(model: ModelConfig) {
    setForm({ ...model })
    setSelected(model)
    setIsNew(false)
  }

  function cancelEdit() {
    setSelected(null)
    setIsNew(false)
  }

  function saveForm() {
    const updated = isNew
      ? [...models, form]
      : models.map(m => m.id === selected?.id ? form : m)
    onChange(updated)
    cancelEdit()
    onSave(updated)
  }

  function deleteModel(id: string) {
    const updated = models.filter(m => m.id !== id)
    onChange(updated)
    if (selected?.id === id) cancelEdit()
    onSave(updated)
  }

  const showForm = isNew || selected !== null

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between px-4 pt-4 pb-3 shrink-0">
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Models</p>
        <button
          onClick={startNew}
          className="rounded-lg bg-blue-500 px-3 py-1 text-xs font-semibold text-white hover:bg-blue-600"
        >
          + Add model
        </button>
      </div>

      <div className="flex-1 overflow-y-auto">
      <div className="flex flex-col gap-4 px-4 pb-4">
      <div className="flex flex-col gap-1">
        {models.length === 0 && !showForm && (
          <p className="text-xs text-slate-400 italic">No models configured.</p>
        )}
        {models.map(m => (
          <div
            key={m.id}
            className={`flex items-center gap-2 rounded-lg px-3 py-2 cursor-pointer transition ${selected?.id === m.id ? 'bg-blue-50 border border-blue-200' : 'border border-transparent hover:bg-slate-50'}`}
            onClick={() => startEdit(m)}
          >
            <div className="flex-1 min-w-0">
              <p className="text-sm font-medium text-slate-700 truncate">{m.name || m.id}</p>
              <p className="text-xs text-slate-400 truncate font-mono">{m.id}</p>
            </div>
            <span className="shrink-0 rounded px-1.5 py-0.5 text-xs font-semibold bg-slate-100 text-slate-600">{m.type}</span>
            <button
              onClick={e => { e.stopPropagation(); deleteModel(m.id) }}
              className="shrink-0 rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600 transition"
            >
              <TrashIcon />
            </button>
          </div>
        ))}
      </div>

      {showForm && (
        <div className="flex flex-col gap-3 border-t border-slate-100 pt-4 overflow-hidden">
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
      )}
      </div>
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

function TrashIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M9 2a1 1 0 00-.894.553L7.382 4H4a1 1 0 000 2v10a2 2 0 002 2h8a2 2 0 002-2V6a1 1 0 100-2h-3.382l-.724-1.447A1 1 0 0011 2H9zM7 8a1 1 0 012 0v6a1 1 0 11-2 0V8zm5-1a1 1 0 00-1 1v6a1 1 0 102 0V8a1 1 0 00-1-1z" clipRule="evenodd" />
    </svg>
  )
}
