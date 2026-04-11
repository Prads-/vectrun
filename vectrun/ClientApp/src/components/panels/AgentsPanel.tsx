import { useState } from 'react'
import type { AgentConfig, ModelConfig, ToolConfig } from '../../types/workspace'
import { DRAG_TYPE } from '../../lib/dragNode'
import type { NodeDragData } from '../../lib/dragNode'

interface Props {
  agents: AgentConfig[]
  models: ModelConfig[]
  tools: ToolConfig[]
  onChange: (agents: AgentConfig[]) => void
  onSave: (agents: AgentConfig[]) => void
  onAddToCanvas?: (agentId: string) => void
}

const emptyAgent = (): AgentConfig => ({
  agentName: '',
  modelId: '',
  output: 'plain_text',
  systemPrompt: '',
  prompt: '',
  toolIds: [],
})

export function AgentsPanel({ agents, models, tools, onChange, onSave, onAddToCanvas }: Props) {
  const [selected, setSelected] = useState<AgentConfig | null>(null)
  const [isNew, setIsNew] = useState(false)
  const [form, setForm] = useState<AgentConfig>(emptyAgent())
  const [schemaText, setSchemaText] = useState('')
  const [schemaError, setSchemaError] = useState<string | null>(null)

  function startNew() {
    setForm(emptyAgent())
    setSchemaText('')
    setSchemaError(null)
    setIsNew(true)
    setSelected(null)
  }

  function startEdit(agent: AgentConfig) {
    setForm({ ...agent, toolIds: [...(agent.toolIds ?? [])] })
    setSchemaText(agent.outputSchema != null ? JSON.stringify(agent.outputSchema, null, 2) : '')
    setSchemaError(null)
    setSelected(agent)
    setIsNew(false)
  }

  function cancelEdit() {
    setSelected(null)
    setIsNew(false)
    setSchemaError(null)
  }

  function handleSchemaChange(text: string) {
    setSchemaText(text)
    if (!text.trim()) { setSchemaError(null); return }
    try { JSON.parse(text); setSchemaError(null) }
    catch { setSchemaError('Invalid JSON') }
  }

  function saveForm() {
    if (schemaError) return

    let parsedSchema: unknown = undefined
    if (form.output === 'json' && schemaText.trim()) {
      try { parsedSchema = JSON.parse(schemaText) }
      catch { setSchemaError('Invalid JSON'); return }
    }

    const cleaned: AgentConfig = {
      ...form,
      systemPrompt: form.systemPrompt || undefined,
      prompt: form.prompt || undefined,
      toolIds: form.toolIds && form.toolIds.length > 0 ? form.toolIds : undefined,
      outputSchema: parsedSchema,
    }
    const updated = isNew
      ? [...agents, cleaned]
      : agents.map(a => a.agentName === selected?.agentName ? cleaned : a)
    onChange(updated)
    cancelEdit()
    onSave(updated)
  }

  function deleteAgent(name: string) {
    const updated = agents.filter(a => a.agentName !== name)
    onChange(updated)
    if (selected?.agentName === name) cancelEdit()
    onSave(updated)
  }

  function toggleTool(toolName: string) {
    const ids = form.toolIds ?? []
    const next = ids.includes(toolName)
      ? ids.filter(id => id !== toolName)
      : [...ids, toolName]
    setForm(f => ({ ...f, toolIds: next }))
  }

  const showForm = isNew || selected !== null

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between px-4 pt-4 pb-3 shrink-0">
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Agents</p>
        <button
          onClick={startNew}
          className="rounded-lg bg-violet-500 px-3 py-1 text-xs font-semibold text-white hover:bg-violet-600"
        >
          + Add agent
        </button>
      </div>

      <div className="flex-1 overflow-y-auto">
      <div className="flex flex-col gap-4 px-4 pb-4">
      <div className="flex flex-col gap-1">
        {agents.length === 0 && !showForm && (
          <p className="text-xs text-slate-400 italic">No agents configured.</p>
        )}
        {agents.map(a => (
          <div
            key={a.agentName}
            draggable
            onDragStart={e => {
              const data: NodeDragData = { nodeType: 'agent', agentId: a.agentName }
              e.dataTransfer.setData(DRAG_TYPE, JSON.stringify(data))
              e.dataTransfer.effectAllowed = 'move'
            }}
            className={`flex items-center gap-2 rounded-lg px-3 py-2 cursor-grab active:cursor-grabbing transition select-none ${selected?.agentName === a.agentName ? 'bg-violet-50 border border-violet-200' : 'border border-transparent hover:bg-slate-50'}`}
            onClick={() => startEdit(a)}
            title="Drag to add to canvas"
          >
            <GripIcon />
            <div className="flex-1 min-w-0">
              <p className="text-sm font-medium text-slate-700 truncate">{a.agentName}</p>
              <p className="text-xs text-slate-400 truncate">{a.modelId}</p>
            </div>
            {onAddToCanvas && (
              <button
                onClick={e => { e.stopPropagation(); onAddToCanvas(a.agentName) }}
                className="shrink-0 rounded p-1 text-slate-400 hover:bg-blue-50 hover:text-blue-600 transition"
                title="Add to canvas"
              >
                <PlusIcon />
              </button>
            )}
            <button
              onClick={e => { e.stopPropagation(); deleteAgent(a.agentName) }}
              className="shrink-0 rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600 transition"
            >
              <TrashIcon />
            </button>
          </div>
        ))}
      </div>

      {showForm && (
        <div className="flex flex-col gap-3 border-t border-slate-100 pt-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
            {isNew ? 'New agent' : 'Edit agent'}
          </p>
          <Field label="Agent name">
            <input
              value={form.agentName}
              onChange={e => setForm(f => ({ ...f, agentName: e.target.value }))}
              className={inputClass}
            />
          </Field>
          <Field label="Model">
            <select
              value={form.modelId}
              onChange={e => setForm(f => ({ ...f, modelId: e.target.value }))}
              className={inputClass}
            >
              <option value="">— select model —</option>
              {models.map(m => (
                <option key={m.id} value={m.id}>{m.name || m.id}</option>
              ))}
            </select>
          </Field>
          <Field label="Output">
            <select
              value={form.output}
              onChange={e => setForm(f => ({ ...f, output: e.target.value as AgentConfig['output'] }))}
              className={inputClass}
            >
              <option value="plain_text">plain_text</option>
              <option value="json">json</option>
            </select>
          </Field>
          {form.output === 'json' && (
            <div className="flex flex-col gap-1">
              <span className="text-xs font-medium text-slate-500">Output schema (optional JSON Schema)</span>
              <textarea
                value={schemaText}
                onChange={e => handleSchemaChange(e.target.value)}
                rows={6}
                className={`${inputClass} resize-y font-mono text-xs ${schemaError ? 'border-red-400 focus:border-red-400 focus:ring-red-100' : ''}`}
                placeholder={'{\n  "type": "object",\n  "properties": { ... }\n}'}
              />
              {schemaError && (
                <span className="text-xs text-red-500">{schemaError}</span>
              )}
            </div>
          )}
          <Field label="System prompt (optional)">
            <textarea
              value={form.systemPrompt ?? ''}
              onChange={e => setForm(f => ({ ...f, systemPrompt: e.target.value }))}
              rows={3}
              className={`${inputClass} resize-y`}
            />
          </Field>
          <Field label="Prompt template (optional)">
            <textarea
              value={form.prompt ?? ''}
              onChange={e => setForm(f => ({ ...f, prompt: e.target.value }))}
              rows={3}
              className={`${inputClass} resize-y`}
              placeholder="Use {PREVIOUS_AGENT_OUTPUT} to inject prior output"
            />
          </Field>
          {tools.length > 0 && (
            <div className="flex flex-col gap-1">
              <span className="text-xs font-medium text-slate-500">Tools</span>
              {tools.map(t => (
                <label key={t.name} className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={(form.toolIds ?? []).includes(t.name)}
                    onChange={() => toggleTool(t.name)}
                    className="rounded border-slate-300"
                  />
                  <span className="text-sm text-slate-700">{t.name}</span>
                </label>
              ))}
            </div>
          )}
          <div className="flex gap-2 pt-1">
            <button
              onClick={saveForm}
              disabled={!!schemaError}
              className="rounded-lg bg-violet-500 px-3 py-1.5 text-xs font-semibold text-white hover:bg-violet-600 disabled:opacity-50 disabled:cursor-not-allowed"
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

function GripIcon() {
  return (
    <svg className="h-3.5 w-3.5 text-slate-300 shrink-0" viewBox="0 0 20 20" fill="currentColor">
      <circle cx="7" cy="5" r="1.2" /><circle cx="13" cy="5" r="1.2" />
      <circle cx="7" cy="10" r="1.2" /><circle cx="13" cy="10" r="1.2" />
      <circle cx="7" cy="15" r="1.2" /><circle cx="13" cy="15" r="1.2" />
    </svg>
  )
}

function PlusIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M10 3a1 1 0 011 1v5h5a1 1 0 110 2h-5v5a1 1 0 11-2 0v-5H4a1 1 0 110-2h5V4a1 1 0 011-1z" clipRule="evenodd" />
    </svg>
  )
}

function TrashIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M9 2a1 1 0 00-.894.553L7.382 4H4a1 1 0 000 2v10a2 2 0 002 2h8a2 2 0 002-2V6a1 1 0 100-2h-3.382l-.724-1.447A1 1 0 0011 2H9zM7 8a1 1 0 012 0v6a1 1 0 11-2 0V8zm5-1a1 1 0 00-1 1v6a1 1 0 102 0V8a1 1 0 00-1-1z" clipRule="evenodd" />
    </svg>
  )
}
